using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Reads answer everywhere; mutations follow Ragnarok's Wrath's self-gating rule — they
    /// run only where the store lives, and a pure client is refused with directions rather
    /// than trusted.
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
                new Terminal.ConsoleCommand("cairn", "Cairn: cairn status | landmarks | save", Run);

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
                    case "status":    Status(args); return;
                    case "landmarks": Landmarks(args); return;
                    case "save":      SaveNow(args); return;
                    default:          Help(args); return;
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
            args.Context?.AddString("cairn status     — what this process is, and what is running");
            args.Context?.AddString("cairn landmarks  — every landmark in the ledger");
            args.Context?.AddString("cairn save       — flush the ledger now (authority only)");
        }

        /// <summary>
        /// Reads answer everywhere. On a pure client the ledger is simply empty, and saying so
        /// is more useful than refusing: "0 landmarks" plus "authority=False" is a complete
        /// explanation, where a refusal would only be half of one.
        /// </summary>
        private static void Landmarks(Terminal.ConsoleEventArgs args)
        {
            var c = CultureInfo.InvariantCulture;
            List<Landmark> all = LandmarkStore.Snapshot();

            if (all.Count == 0)
            {
                args.Context?.AddString(
                    $"cairn: no landmarks (store loaded={Persistence.IsLoaded}, " +
                    $"authority={Cairn.IsSimulationAuthority()})");
                return;
            }

            args.Context?.AddString($"cairn: {all.Count.ToString(c)} landmark(s)");
            foreach (Landmark l in all)
            {
                args.Context?.AddString(
                    $"  {l.Key}  \"{l.Name}\"  by {l.Author}  first seen " +
                    new DateTime(l.FirstSeenUtcTicks, DateTimeKind.Utc).ToString("u", c));
            }
        }

        /// <summary>
        /// Mutations run only where the store lives. A pure client is refused with directions
        /// rather than trusted — Ragnarok's Wrath's self-gating rule, inherited.
        /// </summary>
        private static void SaveNow(Terminal.ConsoleEventArgs args)
        {
            if (!Persistence.IsLoaded)
            {
                args.Context?.AddString(
                    "cairn: no ledger on this process — the store lives on the server. " +
                    "Type this at the server's own console, or on a listen host.");
                return;
            }

            Persistence.Save(force: true);
            args.Context?.AddString($"cairn: saved {LandmarkStore.Count.ToString(CultureInfo.InvariantCulture)} landmark(s).");
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
            args.Context?.AddString(
                $"  ledger     : {(Persistence.IsLoaded ? "loaded" : "not on this process")}, " +
                $"{LandmarkStore.Count.ToString(c)} landmark(s)" +
                (LandmarkStore.IsDirty ? ", unsaved changes" : ""));
        }
    }
}
