using AionDpsMeter.Core.Windowing;
using AionDpsMeter.Services.Services.Pets;
using AionDpsMeter.Services.Services.Roster;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.UI.Services.Pets;
using AionDpsMeter.UI.Services.Roster;
using AionDpsMeter.UI.Services.Timers;
using AionDpsMeter.UI.Services.Windowing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AionDpsMeter.UI.Pages
{
    /// <summary>
    /// Control Panel: every character's week (gathering, crafting, energy, Kina, boss clears, hand-ticked tasks)
    /// and the event schedule. Opened by the user only (⋯ menu / Events hotkey) — never pops up.
    /// </summary>
    partial class ControlPanelPage(RosterService Svc, EventTimerService Timers, FieldBossTimers FieldBosses, KillTimerService Kills, PetTracker Pets,
        IAppSettingsService Settings, WindowHelper Windows, IWindowManagerService WindowManager)
    {
        private const int PureOdyle = 610610015;
        private static readonly HashSet<int> Odyles = [610610013, 610610014, 610610015, 610610016];
        private string Tab = "roster";
        private System.Threading.Timer? tick;
        private IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Clears = new Dictionary<string, IReadOnlyDictionary<string, int>>();
        private string NewName = "";
        private int NewMax = 1;
        private bool NewServer;
        private bool NewDaily;

        private static DateTime Now => DateTime.UtcNow;
        private RosterData Data => Svc.Snapshot;
        private List<TaskDef> EditDefs => Data.TaskDefs;

        private sealed record CharRow(string Name, CharacterWeek Week, bool IsCurrent);

        protected override void OnInitialized()
        {
            Svc.Changed += Refresh;
            Pets.Changed += Refresh;
            Clears = Svc.ClearsThisWeek();
            tick = new System.Threading.Timer(_ => { Svc.RollOverIfDue(); Clears = Svc.ClearsThisWeek(); Refresh(); }, null, 1000, 1000);
        }

        private void Refresh() => InvokeAsync(StateHasChanged);

        private bool PetsShowAll;
        private static readonly string[] ElyosMaps = ["Verteron", "Eltnen"];
        private static readonly string[] AsmodianMaps = ["Altgard", "Morheim"];

        private sealed record PetRow(PetInfo Pet, PetProgress Prog, int Left, int KillsLeft);

        private List<PetRow> PetRows()
        {
            var data = Pets.Snapshot();
            return Pets.Catalog.Pets
                .Select(p =>
                {
                    var prog = data.Pets.GetValueOrDefault(p.Name) ?? new PetProgress();
                    int left = PetLevels.Left(prog.Level, prog.Souls);
                    double best = p.Sources.Length > 0 ? p.Sources.Max(s => s.Chance) : 0;
                    int kills = best > 0 ? (int)Math.Ceiling(left / (best / 100.0)) : 0;
                    return new PetRow(p, prog, left, kills);
                })
                .Where(r => PetsShowAll || r.Prog.Level < PetLevels.Max)
                .OrderBy(r => r.Prog.Set ? 0 : 1).ThenBy(r => r.Left).ThenBy(r => r.Pet.Name)
                .ToList();
        }

        private int UnsetPets() { var d = Pets.Snapshot(); return Pets.Catalog.Pets.Count(p => d.Pets.GetValueOrDefault(p.Name) is not { Set: true }); }

        /// <summary>Sources on the user's side first (Elyos: Verteron/Eltnen, Asmodian: Altgard/Morheim).</summary>
        private IEnumerable<PetSource> SourcesFor(PetInfo p)
        {
            var mine = Kills.AsmodianSide ? AsmodianMaps : ElyosMaps;
            return p.Sources.OrderBy(s => s.Places.Any(pl => mine.Any(m => pl.StartsWith(m))) ? 0 : 1).ThenByDescending(s => s.Chance);
        }

        private void SetPetLevel(PetRow r, ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out int level)) Pets.Set(r.Pet.Name, level, level == r.Prog.Level ? r.Prog.Souls : 0);
        }

        private void SetPetSouls(PetRow r, ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out int souls)) Pets.Set(r.Pet.Name, r.Prog.Level, souls);
        }

        public void Dispose()
        {
            Svc.Changed -= Refresh;
            Pets.Changed -= Refresh;
            tick?.Dispose();
        }

        // ---- roster
        private IEnumerable<CharRow> Characters() =>
            Data.Characters
                .Select(kv => new CharRow(kv.Key, kv.Value, string.Equals(kv.Key, Svc.Book.Current, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(r => r.IsCurrent).ThenByDescending(r => TasksLeft(r.Week)).ThenByDescending(r => r.Week.LastSeenUtc)
                .ToList();

        private IEnumerable<TaskDef> Defs(bool server) => Data.TaskDefs.Where(t => t.Server == server && !t.Hidden);
        private int ServerValue(string id) => Data.ServerTasks.GetValueOrDefault(id);
        private int Pending => Svc.Book.Current is null ? Svc.Book.PendingCount : 0;
        private int TasksLeft(CharacterWeek w) => Defs(false).Count(t => w.Tasks.GetValueOrDefault(t.Id) < t.Max);
        private static string? Opened;   // character the user expanded; null = the one you're playing
        private static bool IsOpen(CharRow c) => Opened is null ? c.IsCurrent : string.Equals(Opened, c.Name, StringComparison.OrdinalIgnoreCase);
        private static void Toggle(CharRow c) => Opened = IsOpen(c) ? "" : c.Name;

        /// <summary>The alt (not the one you're on) with the most left this week; ties go to the one not seen longest.</summary>
        private CharRow? BestNext() =>
            Characters().Where(r => !r.IsCurrent && TasksLeft(r.Week) > 0)
                .OrderByDescending(r => TasksLeft(r.Week)).ThenBy(r => r.Week.LastSeenUtc).FirstOrDefault();

        private static string CraftTitle(CharacterWeek w) =>
            "Odyle Energy crafted this week" + (RosterRecipes.OtherCrafts(w) > 0 ? $" (+{RosterRecipes.OtherCrafts(w)} other morphs)" : "");

        private static string GatherTitle(CharacterWeek w) =>
            $"Pure Odyle this week · Odyle {w.Gathered.GetValueOrDefault(610610013)} · Fine {w.Gathered.GetValueOrDefault(610610014)} · Radiant {w.Gathered.GetValueOrDefault(610610016)}{OtherGathered(w)}";

        // Small line icons (inherit the chip colour).
        private static readonly MarkupString IconGem = new("<svg viewBox='0 0 24 24' class='ic'><path d='M6 3h12l4 6-10 12L2 9z'/><path d='M2 9h20M9 3l3 18 3-18'/></svg>");
        private static readonly MarkupString IconFlask = new("<svg viewBox='0 0 24 24' class='ic'><path d='M9 3h6M10 3v6L4.5 19a1.5 1.5 0 0 0 1.3 2h12.4a1.5 1.5 0 0 0 1.3-2L14 9V3'/><path d='M7 15h10'/></svg>");
        private static readonly MarkupString IconCoin = new("<svg viewBox='0 0 24 24' class='ic'><circle cx='12' cy='12' r='8.5'/><path d='M12 7v10M9.5 9.5h4a1.75 1.75 0 0 1 0 3.5h-3a1.75 1.75 0 0 0 0 3.5h4'/></svg>");
        private static readonly MarkupString IconBolt = new("<svg viewBox='0 0 24 24' class='ic'><path d='M13 2 4 14h7l-1 8 9-12h-7z'/></svg>");
        private static readonly MarkupString IconStar = new("<svg viewBox='0 0 24 24' class='ic'><path d='m12 3 2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1L3.2 9.5l6.1-.9z'/></svg>");

        private string WeekLabel()
        {
            var start = RosterClock.WeekStart(Now);
            return $"week of {start.ToLocalTime():MMM d} · weekly reset in {Until(start.AddDays(7))}";
        }

        private static string OtherGathered(CharacterWeek w)
        {
            int n = w.Gathered.Where(kv => !Odyles.Contains(kv.Key)).Sum(kv => kv.Value);
            return n > 0 ? $" · +{n} other" : "";
        }

        private static string Short(long? v) => v switch
        {
            null => "—",
            >= 1_000_000 => $"{v / 1_000_000.0:0.00}M",
            >= 1_000 => $"{v / 1_000.0:0.#}K",
            _ => v.Value.ToString(),
        };

        private static string KinaDelta(CharacterWeek w) =>
            w.Kina is { } k && w.KinaAtWeekStart is { } s && k != s ? (k > s ? "+" : "−") + Short(Math.Abs(k - s)) + " this week" : "";

        private static string Ago(DateTime utc)
        {
            if (utc == default) return "never";
            var d = Now - utc;
            return d.TotalMinutes < 1 ? "just now"
                : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago"
                : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago"
                : $"{(int)d.TotalDays} days ago";
        }

        // ---- events
        private List<EventStatus> All() =>
            Timers.GetStatus(Settings.TimerRegion, Now).Concat(FieldBosses.GetStatus(Now)).Concat(Kills.GetStatus(Now))
                .GroupBy(e => e.Name).Select(g => g.First()).ToList();   // a boss can be in two lists (field list + kill timer)

        private IEnumerable<EventStatus> Live() => All().Where(e => e.IsLive);

        private IEnumerable<EventStatus> Upcoming(TimeSpan within) =>
            All().Where(e => e.Next is { } n && n > Now && n - Now <= within).OrderBy(e => e.Next);

        private static string LiveText(EventStatus e) =>
            e.PortalOpen(Now) ? $"portal closes in {Until(e.LiveStart!.Value + e.Portal!.Value)}" : $"ends in {Until(e.LiveStart!.Value + e.Duration)}";

        private static double Pos(EventStatus e) => Math.Clamp((e.Next!.Value - Now).TotalHours / 3 * 100, 0, 100);
        private static string Local(DateTime? utc) => utc?.ToLocalTime().ToString("ddd HH:mm") ?? "";

        private static string Until(DateTime? utc)
        {
            if (utc is not { } u) return "";
            var d = u - Now;
            if (d < TimeSpan.Zero) d = TimeSpan.Zero;
            return d.TotalHours >= 24 ? $"{(int)d.TotalDays}d {d.Hours}h"
                : d.TotalHours >= 1 ? $"{(int)d.TotalHours}h {d.Minutes:00}m"
                : $"{d.Minutes}:{d.Seconds:00}";
        }

        // ---- edit tasks
        private void Rename(TaskDef t, string? name) => Replace(t, t with { Name = string.IsNullOrWhiteSpace(name) ? t.Name : name.Trim() });
        private void SetMax(TaskDef t, string? v) { if (int.TryParse(v, out var m)) Replace(t, t with { Max = Math.Clamp(m, 1, 99) }); }
        private void SetHidden(TaskDef t, bool hidden) => Replace(t, t with { Hidden = hidden });

        private void Replace(TaskDef old, TaskDef updated) =>
            Svc.SaveTaskDefs(Data.TaskDefs.Select(d => d.Id == old.Id ? updated : d).ToList());

        private void AddTask()
        {
            if (string.IsNullOrWhiteSpace(NewName)) return;
            var defs = Data.TaskDefs.ToList();
            defs.Add(new TaskDef("custom-" + Guid.NewGuid().ToString("N")[..8], NewName.Trim(), Math.Clamp(NewMax, 1, 99), Weekly: !NewDaily, Server: NewServer));
            Svc.SaveTaskDefs(defs);
            NewName = "";
            NewMax = 1;
        }

        // ---- window
        private void BeginDrag(MouseEventArgs _) => WindowManager.Drag(WindowKey.ControlPanel);
        private void Minimize() => WindowManager.Minimize(WindowKey.ControlPanel);
        private void Close() => Windows.CloseControlPanel();
    }
}
