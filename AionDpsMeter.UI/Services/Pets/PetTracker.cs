using System.IO;
using AionDpsMeter.Services.Services.Pets;
using AionDpsMeter.Services.Services.Roster;
using AionDpsMeter.Services.Services.Settings;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.UI.Services.Pets
{
    /// <summary>
    /// Counts pet souls from the game's inventory updates into pets.json (account-wide, saved 2 s after a
    /// change), answers the meter's pet line and raises LeveledUp for the pop-up. Display only.
    /// </summary>
    public sealed class PetTracker
    {
        private static readonly string SavePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AionDpsMeter", "pets.json");
        private readonly Lock gate = new();
        private readonly PetBook book;
        private readonly PetCatalogDownloader catalogs;
        private readonly IAppSettingsService settings;
        private readonly ILogger<PetTracker> logger;
        private readonly System.Threading.Timer saveTimer;
        private int loggedBigStacks;
        private readonly bool readFailed;

        public event Action? Changed;
        public event Action<PetLevelUp>? LeveledUp;

        public PetCatalog Catalog => catalogs.Current;
        public PetCatalogDownloader Downloads => catalogs;

        public PetTracker(RosterEvents events, PetCatalogDownloader catalogs, IAppSettingsService settings, ILogger<PetTracker> logger)
        {
            this.catalogs = catalogs;
            this.settings = settings;
            this.logger = logger;
            saveTimer = new System.Threading.Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
            catalogs.Updated += () => { lock (gate) { if (book is { } b) b.Catalog = catalogs.Current; } Changed?.Invoke(); };
            catalogs.Start();   // loads the cached catalog synchronously; a refresh (if due) arrives via Updated
            book = new PetBook(PetStore.Load(SavePath, out readFailed), catalogs.Current);
            if (readFailed) logger.LogWarning("[PET] pets.json couldn't be read; not saving this session so it isn't overwritten");
            book.Changed += () => { saveTimer.Change(2000, Timeout.Infinite); Changed?.Invoke(); };
            events.Inventory += OnInventory;
            LeveledUp += up => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                AionDpsMeter.UI.Views.PetToast.Show(up, settings.OverlayScale));
            System.Windows.Application.Current.Exit += (_, _) => Save();
        }

        private void OnInventory(byte[] msg)
        {
            List<PetLevelUp> ups;
            lock (gate)
            {
                ups = book.OnInventory(msg);
                if (book.IgnoredBigStacks > loggedBigStacks)
                {
                    loggedBigStacks = book.IgnoredBigStacks;
                    logger.LogInformation("[PET] ignored a soul stack of more than 5 (inventory listing, not a drop)");
                }
            }
            foreach (var up in ups)
            {
                logger.LogInformation("[PET] {Pet} reached level {Level}", up.Pet, up.Level);
                if (settings.PetTracker) LeveledUp?.Invoke(up);
            }
        }

        public string LineFor(string monster)
        {
            if (!settings.PetTracker || string.IsNullOrEmpty(monster)) return "";
            lock (gate) return book.LineFor(monster);
        }

        public PetData Snapshot() { lock (gate) return PetStore.Clone(book.Data); }

        public void Set(string pet, int level, int souls) { lock (gate) book.Set(pet, level, souls); }

        public void MarkAllMaxed() { lock (gate) book.MarkAllMaxed(); }

        private void Save()
        {
            if (readFailed) return;
            try { PetData copy; lock (gate) copy = PetStore.Clone(book.Data); PetStore.Save(SavePath, copy); }
            catch (Exception ex) { logger.LogWarning(ex, "[PET] save failed"); }
        }
    }
}
