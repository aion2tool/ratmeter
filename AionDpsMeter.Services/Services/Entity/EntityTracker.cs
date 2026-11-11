using AionDpsMeter.Core.Models;
using System.Diagnostics;
using System.IO;

namespace AionDpsMeter.Services.Services.Entity
{

    public sealed class EntityTracker
    {
        // Fired when a summon is registered: (summonId, ownerId)
        public event Action<int, int>? SummonRegistered;

        // Fired when the game sends your own character info (login / zone load): your entity id.
        public event Action<int>? UserInfoReceived;

        public List<Player> PlayerEntities => sessionPlayers.Values.ToList();
        public List<Mob> TargetEntities => targetEntities.Values.ToList();
        public int PlayerEntityCount => sessionPlayers.Count;
        public int TargetEntityCount => targetEntities.Count;
        public int SummonCount => summons.Count;

        // Session id -> player.
        private readonly Dictionary<int, Player> sessionPlayers = [];

        // Global id -> player.
        private readonly Dictionary<int, Player> globalPlayers = [];

        private readonly Dictionary<int, Mob> targetEntities = [];
        private readonly Dictionary<int, int> summons = []; // summonId -> ownerId

        private string currentUserName = string.Empty;

        private const int UnknownEntityHpThreshold = 1_000_000;

        // ----- Your own character across meter restarts -----------------------
        // The game sends your character info (name, "this is you") only on login / zone load. A meter started
        // mid-zone used to show you as "Player_<id>" (no highlight, no boss recordings) until the next zone.
        // So the last one is kept in last-user.json and restored at start; the next zone load replaces it, and
        // if that id turns out to be someone else, SetSessionPlayerName drops it (id reuse handling).
        private static readonly string LastUserFile = Path.Combine(AppContext.BaseDirectory, "last-user.json");
        private static readonly TimeSpan LastUserMaxAge = TimeSpan.FromHours(6);

        private sealed record SavedUser(int EntityId, string Name, string ServerName, int CombatPower, DateTime SavedAtUtc, int ServerId = 0);

        public EntityTracker()
        {
            RestoreLastUser();
        }

        private void RestoreLastUser()
        {
            try
            {
                if (!File.Exists(LastUserFile)) return;
                var saved = System.Text.Json.JsonSerializer.Deserialize<SavedUser>(File.ReadAllText(LastUserFile));
                if (saved is null || string.IsNullOrWhiteSpace(saved.Name) || DateTime.UtcNow - saved.SavedAtUtc > LastUserMaxAge) return;
                sessionPlayers[saved.EntityId] = new Player
                {
                    Id = saved.EntityId,
                    Name = saved.Name,
                    ServerName = saved.ServerName,
                    ServerId = saved.ServerId,
                    CombatPower = saved.CombatPower,
                    IsIdentified = true,
                    IsUser = true,
                };
                currentUserName = saved.Name;
            }
            catch (Exception) { /* unreadable: wait for the next zone load */ }
        }

        private static void SaveLastUser(int entityId, Player player)
        {
            try
            {
                File.WriteAllText(LastUserFile, System.Text.Json.JsonSerializer.Serialize(
                    new SavedUser(entityId, player.Name, player.ServerName, player.CombatPower, DateTime.UtcNow, player.ServerId)));
            }
            catch (Exception) { /* not critical */ }
        }


        // ----- Targets (mobs) --------------------------------------------------

        public Mob GetOrCreateTargetEntity(int entityId)
        {
            if (targetEntities.TryGetValue(entityId, out var entity)) return entity;

            entity = new Mob { Id = entityId };
            targetEntities[entityId] = entity;
            return entity;
        }

        public bool IsIdentifiedPlayer(int entityId) => sessionPlayers.TryGetValue(entityId, out var player) && player.IsIdentified;

        public bool IsCurrentPlayer(int entityId) => sessionPlayers.TryGetValue(entityId, out var player) && player.IsUser;


