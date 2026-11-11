using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session.Persistence;
using AionDpsMeter.Services.Services.Settings;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using AionDpsMeter.Services.Services.Timed;
using Microsoft.Extensions.DependencyInjection;

namespace AionDpsMeter.Services.Services.Session
{
    public sealed class CombatSessionManager
    {
        public event EventHandler<int>? PingUpdated;

        private readonly ConcurrentDictionary<int, TargetEntry> targetEntries = new();
        private readonly ActiveTargetResolver targetResolver;
        private readonly EntityTracker entityTracker;
        private readonly ILogger<CombatSessionManager> logger;
        private readonly Lock lockObject = new();
        private readonly IAppSettingsService settingsService;
        private readonly ICombatHistoryStore historyStore;
        private PlayerStatSnapshot? latestPlayerStatSnapshot;
        private readonly List<BuffEvent> activeBuffBacklog = new();
        private readonly ITimedEventTracker buffEventTracker;
        private readonly ITimedEventTracker skillCdEventTracker;
        private readonly CooldownTracker cooldowns;

        // All-targets (AoE) mode: one session that receives every damage event of a
        // pull, regardless of which mob was hit. A pull ends after a configurable gap.
        private const int AllTargetsSessionId = -1;

        // PvP activity for the recorder: hits between you and another player.
        private static readonly TimeSpan PvpEngagementGap = TimeSpan.FromSeconds(30);
        private DateTime pvpStartedAt;
        private DateTime pvpLastHitAt;
        private readonly Dictionary<int, string> pvpOpponents = new();

        /// <summary>Time of the last damage event of any kind (for "is a fight going on" checks).</summary>
        public DateTime LastDamageAt { get; private set; } = DateTime.MinValue;

        /// <summary>Time of your own last hit (other players fighting nearby don't count).</summary>
        public DateTime LastUserDamageAt { get; private set; } = DateTime.MinValue;

        /// <summary>A boss fight finished (after it was saved to History).</summary>
        public event Action<TargetCombatSession>? BossSessionCompleted;

        // Healing meter: one running tally per pull; a new one starts after a quiet gap or a Reset().
        private static readonly TimeSpan HealGap = TimeSpan.FromSeconds(30);
        private readonly Dictionary<int, HealTally> healTallies = new();
        private DateTime healStart, healLast;
        private int healSamplesLogged;
        private TargetCombatSession? allTargetsSession;
        private readonly HashSet<int> allTargetsMobIds = new();

        // Fights end when the mob dies (death packet, or HP 0 with no hits for a moment) or after the idle
        // timeout, checked every second, instead of only when the next hit on something else arrives.
        // The meter keeps showing the finished fight until a new one starts.
        private static readonly TimeSpan DeadMobGrace = TimeSpan.FromSeconds(1.5);
        private readonly Timer fightEndTimer;
        private TargetCombatSession? lastFinished;


