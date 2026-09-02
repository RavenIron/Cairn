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

        // ---- Cairn piles ----------------------------------------------------------
        public static ConfigEntry<string> StonePrefabs;
        public static ConfigEntry<float>  PileLinkMeters;
        public static ConfigEntry<int>    PileMinPieces;
        public static ConfigEntry<int>    PileMaxPieces;
        public static ConfigEntry<float>  PileMaxExtentMeters;
        public static ConfigEntry<float>  LandmarkPairMeters;

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
                "Both defaults confirmed in-game 2026-09-02 by `cairn prefabs sign`: exactly " +
                "two of 3458 prefabs contain \"sign\", and these are they.");

            StonePrefabs = config.Bind(
                "3 - Cairn piles", "StonePrefabs", "stone_pile,stone_wall_1x1,stone_wall_2x1,stone_pillar",
                "Comma-separated stone pieces a cairn can be built from. Names confirmed in-game; " +
                "which of them a player can actually BUILD is a separate question, and the " +
                "per-prefab found= counts in the sweep log answer it empirically. Deliberately " +
                "the SMALL pieces: a cairn is stacked, and floors and stairs would mostly feed " +
                "the footprint rule things it is going to reject anyway.");

            PileLinkMeters = config.Bind(
                "3 - Cairn piles", "PileLinkMeters", 2.5f,
                new ConfigDescription(
                    "Stones within this distance of each other belong to the same pile. Linking " +
                    "is transitive, so a chain of stones forms ONE cluster — which is what lets " +
                    "a long wall be measured, and rejected, as a whole.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            PileMinPieces = config.Bind(
                "3 - Cairn piles", "PileMinPieces", 3,
                new ConfigDescription(
                    "Fewer stones than this is a dropped rock, not a cairn.",
                    new AcceptableValueRange<int>(2, 20)));

            PileMaxPieces = config.Bind(
                "3 - Cairn piles", "PileMaxPieces", 12,
                new ConfigDescription(
                    "More than this is a structure. A backstop only — the footprint rule does the " +
                    "real work, because piece count alone can never tell a tidy wall from a waymark.",
                    new AcceptableValueRange<int>(3, 100)));

            PileMaxExtentMeters = config.Bind(
                "3 - Cairn piles", "PileMaxExtentMeters", 4f,
                new ConfigDescription(
                    "THE rule. A cairn is narrow; a building is wide. Widest horizontal span a " +
                    "cluster may cover before it is judged architecture rather than a waymark.",
                    new AcceptableValueRange<float>(1f, 20f)));

            LandmarkPairMeters = config.Bind(
                "3 - Cairn piles", "LandmarkPairMeters", 6f,
                new ConfigDescription(
                    "How far from a pile a named sign may stand and still name it. Measured " +
                    "XZ-planar: a sign set into a cairn's flank sits metres below its crown, and " +
                    "a 3D check would put it out of reach for no reason a player could see.",
                    new AcceptableValueRange<float>(1f, 30f)));
        }
    }
}
