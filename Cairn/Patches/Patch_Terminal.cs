using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// The `cairn` console — the locked-decision prefix. Registered from an InitTerminal
    /// postfix; the ConsoleCommand constructor assigns into Terminal's command map by
    /// lowered name (decompile-verified 2026-09-01), so it overwrites its own entry and the
    /// repeat call per terminal is harmless.
    ///
    /// Reads answer everywhere. There is nothing to mutate yet; when there is, it follows
    /// Ragnarok's Wrath's self-gating rule — mutations run only where the store lives, and
    /// a pure client is refused with directions rather than trusted.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class Patch_Terminal_Cairn
    {
        private static bool _confirmed;

        /// <summary>
        /// Resolved lazily and never latched on failure, per house style rule 5.
        /// </summary>
        private static FieldInfo _commandsField;

        private static void Postfix()
        {
            try
            {
                new Terminal.ConsoleCommand("cairn", "Cairn: cairn status", Run);

                if (_confirmed) return;
                _confirmed = true;

                // Read it back rather than assuming. "Registered a command" and "the command
                // exists" are different claims, and only one of them can be checked.
                IDictionary map = ReadCommandMap();
                if (map == null)
                {
                    Cairn.Log.LogWarning(
                        "cairn console: registered, but Terminal's command map could not be read " +
                        "back, so registration is UNCONFIRMED. The command may still work. If " +
                        "Valheim renamed the field, this instrument needs updating.");
                }
                else if (map.Contains("cairn"))
                {
                    Cairn.Log.LogInfo(
                        $"cairn console registered — confirmed present in Terminal's command map " +
                        $"({map.Count} command(s) total).");
                }
                else
                {
                    Cairn.Log.LogError(
                        "cairn console did NOT appear in Terminal's command map after registration. " +
                        "The console is the only instrument this build has; treat this as fatal.");
                }
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"cairn console: register failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Terminal's command dictionary, by reflection. Returns null when it cannot be read.
        ///
        /// HOUSE STYLE RULE 5, and the most expensive way to learn it: `Terminal.commands` is
        /// `public static` in the PUBLICIZED reference assembly and private at runtime. Naming
        /// it directly in source produced
        ///
        ///     FieldAccessException: Field `Terminal:commands' is inaccessible
        ///
        /// on a dedicated server on 2026-09-02 — and the try/catch wrapped around it did not
        /// help, because Mono raises that when the METHOD IS COMPILED, not when the line runs.
        /// The whole Postfix aborted, taking Terminal.Awake and Chat.Awake with it, and the
        /// server shut down before it finished booting. A clean build had reported 0 warnings.
        ///
        /// Two lessons, both load-bearing: reach private members only through reflection, and
        /// never treat try/catch as protection against an inaccessible member — the exception
        /// arrives too early for it. Returned as a non-generic IDictionary on purpose: that
        /// names neither the field's type nor its generic arguments anywhere in our IL.
        /// </summary>
        private static IDictionary ReadCommandMap()
        {
            try
            {
                if (_commandsField == null)
                    _commandsField = AccessTools.Field(typeof(Terminal), "commands");

                if (_commandsField == null)
                {
                    Cairn.Log.LogWarning(
                        "cairn console: Terminal has no field named 'commands' — Valheim's API moved.");
                    return null;
                }

                return _commandsField.GetValue(null) as IDictionary;
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"cairn console: reading Terminal's command map threw: {ex.Message}");
                return null;
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