        public CombatSessionManager(
            EntityTracker entityTracker,
            ILoggerFactory loggerFactory,
            IAppSettingsService settingsService,
            ICombatHistoryStore historyStore,
            [FromKeyedServices("Buffs")] ITimedEventTracker buffEventTracker,
            [FromKeyedServices("SkillCd")] ITimedEventTracker skillCdEventTracker,
            CooldownTracker cooldowns)
        {
            this.cooldowns = cooldowns;
            this.entityTracker = entityTracker;
            this.settingsService = settingsService;
            this.historyStore = historyStore;
            this.buffEventTracker = buffEventTracker;
            this.skillCdEventTracker = skillCdEventTracker;
            targetResolver = new ActiveTargetResolver(entityTracker);
            logger = loggerFactory.CreateLogger<CombatSessionManager>();
            entityTracker.SummonRegistered += OnSummonRegistered;
            fightEndTimer = new Timer(_ => SweepFinishedFights(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            // Switching Mob / All / Boss mode should change the shown fight right away.
            settingsService.SettingsChanged += (_, _) =>
            {
                lock (lockObject) { targetResolver.Update(targetEntries.Values, DateTime.Now, settingsService.BossFocus); }
            };
        }


        public void FirePingUpdate(int pingMs)
        {
            PingUpdated?.Invoke(this, pingMs);
        }

        public TargetCombatSession? GetActiveTargetSession()
        {
            lock (lockObject)
            {
                if (settingsService.AllTargetsMode && allTargetsSession is not null)
                    return BossInPull() ?? allTargetsSession;

                if (targetResolver.ActiveTargetId is { } id && targetEntries.TryGetValue(id, out var entry) && entry.CurrentSession is { } current)
                    return current;
                return lastFinished;
            }
        }


        public IEnumerable<TargetCombatSession> GetTargetHistory(int targetId)
        {
            lock (lockObject)
            {
                return targetEntries.TryGetValue(targetId, out var entry)
                    ? entry.AllSessions.ToList()
                    : [];
            }
        }

        public HistorySessionPageResult GetHistoryPage(HistorySessionQuery query)
        {
            lock (lockObject)
            {
                int pageNumber = Math.Max(1, query.PageNumber);
                int pageSize = Math.Clamp(query.PageSize, 1, 500);
                int globalSkip = (pageNumber - 1) * pageSize;

                var running = targetEntries.Values
                    .Select(e => e.CurrentSession)
                    .Where(s => s is not null && s is { IsCompleted: false, TargetInfo.IsBoss: true })
                    .Select(s => CreateListItem(s!))
                    .Where(s => IsMatch(s, query))
                    .OrderByDescending(x => x.SessionEnd)
                    .ToList();

                var runningIds = running.Select(x => x.SessionId).ToHashSet();
                int persistedCount = historyStore.GetSessionCount(
                    query.DateFrom,
                    query.DateTo,
                    query.BossNameContains,
                    runningIds);

                int totalCount = running.Count + persistedCount;

                int runningSkip = Math.Min(globalSkip, running.Count);
                int runningTake = Math.Min(pageSize, Math.Max(0, running.Count - runningSkip));
                var pageItems = running.Skip(runningSkip).Take(runningTake).ToList();

                int persistedTake = pageSize - pageItems.Count;
                if (persistedTake > 0)
                {
                    int persistedSkip = Math.Max(0, globalSkip - running.Count);
                    var persistedPage = historyStore.GetSessionPage(
                        query.DateFrom,
                        query.DateTo,
                        query.BossNameContains,
                        persistedSkip,
                        persistedTake,
                        runningIds);

                    pageItems.AddRange(persistedPage);
                }

                return new HistorySessionPageResult
                {
                    Items = pageItems,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                };
            }
        }

        public HistorySessionSnapshot? GetHistorySession(Guid sessionId)
        {
            lock (lockObject)
            {
                var live = targetEntries.Values
                    .SelectMany(e => e.AllSessions)
                    .FirstOrDefault(s => s.SessionId == sessionId);

                if (live is not null)
                    return HistorySessionSnapshot.From(live, cooldowns.Analyze(live.SessionStart, live.LastHitTime));

                return historyStore.GetSession(sessionId);
            }
        }

        /// <summary>Number of different mobs hit in the current all-targets pull (0 while a boss fight is shown instead).</summary>
        public int AllTargetsMobCount
        {
            get { lock (lockObject) { return BossInPull() is null ? allTargetsMobIds.Count : 0; } }
        }

        /// <summary>
        /// All-targets mode still gives a boss its own fight: while a boss is being fought (and after it dies,
        /// until the next pull starts) the meter shows that boss fight, so adds don't pad it and a pause in the
        /// fight doesn't split it. Trash pulls without a boss show the whole pull.
        /// </summary>
        private TargetCombatSession? BossInPull()
        {
            TargetCombatSession? boss = null;
            foreach (var entry in targetEntries.Values)
            {
                if (entry.CurrentSession is not { IsCompleted: false } s) continue;
                var mob = entityTracker.GetTargetMob(entry.TargetId) ?? s.TargetInfo;
                if (mob.IsBoss && (boss is null || s.LastHitTime > boss.LastHitTime)) boss = s;
            }
            if (boss is not null) return boss;
            return lastFinished is { TargetInfo.IsBoss: true } done && allTargetsSession is not null && done.LastHitTime >= allTargetsSession.SessionStart
                ? done : null;
        }

        public IReadOnlyCollection<PlayerStats> PlayerStats
        {
            get { lock (lockObject) { return GetActiveTargetSession()?.GetPlayerStats() ?? []; } }
        }

        public PlayerStatSnapshot? GetCurrentPlayerStatSnapshot()
        {
            lock (lockObject)
            {
                return latestPlayerStatSnapshot;
            }
        }

        public void ProcessPlayerStatsUpdate(PlayerCharacterStats stats, DateTime? capturedAt = null)
        {
            ArgumentNullException.ThrowIfNull(stats);

            lock (lockObject)
            {
                latestPlayerStatSnapshot = PlayerStatSnapshot.Create(stats, capturedAt);
            }
        }

        public void RegisterPlayerDeath(int playerId)
        {
            GetActiveTargetSession()?.RegisterPlayerDeath(playerId);
        }

        public double GetPartyDps()
        {
            double totalDamage = PlayerStats.Sum(r => r.TotalDamage);
            // At least 1 s: at the very start of a fight the duration is ~0 and the division blew up
            // to infinity (shown as "9223372036.85B/s").
            double seconds = Math.Max(1.0, GetCombatDuration().TotalSeconds);
            return totalDamage / seconds;
        }

        public IReadOnlyList<PlayerDamage> GetPlayerCombatLog(long playerId)
        {
            lock (lockObject)
            {
                return GetActiveTargetSession()?.GetCombatLog(playerId) ?? [];
            }
        }

        public IReadOnlyCollection<SkillStats> GetPlayerSkillStats(long playerId)
        {
            lock (lockObject)
            {
                return GetActiveTargetSession()?.GetSkillStats(playerId) ?? [];
            }
        }

        public IReadOnlyCollection<BuffStats> GetPlayerBuffStats(long playerId)
        {
            lock (lockObject)
            {
                var session = GetActiveTargetSession();
                if (session is null) return [];

                var sessionStart = session.SessionStart;
                var sessionEnd = session.LastHitTime;

                var buffs = session.GetBuffEvents(playerId, sessionStart, sessionEnd);
                return BuffStatisticsCalculator.ComputeBuffStats(buffs, sessionStart, sessionEnd);
            }
        }

        public (IReadOnlyList<Models.DpsDataPoint> Points, IReadOnlyList<Models.BuffTimelineEntry> BuffTimeline, double TotalSec)
            GetPlayerGraphData(long playerId)
        {
            lock (lockObject)
            {
                var session = GetActiveTargetSession();
                if (session is null)
                    return ([], [], 0);

                var hits = session.GetCombatLog(playerId);
                if (hits.Count == 0)
                    return ([], [], 0);

                var sessionStart = session.SessionStart;
                var sessionEnd   = session.LastHitTime;
                var buffEvents   = session.GetBuffEvents(playerId, sessionStart, sessionEnd);

                return GraphDataCalculator.Compute(hits, buffEvents);
            }
        }

        public TimeSpan GetCombatDuration()
        {
            lock (lockObject)
            {
                return GetActiveTargetSession()?.GetCombatDuration() ?? TimeSpan.Zero;
            }
        }

        /// <summary>
        /// The boss fight with the most recent hit that is still in progress, or null.
        /// Used by the boss recorder; independent of the meter's Mob / All / Boss view.
        /// </summary>
        public BossFightInfo? GetActiveBossFight()
        {
            lock (lockObject)
            {
                TargetCombatSession? best = null;
                Mob? bestMob = null;
                foreach (var entry in targetEntries.Values)
                {
                    var session = entry.CurrentSession;
                    if (session is null || session.IsCompleted) continue;
                    var mob = entityTracker.GetTargetMob(entry.TargetId) ?? session.TargetInfo;
                    if (!mob.IsBoss) continue;
                    if (best is null || session.LastHitTime > best.LastHitTime)
                    {
                        best = session;
                        bestMob = mob;
                    }
                }
                if (best is null) return null;
                // Current HP not received yet: estimate it as max HP minus the damage dealt this fight.
                long hp = bestMob!.HpCurrent > 0 || bestMob.HpTotal <= 0
                    ? bestMob.HpCurrent
                    : Math.Max(0, bestMob.HpTotal - best.GetPlayerStats().Sum(p => p.TotalDamage));
                return new BossFightInfo(bestMob.Name, best.SessionStart, best.LastHitTime, hp, bestMob.HpTotal, best.GetUserLastHitTime());
            }
        }

        /// <summary>The current PvP engagement (you hit a player or a player hit you), or null.</summary>
        public PvpFightInfo? GetPvpActivity()
        {
            lock (lockObject)
            {
                if (pvpOpponents.Count == 0) return null;
                var first = pvpOpponents.Values.First();
                return new PvpFightInfo(first, pvpOpponents.Count, pvpStartedAt, pvpLastHitAt);
            }
        }

        private void TrackPvp(PlayerDamage damageEvent)
        {
            int source = damageEvent.SourceEntity.Id;
            int target = damageEvent.TargetEntity.Id;
            if (source == target || entityTracker.GetPlayerEntity(target) is not { } targetPlayer) return;

            bool userAttacks = entityTracker.IsCurrentPlayer(source);
            bool userHit = entityTracker.IsCurrentPlayer(target);
            if (!userAttacks && !userHit) return;

            if (pvpOpponents.Count == 0 || damageEvent.DateTime - pvpLastHitAt > PvpEngagementGap)
            {
                pvpOpponents.Clear();
                pvpStartedAt = damageEvent.DateTime;
            }
            pvpLastHitAt = damageEvent.DateTime;
            var opponent = userAttacks ? targetPlayer : entityTracker.GetPlayerEntity(source);
            if (opponent is not null) pvpOpponents.TryAdd(opponent.Id, opponent.Name);
        }

        /// <summary>Your current DPS in the boss fight with the most recent hit (for personal-best comparison).</summary>
        public (string Boss, double UserDps)? GetActiveBossUserDps()
        {
            lock (lockObject)
            {
                TargetCombatSession? best = null;
                Mob? bestMob = null;
                foreach (var entry in targetEntries.Values)
                {
                    var session = entry.CurrentSession;
                    if (session is null || session.IsCompleted) continue;
                    var mob = entityTracker.GetTargetMob(entry.TargetId) ?? session.TargetInfo;
                    if (!mob.IsBoss) continue;
                    if (best is null || session.LastHitTime > best.LastHitTime) { best = session; bestMob = mob; }
                }
                var me = best?.GetPlayerStats().FirstOrDefault(p => p.IsUser);
                return best is null || me is null ? null : (bestMob!.Name, me.DamagePerSecond);
            }
        }

        // ------------------------------------------------------------------ healing meter

        public void ProcessHealEvent(int healerId, int targetId, long amount, int skillCode, CharacterClass? healerClass, DateTime at)
        {
            if (amount <= 0) return;
            lock (lockObject)
            {
                if (healSamplesLogged < 30)
                {
                    // Verification samples: heal packets are newly used, check the numbers look sane.
                    healSamplesLogged++;
                    logger.LogInformation("[HEAL] healer {Healer} -> target {Target} (player: {IsPlayer}) skill {Skill} amount {Amount}",
                        healerId, targetId, entityTracker.GetPlayerEntity(targetId) is not null, skillCode, amount);
                }
                if (healTallies.Count == 0 || at - healLast > HealGap)
                {
                    healTallies.Clear();
                    healStart = at;
                }
                healLast = at;
                var player = entityTracker.GetOrCreateSessionPlayer(healerId, healerClass);
                if (!healTallies.TryGetValue(healerId, out var tally))
                    healTallies[healerId] = tally = new HealTally();
                tally.Total += amount;
                tally.Hits++;
                tally.First ??= at;
                tally.Last = at;
                _ = player;
            }
        }

        public IReadOnlyList<HealStat> GetHealStats()
        {
            lock (lockObject)
            {
                long total = healTallies.Values.Sum(t => t.Total);
                return healTallies.Select(kv =>
                {
                    var p = entityTracker.GetPlayerEntity(kv.Key);
                    var t = kv.Value;
                    double secs = Math.Max(1, ((t.Last ?? healLast) - (t.First ?? healStart)).TotalSeconds);
                    return new HealStat(kv.Key, p?.Name ?? $"Player_{kv.Key}", p?.ServerName ?? "", (int)(p?.CharacterClass?.Id ?? 0),
                        p?.CharacterClass?.Name ?? "", p?.IsUser == true, p?.IsIdentified == true,
                        t.Total, t.Total / secs, total > 0 ? t.Total * 100.0 / total : 0, t.Hits);
                }).OrderByDescending(s => s.Total).ToList();
            }
        }

        public TimeSpan GetHealDuration()
        {
            lock (lockObject) { return healTallies.Count == 0 ? TimeSpan.Zero : healLast - healStart; }
        }

        private sealed class HealTally
        {
            public long Total;
            public int Hits;
            public DateTime? First;
            public DateTime? Last;
        }

        /// <summary>Damage dealt to one mob in its current fight (for HP estimates).</summary>
        public long GetTargetDamage(int targetId)
        {
            lock (lockObject)
            {
                return targetEntries.TryGetValue(targetId, out var entry) && entry.CurrentSession is { } s
                    ? s.GetPlayerStats().Sum(p => p.TotalDamage)
                    : 0;
            }
        }

        public Mob? GetActiveTargetInfo()
        {
            // The mob of the fight the meter shows (a boss in AoE mode, or the finished fight), so the header
            // matches the numbers; an AoE pull itself falls back to the mob you're hitting.
            lock (lockObject)
            {
                var shown = GetActiveTargetSession();
                if (shown is null || shown.TargetId == AllTargetsSessionId) return targetResolver.GetActiveTargetMob();
                return entityTracker.GetTargetMob(shown.TargetId) ?? shown.TargetInfo;
            }
        }



        /// <summary>The game reported an entity's death: a mob being fought ends its fight right away.</summary>
        public void OnEntityDied(int entityId)
        {
            lock (lockObject)
            {
                if (entityTracker.GetTargetMob(entityId) is { } mob) mob.DeathSeen = true;
                if (targetEntries.TryGetValue(entityId, out var entry) && entry.CurrentSession is { IsCompleted: false })
                    FinishFight(entityId, entry);
            }
        }

        private void SweepFinishedFights()
        {
            try
            {
                lock (lockObject)
                {
                    var now = DateTime.Now;
                    foreach (var (id, entry) in targetEntries)
                    {
                        if (entry.CurrentSession is not { IsCompleted: false } session) continue;
                        var mob = entityTracker.GetTargetMob(id) ?? session.TargetInfo;
                        bool dead = mob.IsDead && !mob.IsDummy && now - session.LastHitTime > DeadMobGrace;
                        if (dead || entry.IsIdle(now)) FinishFight(id, entry);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fight end check failed");
            }
        }

        private void FinishFight(int targetId, TargetEntry entry)
        {
            var session = entry.CurrentSession;
            if (session is not null) LogFightCheck(session);
            entry.CompleteActiveSession(); // saves boss fights to History and raises BossSessionCompleted
            targetEntries.TryRemove(targetId, out _);
            if (session is not null && (targetResolver.ActiveTargetId == targetId || lastFinished is null || session.LastHitTime >= lastFinished.LastHitTime))
                lastFinished = session;
        }

        /// <summary>
        /// Accuracy check in the log for each boss fight: damage counted vs the boss's max HP, and how much HP it
        /// gained back during the fight. Damage ≈ max HP + regained HP means nothing was missed or double counted.
        /// </summary>
        private void LogFightCheck(TargetCombatSession session)
        {
            var mob = entityTracker.GetTargetMob(session.TargetId) ?? session.TargetInfo;
            if (!mob.IsBoss || mob.HpTotal <= 0) return;
            long damage = session.GetPlayerStats().Sum(p => p.TotalDamage);
            long regained = mob.HpRegained - session.HpRegainedAtStart;
            long expected = mob.HpTotal + regained;
            logger.LogInformation("[FIGHT] {Boss}: {Result}, damage {Damage:N0} vs max HP {Max:N0} ({Pct:F1}%), boss regained {Regained:N0} HP → damage is {Check:F1}% of max HP + regained",
                mob.Name, mob.IsDead ? "killed" : "not killed", damage, mob.HpTotal, damage * 100.0 / mob.HpTotal, regained, damage * 100.0 / expected);
        }

        public void ProcessDamageEvent(PlayerDamage damageEvent)
        {
            try
            {
                lock (lockObject)
                {
                    // "Only me + party": strangers hitting the same mob (e.g. the training dummy) are ignored entirely.
                    if (settingsService.PartyOnly && !entityTracker.IsUserOrParty(damageEvent.SourceEntity)) return;
                    // "Only fights you're in": a mob counts from the first hit by you or your party (in a dungeon
                    // that's every mob); other players' fights on mobs you never touched are ignored.
                    if (settingsService.OnlyMyFights && damageEvent.TargetEntity is { } target && entityTracker.HasKnownUser) // never filter blind
                    {
                        if (entityTracker.IsUserOrParty(damageEvent.SourceEntity))
                        {
                            if (engagedTargets.Count > 20_000) engagedTargets.Clear();
                            engagedTargets.Add(target.Id);
                        }
                        else if (!engagedTargets.Contains(target.Id)) return;
                    }
                    LastDamageAt = DateTime.Now;
                    if (damageEvent.SourceEntity?.IsUser == true) LastUserDamageAt = LastDamageAt;
                    TrackPvp(damageEvent);

                    if (settingsService.BossOnlyCapture)
                    {
                        var mob = entityTracker.GetTargetMob(damageEvent.TargetEntity.Id) ?? damageEvent.TargetEntity;
                        if (!mob.IsBoss) return;
                    }

                    if (entityTracker.IsSummon(damageEvent.SourceEntity.Id) &&
                        !ResolveSummonSource(damageEvent))
                        return;
                    // Late hits / DoT ticks on a mob that already died must not start a new "fight" on the corpse:
                    // that junk fight (a few hundred damage) replaced the real kill on the meter, in the fight
                    // summary and in the kill timers. A respawned mob gets HP again, so it is not dead any more.
                    if (!targetEntries.ContainsKey(damageEvent.TargetEntity.Id) &&
                        entityTracker.GetTargetMob(damageEvent.TargetEntity.Id) is { IsDead: true })
                        return;
                    RouteToTargetEntry(damageEvent);
                    AddToAllTargetsSession(damageEvent);

                    // Check all other entries for idle timeout on each new event
                    CheckIdleTimeouts(damageEvent.DateTime, excludeTargetId: damageEvent.TargetEntity.Id);

                    targetResolver.Update(targetEntries.Values, damageEvent.DateTime, settingsService.BossFocus);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing damage event");
            }
        }

        public void ProcessBuffEvent(BuffEvent buffEvent)
        {
            try
            {
                lock (lockObject)
                {
                    RegisterTimedBuffEvent(buffEvent);
                    activeBuffBacklog.RemoveAll(b => b.AppliedAt.AddMilliseconds(b.DurationMs) <= buffEvent.AppliedAt);
                    activeBuffBacklog.Add(buffEvent);

                    foreach (var targetEntry in AllTargetEntries)
                    {
                        targetEntry.TryAddBuff(buffEvent);

                    }
                    allTargetsSession?.ProcessBuffEvent(buffEvent);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing buff event");
            }
        }

        public void RegisterSkillCdEvent(TimedEvent skillCdEvent)
        {
            skillCdEventTracker.Register(skillCdEvent);
        }

        private void RegisterTimedBuffEvent(BuffEvent buffEvent)
        {
            if (!entityTracker.IsCurrentPlayer(buffEvent.EntityId)) return;
            buffEventTracker.Register(new TimedEvent()
            {
                Name = buffEvent.BuffName,
                IconUrl = buffEvent.BuffIcon ?? string.Empty,
                Duration = TimeSpan.FromMilliseconds(buffEvent.DurationMs),
                Id = (uint)buffEvent.BuffId
            });
        }

        /// <summary>Mobs you or your party have hit (lock: lockObject), for "only fights you're in".</summary>
        private readonly HashSet<int> engagedTargets = new();

        public void Reset()
        {
            lock (lockObject) { ResetInternal(); engagedTargets.Clear(); }
        }

        public void CompleteAndPersistActiveSessions(DateTime? at = null)
        {
            lock (lockObject)
            {
                var completedAt = at ?? DateTime.Now;
                foreach (var entry in targetEntries.Values)
                {
                    entry.CompleteActiveSession();
                }

                targetResolver.Update(targetEntries.Values, completedAt, settingsService.BossFocus);
            }
        }

        private IReadOnlyCollection<TargetEntry> AllTargetEntries
        {
            get { lock (lockObject) { return targetEntries.Values.ToList(); } }
        }


        private void RouteToTargetEntry(PlayerDamage damageEvent)
        {
            var entry = targetEntries.GetOrAdd(
                damageEvent.TargetEntity.Id,
                id => new TargetEntry(id, entityTracker, settingsService, OnSessionCompleted));

            var previousSession = entry.CurrentSession;
            entry.AddDamage(damageEvent);
            var currentSession = entry.CurrentSession;

            if (!ReferenceEquals(previousSession, currentSession) && currentSession is not null)
            {
                ReplayPreCombatBuffs(currentSession);
            }
        }

        private void AddToAllTargetsSession(PlayerDamage damageEvent)
        {
            var gap = TimeSpan.FromSeconds(settingsService.AllTargetsGapSeconds);
            if (allTargetsSession is null || damageEvent.DateTime - allTargetsSession.LastHitTime > gap)
            {
                allTargetsSession?.Reset();
                allTargetsMobIds.Clear();
                // HpCurrent > 0 so player deaths still register on this pseudo target.
                var pseudoTarget = new Mob { Id = AllTargetsSessionId, HpCurrent = 1 };
                allTargetsSession = new TargetCombatSession(pseudoTarget, damageEvent.DateTime, entityTracker, settingsService);
                ReplayPreCombatBuffs(allTargetsSession);
            }

            allTargetsMobIds.Add(damageEvent.TargetEntity.Id);
            allTargetsSession.AddDamage(damageEvent);
        }

        private void ReplayPreCombatBuffs(TargetCombatSession session)
        {
            var sessionStart = session.SessionStart;
            foreach (var buff in activeBuffBacklog)
            {
                if (buff.AppliedAt >= sessionStart)
                    continue;

                var buffEnd = buff.AppliedAt.AddMilliseconds(buff.DurationMs);
                if (buffEnd <= sessionStart)
                    continue;

                session.ProcessBuffEvent(buff);
            }
        }

        private void OnSessionCompleted(TargetCombatSession session)
        {
            try
            {
                historyStore.Save(HistorySessionSnapshot.From(session, cooldowns.Analyze(session.SessionStart, session.LastHitTime)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist completed combat session {SessionId}", session.SessionId);
            }
            try { BossSessionCompleted?.Invoke(session); }
            catch (Exception ex) { logger.LogError(ex, "BossSessionCompleted handler failed"); }
        }

        private static HistorySessionListItem CreateListItem(TargetCombatSession session)
        {
            var playerStats = session.GetPlayerStats();
            return new HistorySessionListItem
            {
                SessionId = session.SessionId,
                TargetId = session.TargetId,
                TargetName = session.TargetInfo.Name,
                TargetHpTotal = session.TargetInfo.HpTotal,
                SessionStart = session.SessionStart,
                SessionEnd = session.LastHitTime,
                State = session.State,
                TotalDamage = playerStats.Sum(p => p.TotalDamage),
                PlayerCount = playerStats.Count(p => p.IsIdentified || p.DamagePercentage > 1),
            };
        }

        private bool IsMatch(HistorySessionListItem item, HistorySessionQuery query)
        {
            if (query.DateFrom is { } from && item.SessionEnd < from)
                return false;

            if (query.DateTo is { } to && item.SessionEnd > to)
                return false;

            if (!string.IsNullOrWhiteSpace(query.BossNameContains))
            {
                var search = query.BossNameContains.Trim();
                if (item.TargetName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // Player name filtering requires checking the full session snapshot
            if (!string.IsNullOrWhiteSpace(query.PlayerNameContains))
            {
                var snapshot = GetHistorySession(item.SessionId);
                if (snapshot is null) return false;

                var search = query.PlayerNameContains.Trim();
                bool hasMatchingPlayer = snapshot.PlayerStats
                    .Any(p => p.PlayerName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!hasMatchingPlayer) return false;
            }

            return true;
        }

        private void CheckIdleTimeouts(DateTime now, int excludeTargetId)
        {
            foreach (var (id, entry) in targetEntries)
            {
                if (id == excludeTargetId) continue;
                entry.CheckIdleTimeout(now);

                // A completed session is already persisted and dropped by its entry;
                // the empty entry would otherwise stay forever (one per mob ever hit)
                // and make this loop slower on every hit during long grinding.
                if (entry.CurrentSession is null)
                    targetEntries.TryRemove(id, out _);
            }
        }

        private bool ResolveSummonSource(PlayerDamage damageEvent)
        {
            var ownerId = entityTracker.GetSummonOwner(damageEvent.SourceEntity.Id);
            if (ownerId is null)
            {
                logger.LogError("Summon owner not found for summon entity {SummonId}", damageEvent.SourceEntity.Id);
                return false;
            }
            damageEvent.SourceSummon = new Player
            {
                CharacterClass = damageEvent.SourceEntity.CharacterClass,
                Icon = damageEvent.SourceEntity.Icon,
                Name = damageEvent.SourceEntity.Name,
                Id = damageEvent.SourceEntity.Id,
            };
            damageEvent.SourceEntity = new Player
            {
                CharacterClass = damageEvent.SourceEntity.CharacterClass,
                Icon = damageEvent.SourceEntity.Icon,
                Name = damageEvent.SourceEntity.Name,
                Id = ownerId.Value,
            };
            return true;
        }

        private void ResetInternal()
        {
            foreach (var entry in targetEntries.Values)
                entry.Reset();
            targetEntries.Clear();
            targetResolver.Reset();
            allTargetsSession?.Reset();
            allTargetsSession = null;
            allTargetsMobIds.Clear();
            lastFinished = null;
            healTallies.Clear();
            latestPlayerStatSnapshot = null;
            activeBuffBacklog.Clear();
        }

        private void OnSummonRegistered(int summonId, int ownerId)
        {
            lock (lockObject)
            {
                int totalTransferred = 0;
                int entriesAffected  = 0;

                foreach (var (targetId, entry) in targetEntries)
                {
                    if (entry.CurrentSession is not { } current) continue;

                    int result = current.TransferSummonDamage(summonId, ownerId);
                    if (result == 0)
                    {
                        logger.LogWarning(
                            "Summon late-registration: summon {SummonId} found in target entry {TargetId} but owner player entity {OwnerId} is unknown. Skipping transfer.",
                            summonId, targetId, ownerId);
                    }
                    else if (result > 0)
                    {
                        totalTransferred += result;
                        entriesAffected++;
                        logger.LogInformation(
                            "Summon late-registration: transferred {Count} hit(s) from summon {SummonId} to owner {OwnerId} in target entry {TargetId}.",
                            result, summonId, ownerId, targetId);
                    }
                }

                allTargetsSession?.TransferSummonDamage(summonId, ownerId);

                if (totalTransferred == 0)
                    logger.LogDebug(
                        "Summon late-registration: summon {SummonId} registered with owner {OwnerId} — no prior damage hits found to transfer.",
                        summonId, ownerId);
                else
                    logger.LogInformation(
                        "Summon late-registration complete: summon {SummonId} → owner {OwnerId}. Total hits transferred: {Total} across {Entries} target entry(ies).",
                        summonId, ownerId, totalTransferred, entriesAffected);
            }
        }
    }

    public sealed record HealStat(long PlayerId, string Name, string ServerName, int ClassId, string ClassName,
        bool IsUser, bool IsIdentified, long Total, double PerSecond, double Percentage, int Hits);

    public sealed record PvpFightInfo(string Opponent, int OpponentCount, DateTime StartedAt, DateTime LastHitAt);

    /// <param name="UserLastHitAt">When you last hit this boss (null = you haven't hit it).</param>
    public sealed record BossFightInfo(string Name, DateTime StartedAt, DateTime LastHitAt, long HpCurrent, long HpTotal, DateTime? UserLastHitAt);
}
