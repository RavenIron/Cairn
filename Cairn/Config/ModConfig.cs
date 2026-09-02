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
        }
    }
}
