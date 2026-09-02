using System;
using System.Collections.Generic;
using System.Diagnostics;
using RavenIron.Cairn.Config;
using UnityEngine;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// The one place anything in this mod is driven from.
    ///
    /// HOUSE STYLE RULE 2: a time-budgeted cursor driven from a single Update, NOT
    /// coroutines. Every long-lived coroutine in this studio's lineage independently grew
    /// the same bug — a `while (true)` whose body can `continue` past its only `yield`,
    /// hard-locking the game. It reached production once. A rule you must remember at every
    /// future edit is a rule that will eventually be forgotten, so the shape that cannot
    /// express the bug is the one used.
    ///
    /// Systems register once and are round-robined. Each Update spends at most
    /// <see cref="ModConfig.TickBudgetMs"/> milliseconds across all of them; whatever is
    /// left resumes next frame from where the cursor stopped. The cursor never resets to
    /// zero, so no system can starve another.
    ///
    /// The role line prints unconditionally, deliberately: a proof-of-life line that only
    /// appears once there is work to do cannot distinguish "loaded and idle" from "never
    /// loaded", which is the exact ambiguity this studio spends its debugging round-trips on.
    /// </summary>
    public class CairnTick : MonoBehaviour
    {
        private static readonly List<IWorldSystem> _systems = new List<IWorldSystem>();
        private static readonly Dictionary<IWorldSystem, float> _lastRun =
            new Dictionary<IWorldSystem, float>();

        private static int _cursor;
        private static bool _initialised;
        private static bool _roleLogged;

        private readonly Stopwatch _sw = new Stopwatch();

        /// <summary>
        /// Register a system. Safe to call before ZNet exists; Initialise is deferred until
        /// the first tick where we know what this process is.
        /// </summary>
        public static void Register(IWorldSystem system)
        {
            if (system == null) return;
            if (_systems.Contains(system)) return;

            _systems.Add(system);
            _lastRun[system] = 0f;
        }

        public static int SystemCount => _systems.Count;

        /// <summary>Has the role line been emitted — i.e. has a world actually loaded?</summary>
        public static bool WorldSeen => _roleLogged;

        /// <summary>Human-readable role, for the console and the boot line.</summary>
        public static string Role()
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return "no world";
            if (Cairn.IsDedicated()) return "dedicated server";
            if (znet.IsServer()) return "listen host";
            return "client";
        }

        private void Update()
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return;   // main menu: nothing is knowable yet

            if (!_roleLogged)
            {
                _roleLogged = true;
                Cairn.Log.LogInfo(
                    $"Cairn online — role={Role()}, authority={Cairn.IsSimulationAuthority()}, " +
                    $"dedicated={Cairn.IsDedicated()}, renderer={Cairn.HasRenderer}, " +
                    $"systems={_systems.Count}, budget={ModConfig.TickBudgetMs.Value}ms/frame");
            }

            if (!ModConfig.Enabled.Value) return;

            // Both sides: a client must be listening before the first broadcast arrives, and
            // registration is cheap and idempotent once ZRoutedRpc exists.
            Net.LandmarkSync.EnsureRegistered();

            // The ledger lives on the authority. A pure client renders what it is told and
            // simulates nothing.
            if (!Cairn.IsSimulationAuthority()) return;

            if (!_initialised)
            {
                InitialiseSystems();
                _initialised = true;
            }

            float now = Time.realtimeSinceStartup;

            // Before the system-count check on purpose: the ledger can be changed by the
            // console with no system registered at all, and an unsaved change is invisible
            // until the next restart loses it.
            MaybeAutosave(now);
            MaybeBroadcastBeacons(now);

            if (_systems.Count == 0) return;

            float budgetMs = Mathf.Clamp(ModConfig.TickBudgetMs.Value, 0.25f, 10f);

            _sw.Restart();

            // At most one lap per frame. The cursor persists across frames, so a lap cut
            // short by the budget resumes rather than restarting — that is what stops the
            // systems early in the list from starving the ones after them.
            int examined = 0;
            while (examined < _systems.Count && _sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                IWorldSystem system = _systems[_cursor];
                _cursor = (_cursor + 1) % _systems.Count;
                examined++;

                if (!system.Enabled) continue;

                float last = _lastRun[system];
                float due = now - last;
                if (due < system.IntervalSeconds) continue;

                _lastRun[system] = now;

                // One misbehaving system must not take the others down with it.
                try
                {
                    system.Tick(due);
                }
                catch (Exception ex)
                {
                    Cairn.Log.LogError($"[{system.Name}] tick threw: {ex}");
                }
            }

            _sw.Stop();
        }

        private static float _lastSave;

        /// <summary>
        /// Periodic write-behind. Save is a no-op when nothing is dirty, so this costs a
        /// comparison on most passes.
        /// </summary>
        private static void MaybeAutosave(float now)
        {
            float interval = ModConfig.AutosaveIntervalSeconds.Value;
            if (interval <= 0f) return;
            if (now - _lastSave < interval) return;

            _lastSave = now;
            Persistence.Save();
        }

        private static float _lastBeaconSend;

        /// <summary>
        /// Absolute, unconditional, on a cadence. A client that joined a second ago is right
        /// within one interval, and a dropped packet heals itself with the next — which a
        /// change-triggered push could never promise.
        /// </summary>
        private static void MaybeBroadcastBeacons(float now)
        {
            if (!ModConfig.EnableBeacons.Value) return;

            float interval = ModConfig.BeaconSyncSeconds.Value;
            if (now - _lastBeaconSend < interval) return;

            _lastBeaconSend = now;
            Net.LandmarkSync.Broadcast(ModConfig.BeaconMaxCount.Value);
        }

        private static void InitialiseSystems()
        {
            // Load stored landmarks before any system reads them. Never throws; a missing or
            // corrupt store leaves an empty, usable ledger.
            Persistence.Load();

            foreach (IWorldSystem system in _systems)
            {
                try
                {
                    system.Initialise();
                    Cairn.Log.LogInfo(
                        $"[{system.Name}] initialised (enabled={system.Enabled}, interval={system.IntervalSeconds}s)");
                }
                catch (Exception ex)
                {
                    Cairn.Log.LogError($"[{system.Name}] failed to initialise: {ex}");
                }
            }
        }

        private void OnDestroy()
        {
            // Last chance to flush. Leaving the world, stopping the server, or unloading the
            // plugin all land here — without this, up to one autosave interval of the ledger
            // is lost every session, which reads as the mod randomly forgetting a landmark.
            Persistence.Save(force: true);

            _systems.Clear();
            _lastRun.Clear();
            _cursor = 0;
            _initialised = false;
            _roleLogged = false;
            _lastSave = 0f;
            _lastBeaconSend = 0f;
        }
    }
}
