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
        public static ConfigEntry<float>  LandmarkRotationSeconds;
        public static ConfigEntry<string> SignPrefabs;

        // ---- Cairn piles ----------------------------------------------------------
        public static ConfigEntry<string> StonePrefabs;
        public static ConfigEntry<float>  PileLinkMeters;
        public static ConfigEntry<int>    PileMinPieces;
        public static ConfigEntry<int>    PileMaxPieces;
        public static ConfigEntry<float>  PileMaxExtentMeters;
        public static ConfigEntry<float>  LandmarkPairMeters;
        public static ConfigEntry<float>  PileDriftMeters;
        public static ConfigEntry<int>    StonePileStoneCost;

        // ---- The light ------------------------------------------------------------
        public static ConfigEntry<bool>  EnableBeacons;
        public static ConfigEntry<float> BeaconSyncSeconds;
        public static ConfigEntry<int>   BeaconMaxCount;
        public static ConfigEntry<float> BeaconMaxDistanceMeters;
        public static ConfigEntry<float> BeaconAngularSize;
        public static ConfigEntry<float> BeaconMinSizeMeters;
        public static ConfigEntry<float> BeaconMaxSizeMeters;
        public static ConfigEntry<float> BeaconHeightMeters;
        public static ConfigEntry<bool>   BeaconOcclusion;
        public static ConfigEntry<string> BeaconColour;

        // ---- The raven ------------------------------------------------------------
        public static ConfigEntry<bool>  EnableRavenVoice;
        public static ConfigEntry<float> RavenNameMeters;
        public static ConfigEntry<int>   RavenNameMaxLength;

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

            LandmarkRotationSeconds = config.Bind(
                "2 - Landmarks", "LandmarkRotationSeconds", 20f,
                new ConfigDescription(
                    "Seconds for a FULL rotation over every sign and stone prefab — which is " +
                    "how long a newly built cairn can take to be noticed. Each prefab is swept " +
                    "one at a time at rotation/count, so adding a prefab makes each step " +
                    "shorter rather than the wait longer. It used to govern each PREFAB, which " +
                    "made four prefabs a three-minute wait; latency should not be a tax on the " +
                    "length of a config list. TRADE-OFF: a shorter rotation touches the ZDO " +
                    "index more often, which is the thing AwayFromHome also scans every 60s. " +
                    "Raise it on a heavily modded server if sweeps ever show up in a profile.",
                    new AcceptableValueRange<float>(4f, 600f)));

            SignPrefabs = config.Bind(
                "2 - Landmarks", "SignPrefabs", "sign,sign_notext",
                "Comma-separated prefab names treated as signs. CONFIG rather than code because " +
                "these are data about the game's content: they drift with game patches and modded " +
                "pieces, and a wrong name costs a silent zero matches. The first sweep of each " +
                "session logs its per-prefab counts so a wrong name is visible in the log. " +
                "Both defaults confirmed in-game 2026-09-02 by `cairn prefabs sign`: exactly " +
                "two of 3458 prefabs contain \"sign\", and these are they.");

            StonePrefabs = config.Bind(
                "3 - Cairn piles", "StonePrefabs", "Placeable_Stone,stone_pile",
                "Comma-separated stone pieces a cairn can be built from. Both confirmed BUILDABLE " +
                "in-game 2026-09-02 by `cairn pieces stone`: Placeable_Stone is [Hoe] Stone x1 — " +
                "a single stone you stack, which is what a cairn actually is — and stone_pile is " +
                "[Hammer] Stone x50, one pre-made heap. Architecture is deliberately absent: " +
                "walls and floors would only feed the footprint rule things it is going to " +
                "reject, and leaving them out means a stone HOUSE can never become a landmark.");

            PileLinkMeters = config.Bind(
                "3 - Cairn piles", "PileLinkMeters", 2.5f,
                new ConfigDescription(
                    "Stones within this distance of each other belong to the same pile. Linking " +
                    "is transitive, so a chain of stones forms ONE cluster — which is what lets " +
                    "a long wall be measured, and rejected, as a whole.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            PileMinPieces = config.Bind(
                "3 - Cairn piles", "PileMinPieces", 4,
                new ConfigDescription(
                    "Fewer stones than this is a dropped rock, not a cairn. At 1 stone per " +
                    "Placeable_Stone the price signals nothing, so DELIBERATENESS is carried by " +
                    "this count and the footprint rule instead — which is the job they were " +
                    "written for. Raise it if scattered decorative stones start lighting up.",
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

            PileDriftMeters = config.Bind(
                "3 - Cairn piles", "PileDriftMeters", 4f,
                new ConfigDescription(
                    "How far an UNNAMED cairn's crown may move between sweeps and still be the " +
                    "same cairn. A crown is a computed centroid, so adding a stone shifts it — " +
                    "and on 2026-09-02 a live cairn drifted 0.8m, crossed a metre boundary, and " +
                    "was pruned and re-founded with its history erased. Within this distance the " +
                    "old FirstSeen follows the cairn to its new key instead. Defaults to the " +
                    "footprint limit: a cairn that moved less than its own width has not become " +
                    "a different place. Named cairns never need this — they are keyed on a sign, " +
                    "and signs do not move.",
                    new AcceptableValueRange<float>(0f, 20f)));

            StonePileStoneCost = config.Bind(
                "3 - Cairn piles", "StonePileStoneCost", 0,
                new ConfigDescription(
                    "Stone required to build one vanilla 'stone_pile'. 0 means DO NOT TOUCH THE " +
                    "GAME, and that is now the default. Any other value edits a vanilla recipe — " +
                    "the only thing in this mod that reaches outside its own scope — and " +
                    "announces itself in the log with the before and the after, because a mod " +
                    "that silently rewrites a recipe is exactly the surprise this studio " +
                    "dislikes in others. It briefly shipped at 10, when stone_pile at 50 stone " +
                    "looked like the only way to make cairns affordable. Then `cairn pieces " +
                    "stone` turned up Placeable_Stone at ONE stone, the problem the override " +
                    "existed to solve stopped existing, and the right move was to hand vanilla " +
                    "back. Kept as a switch for anyone who prefers heaps to stacks.",
                    new AcceptableValueRange<int>(0, 100)));

            EnableBeacons = config.Bind(
                "4 - The light", "EnableBeacons", true,
                "Draw a light on top of every cairn. Client-side and purely visual: turning it " +
                "off changes nothing the server knows, and the ledger keeps recording places.");

            BeaconSyncSeconds = config.Bind(
                "4 - The light", "BeaconSyncSeconds", 5f,
                new ConfigDescription(
                    "How often the server broadcasts every lit cairn to everyone. Absolute " +
                    "snapshots, sent whether or not anything changed: a delta scheme drifts " +
                    "forever on one dropped packet, and this heals itself within one cadence.",
                    new AcceptableValueRange<float>(5f, 120f)));

            BeaconMaxCount = config.Bind(
                "4 - The light", "BeaconMaxCount", 100,
                new ConfigDescription(
                    "Most cairns carried in one broadcast. A bound on the packet, not a limit " +
                    "anyone should reach.",
                    new AcceptableValueRange<int>(1, 500)));

            BeaconMaxDistanceMeters = config.Bind(
                "4 - The light", "BeaconMaxDistanceMeters", 800f,
                new ConfigDescription(
                    "How far a beacon can be seen before it fades out entirely.",
                    new AcceptableValueRange<float>(50f, 4000f)));

            BeaconAngularSize = config.Bind(
                "4 - The light", "BeaconAngularSize", 0.03f,
                new ConfigDescription(
                    "THE reason a beacon is visible at range. The glow is scaled BY DISTANCE, " +
                    "so it holds roughly constant size on screen instead of shrinking away — " +
                    "0.03 means a 12m glow at 400m, about 1.7 degrees, which is a clear point " +
                    "of light rather than a smudge. A fixed-size glow would be the wrong shape " +
                    "of thing entirely.",
                    new AcceptableValueRange<float>(0.005f, 0.2f)));

            BeaconMinSizeMeters = config.Bind(
                "4 - The light", "BeaconMinSizeMeters", 1.5f,
                new ConfigDescription(
                    "Floor on the glow's world size, so standing next to a cairn does not put " +
                    "a speck on the stones.",
                    new AcceptableValueRange<float>(0.2f, 20f)));

            BeaconMaxSizeMeters = config.Bind(
                "4 - The light", "BeaconMaxSizeMeters", 14f,
                new ConfigDescription(
                    "Ceiling on the glow's world size, so a distant beacon stays a point of " +
                    "light rather than becoming a wall of it.",
                    new AcceptableValueRange<float>(1f, 100f)));

            BeaconHeightMeters = config.Bind(
                "4 - The light", "BeaconHeightMeters", 1.2f,
                new ConfigDescription(
                    "How far above the cairn's crown the light sits.",
                    new AcceptableValueRange<float>(0f, 10f)));

            BeaconColour = config.Bind(
                "4 - The light", "BeaconColour", "FFB85A",
                "Beacon colour as hex — RGB, RRGGBB or RRGGBBAA, with or without a leading #. " +
                "The default is firelight. A value that will not parse falls back to that and " +
                "says so once in the log, because a beacon silently drawing black looks exactly " +
                "like a beacon that is not drawing at all.");

            BeaconOcclusion = config.Bind(
                "4 - The light", "BeaconOcclusion", true,
                "Terrain hides a beacon. LEAVE THIS ON. A glow that shines through a mountain " +
                "is not a beacon, it is a waypoint marker in a costume, and house rule A " +
                "forbids exactly that — a ridge between you and a cairn should mean you have " +
                "to move, which is the difference between navigating and being told. Off is " +
                "for diagnosing whether a beacon is missing or merely hidden.");

            EnableRavenVoice = config.Bind(
                "5 - The raven", "EnableRavenVoice", true,
                "Let Hugin say the name of a named landmark you are standing in. FLAVOUR, never " +
                "a navigation channel — vanilla refuses to land the bird below 30m of altitude, " +
                "with a hostile within 10m, or during any world event, so it is silent exactly " +
                "when you would most want it. Off changes nothing else.");

            RavenNameMeters = config.Bind(
                "5 - The raven", "RavenNameMeters", 12f,
                new ConfigDescription(
                    "How close you must stand to a NAMED landmark before the line is offered. " +
                    "Kept inside the raven's own 15m spawn distance, or the bird would be " +
                    "carrying a message about somewhere it cannot reach.",
                    new AcceptableValueRange<float>(2f, 15f)));

            RavenNameMaxLength = config.Bind(
                "5 - The raven", "RavenNameMaxLength", 64,
                new ConfigDescription(
                    "Longest spoken name before it is cut with an ellipsis. Rich text is always " +
                    "stripped before speaking — storage stays faithful to what the player typed, " +
                    "the display decides what may be said, and that is the moderation question " +
                    "the scope doc left open being answered conservatively.",
                    new AcceptableValueRange<int>(8, 200)));
        }
    }
}