        public bool UpdateTargetEntityHpCurrent(int entityId, long hpCurrent)
        {
            if (!targetEntities.TryGetValue(entityId, out var entity))
            {
                if (hpCurrent < UnknownEntityHpThreshold) return false;
                entity = CreateOrUpdateTargetEntity(entityId, 0, 0);
            }

            if (entity.HpKnown && entity.HpCurrent > 0 && hpCurrent > entity.HpCurrent)
                entity.HpRegained += hpCurrent - entity.HpCurrent; // healed / regenerated mid-fight
            entity.HpCurrent = hpCurrent;
            entity.HpKnown = true;
            if (hpCurrent > 0) entity.DeathSeen = false; // respawned / reset
            // Max HP comes from a byte scan of the spawn packet and is sometimes a wrong value (66, 67,
            // or a fifth of the real HP). Current HP above it proves it wrong, so raise it. This used to
            // need a 10M gap, so those bosses never corrected (History showed 5x to 78,000x their HP).
            if (entity.HpCurrent > entity.HpTotal)
            {
                entity.HpTotal = entity.HpCurrent;
            }
            return true;
        }

        public Mob CreateOrUpdateTargetEntity(int entityId, int mobCode, long hpTotal = 0)
        {
            if (targetEntities.TryGetValue(entityId, out var entity))
            {
                entity.MobCode = mobCode;
                if (hpTotal > 0) entity.HpTotal = hpTotal;
                return entity;
            }

            var mob = new Mob
            {
                Id = entityId,
                MobCode = mobCode,
                HpTotal = hpTotal,
            };

            targetEntities[entityId] = mob;
            return mob;
        }

        public Core.Models.Entity? GetTargetEntity(int entityId) => targetEntities.GetValueOrDefault(entityId);

        public Mob? GetTargetMob(int entityId) => targetEntities.GetValueOrDefault(entityId);

        // ----- Flow 1: server sends a player tied to a session id --------------

        public Player GetOrCreateSessionPlayer(int sessionId, CharacterClass? characterClass = null)
        {
            if (sessionPlayers.TryGetValue(sessionId, out var player))
            {
                player.CharacterClass ??= characterClass;
                return player;
            }

            player = new Player
            {
                Id = sessionId,
                Name = $"Player_{sessionId}",
                CharacterClass = characterClass,
            };

            sessionPlayers[sessionId] = player;
            CleanupUnidSessionPlayers();
            return player;
        }

        public void CleanupUnidSessionPlayers()
        {
            var toRemove = sessionPlayers.Where(r=>!r.Value.IsIdentified && r.Value.CreatedAt < DateTime.Now.AddMinutes(-10)).ToList();
            foreach (var kvp in toRemove) sessionPlayers.Remove(kvp.Key);
        }

        public void SetSessionPlayerName(int sessionId, string name, int combatPower, string serverName = "", bool isUser = false, int serverId = 0)
        {
            if (sessionPlayers.TryGetValue(sessionId, out var existing) && existing.IsIdentified && existing.Name != name)
            {
                // Same session id, but it was already confirmed as a different
                // player - the id has been reused for someone else. Drop the
                // stale entity (and whatever it was linked to) and start fresh.
                sessionPlayers.Remove(sessionId);
            }

            var player = GetOrCreateSessionPlayer(sessionId);

            player.Name = name;
            if(combatPower > 0) player.CombatPower = combatPower;
            if (!string.IsNullOrEmpty(serverName)) player.ServerName = serverName;
            // Numeric game server id: the gear-score lookup needs it to find the right same-named character.
            if (serverId > 0) player.ServerId = serverId;
            player.IsIdentified = true;
            player.IsUser = isUser || name == currentUserName;

            if (isUser)
            {
                SaveLastUser(sessionId, player);
                SetCurrentUser(name);
                UserInfoReceived?.Invoke(sessionId);
            }
            TryFallbackLinkByName(name);
        }

        public Player? GetPlayerEntity(int sessionId) => sessionPlayers.GetValueOrDefault(sessionId);

