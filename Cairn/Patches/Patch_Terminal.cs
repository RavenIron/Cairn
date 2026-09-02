using System;
using System.Globalization;
using HarmonyLib;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// The `cairn` console — the locked-decision prefix. Registered from an InitTerminal
    /// postfix; the ConsoleCommand constructor assigns into `Terminal.commands` by lowered
    /// name (decompile-verified 2026-09-01), so it overwrites its own entry and the repeat
    /// call per terminal is harmless.
    ///
    /// Reads answer everywhere. There is nothing to mutate yet; when there is, it follows
    /// Ragnarok's Wrath's self-gating rule — mutations run only where the store lives, and
    /// a pure client is refused with directions rather than trusted.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class Patch_Terminal_Cairn
    {
        private static bool _confirmed;

        private static void Postfix()
        {
            try
            {
                new Terminal.ConsoleCommand("cairn", "Cairn: cairn status", Run);

                // Read it back rather than assuming. "Registered a command" and "the command
                // exists" are different claims, and only one of them can be checked — this is
                // the cheapest place to turn the first into the second.
                if (!_confirmed)
                {
                    _confirmed = true;
                    bool present = Terminal.commands != null && Terminal.commands.ContainsKey("cairn");
                    if (present)
                        Cairn.Log.LogInfo("cairn console registered — confirmed present in Terminal.commands.");
                    else
                        Cairn.Log.LogError(
                            "cairn console did NOT appear in Terminal.commands after registration. " +
                            "The console is the only instrument this build has; treat this as fatal.");
                }
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"cairn console: register failed: {ex.Message}");
            }
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "help";
                switch (sub)
                {
                    case "status": Status(args); return;
                    default: Help(args); return;
                }
            }
            catch (Exception ex)
            {
                args.Context?.AddString("cairn: " + ex.Message);
                Cairn.Log.LogWarning($"cairn console threw: {ex}");
            }
        }

        private static void Help(Terminal.ConsoleEventArgs args)
        {
            args.Context?.AddString("cairn status  — what this process is, and what is running");
        }

        /// <summary>
        /// The whole instrument for this build. It answers the three questions the skeleton
        /// exists to settle: did the plugin load, what process does it think it is, and is
        /// the tick actually running.
        /// </summary>
        private static void Status(Terminal.ConsoleEventArgs args)
        {
            var c = CultureInfo.InvariantCulture;

            args.Context?.AddString($"Cairn v{Cairn.PluginVersion}");
            args.Context?.AddString(
                $"  role       : {CairnTick.Role()} (authority={Cairn.IsSimulationAuthority()}, " +
                $"dedicated={Cairn.IsDedicated()})");
            args.Context?.AddString($"  renderer   : {Cairn.HasRenderer}");
            args.Context?.AddString(
                $"  tick       : {(CairnTick.WorldSeen ? "running" : "not yet — no world")}, " +
                $"{CairnTick.SystemCount.ToString(c)} system(s), " +
                $"budget {ModConfig.TickBudgetMs.Value.ToString("0.##", c)}ms/frame");
            args.Context?.AddString(
                $"  enabled    : {ModConfig.Enabled.Value} (verbose={ModConfig.VerboseLogging.Value})");
            args.Context?.AddString("  landmarks  : none — the ledger is task 2");
        }
    }
}
