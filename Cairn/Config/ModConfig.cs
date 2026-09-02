using BepInEx.Configuration;

namespace RavenIron.Cairn.Config
{
    /// <summary>
    /// Config surface. Expected to grow — beacon height and colour, the raven's voice,
    /// sound range, ledger cadence all belong here eventually.
    ///
    /// Two conventions carried over from Ragnarok's Wrath, both paid for:
    ///
    /// 1. Every system gets its own on/off toggle from day one. That is what makes
    ///    incremental testing possible and lets a server owner adopt part of the mod.
    ///    There are no systems yet, so there are no system toggles yet — a toggle for a
    ///    thing that does not exist is a promise the log cannot keep.
    ///
    /// 2. Clamp on READ as well as on write. Config files get hand-edited, and BepInEx
    ///    persists whatever it clamped, so a value validated only on write comes back
    ///    wrong on the next boot.
    /// </summary>
    public static class ModConfig
    {
        // ---- Core -----------------------------------------------------------------
        public static ConfigEntry<bool>  Enabled;
        public static ConfigEntry<float> TickBudgetMs;
        public static ConfigEntry<bool>  VerboseLogging;
        public static ConfigEntry<float> AutosaveIntervalSeconds;

        // ---- Landmarks ------------------------------------------------------------
        public static ConfigEntry<bool>   EnableLandmarks;
        public static ConfigEntry<float>  LandmarkIntervalSeconds;
        public static ConfigEntry<string> SignPrefabs;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind(
                "1 - Core", "Enabled", true,
                "Master switch. False leaves the plugin loaded and the console registered, " +
                "but nothing ticks.");

            TickBudgetMs = config.Bind(
                "1 - Core", "TickBudgetMs", 2.0f,
                new ConfigDescription(
                    "Milliseconds per frame CairnTick may spend across ALL systems. Work that " +
                    "does not fit resumes next frame from where the cursor stopped.",
                    new AcceptableValueRange<float>(0.25f, 10f)));

            VerboseLogging = config.Bind(
                "1 - Core", "VerboseLogging", false,
                "Per-tick detail. Off by default: it is loud enough to hide the lines that matter.");

            AutosaveIntervalSeconds = config.Bind(
                "1 - Core", "AutosaveIntervalSeconds", 60f,
                new ConfigDescription(
                    "How often the landmark ledger is written when it has changed. Time-based " +
                    "rather than change-based on purpose: a busy world would otherwise write on " +
                    "every sweep, and an idle one would never write at all.",
                    new AcceptableValueRange<float>(5f, 600f)));

            EnableLandmarks = config.Bind(
                "2 - Landmarks", "EnableLandmarks", true,
                "The sweep that turns named signs into landmarks. Off leaves the ledger frozen " +
                "at whatever it already holds.");

            LandmarkIntervalSeconds = config.Bind(
                "2 - Landmarks", "LandmarkIntervalSeconds", 45f,
                new ConfigDescription(
                    "Seconds between sweeps of ONE sign prefab. 45 by default to stagger against " +
                    "AwayFromHome's 60s full-index rescan.",
                    new AcceptableValueRange<float>(5f, 600f)));

            SignPrefabs = config.Bind(
                "2 - Landmarks", "SignPrefabs", "sign,sign_notext",
                "Comma-separated prefab names treated as signs. CONFIG rather than code because " +
                "these are data about the game's content: they drift with game patches and modded " +
                "pieces, and a wrong name costs a silent zero matches. The first sweep of each " +
                "session logs its per-prefab counts so a wrong name is visible in the log. " +
                "Both defaults were confirmed against real world saves by prefab-hash search.");
        }
    }
}