        // ----- Your party (the game sends the whole member list each time) ------
        private volatile HashSet<int> partyGlobalIds = new();
        private volatile HashSet<string> partyNames = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The party list from the latest party packet replaces the previous one.</summary>
        public void SetParty(IReadOnlyCollection<Player> members)
        {
            partyGlobalIds = members.Select(m => m.Id).ToHashSet();
            partyNames = members.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The meter knows which player is you (from this session or the saved last character).</summary>
        public bool HasKnownUser => sessionPlayers.Values.Any(p => p.IsUser);

        /// <summary>You, or someone in your current party (by global id, or by name until the ids are linked).</summary>
        public bool IsUserOrParty(Player? p) =>
            p is not null && (p.IsUser || (p.GlobalId is int g && partyGlobalIds.Contains(g)) || (p.IsIdentified && partyNames.Contains(p.Name)));

        // ----- Flow 2: server sends global player metadata, any time -----------

        public void RegisterOrUpdateGlobalPlayer(Player data)
        {
            var globalId = data.Id;

            if (!globalPlayers.TryGetValue(globalId, out var identity))
            {
                identity = new Player { Id = globalId };
                globalPlayers[globalId] = identity;
            }

            identity.Name = data.Name;
            identity.CharacterLevel = data.CharacterLevel;
            identity.CombatPower = data.CombatPower;
            if (data.GearScore > 0) identity.GearScore = data.GearScore;
            identity.ServerId = data.ServerId;
            identity.ServerName = data.ServerName;
            if (data.CharacterClass != null) identity.CharacterClass = data.CharacterClass;
            identity.IsIdentified = true;
            identity.IsUser = identity.IsUser || identity.Name == currentUserName;

            PropagateIdentityToLinkedSessions(globalId);
            TryFallbackLinkByName(identity.Name);
        }

        // ----- Flow 3: packet carries both a global id and a session id --------

        public void LinkSessionToGlobalPlayer(int globalId, int sessionId)
        {
            var session = GetOrCreateSessionPlayer(sessionId);
            session.GlobalId = globalId;

            if (!globalPlayers.TryGetValue(globalId, out var identity))
            {

                identity = new Player
                {
                    Id = globalId,
                    Name = session.Name,
                    CharacterLevel = session.CharacterLevel,
                    CombatPower = session.CombatPower,
                    GearScore = session.GearScore,
                    ServerId = session.ServerId,
                    ServerName = session.ServerName,
                    CharacterClass = session.CharacterClass,
                    IsUser = session.IsUser,
                    IsIdentified = session.IsIdentified,
                };
                globalPlayers[globalId] = identity;
            }

            ApplyIdentity(session, identity);
        }

        private void PropagateIdentityToLinkedSessions(int globalId)
        {
            if (!globalPlayers.TryGetValue(globalId, out var identity)) return;

            foreach (var session in sessionPlayers.Values)
            {
                if (session.GlobalId == globalId) ApplyIdentity(session, identity);
            }
        }

        private static void ApplyIdentity(Player session, Player identity)
        {
            // Only a confirmed (server-given) name should ever overwrite a session's name.
            if (identity.IsIdentified)
            {
                session.Name = identity.Name;
                session.IsIdentified = true;
            }

            session.CharacterLevel = identity.CharacterLevel;
            session.CombatPower = identity.CombatPower;
            if (identity.GearScore > 0) session.GearScore = identity.GearScore;
            if (identity.ServerId > 0) session.ServerId = identity.ServerId;
            if (!string.IsNullOrEmpty(identity.ServerName)) session.ServerName = identity.ServerName;
            session.CharacterClass ??= identity.CharacterClass;
            session.IsUser = session.IsUser || identity.IsUser;
            session.GlobalId = identity.Id;
        }

        // Link packets are sometimes missing entirely. As a fallback, if we
        // see the same name show up on both an unlinked session player and a
        // global identity, assume they're the same person and link them.
        private void TryFallbackLinkByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            var session = sessionPlayers.Values.FirstOrDefault(p => p.GlobalId == null && p.Name == name);
            if (session == null) return;

            var identity = globalPlayers.Values.FirstOrDefault(p => p.Name == name);
            if (identity == null) return;

            session.GlobalId = identity.Id;
            ApplyIdentity(session, identity);
        }

        private void SetCurrentUser(string name)
        {
            currentUserName = name;


            foreach (var player in sessionPlayers.Values)
            {
                if (player.Name == name) player.IsUser = true;
            }
            foreach (var identity in globalPlayers.Values)
            {
                if (identity.Name == name) identity.IsUser = true;
            }
        }

        // ----- Summons -----------------------------------------------------------

        public void RegisterSummon(int summonId, int ownerId)
        {
            summons[summonId] = ownerId;
            SummonRegistered?.Invoke(summonId, ownerId);
        }

        public bool IsSummon(int entityId) => summons.ContainsKey(entityId);

        public int? GetSummonOwner(int summonId) => summons.TryGetValue(summonId, out var ownerId) ? ownerId : null;

        public void Clear()
        {
            // Players persist across a Clear() (e.g. a new pull in the same session);
            // only per-fight bookkeeping like summons resets.
            summons.Clear();
        }
    }
}