using System;
using System.Collections.Generic;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Systems
{
    /// <summary>
    /// The sweep: named signs become landmarks.
    ///
    /// Reads ZDOs rather than instantiated objects, so it sees every sign the server knows
    /// about and not merely the ones near a player. That is the whole reason the ledger can
    /// be complete on an empty server.
    ///
    /// THE WALK. `ZDOMan.GetAllZDOsWithPrefabIterative` is vanilla's own self-chunking
    /// traversal (decompile-verified 2026-09-02: appends matches by prefab hash, advances an
    /// index through the sector array, and returns true once it has passed the end and
    /// drained the outside-sector list). One WHOLE prefab is drained per tick — RW learned
    /// that resuming one chunk per tick stretches a rotation across the better part of an
    /// hour, and vanilla's own callers drain it in a loop within one frame. Termination is
    /// structural: the index advances on every call until it passes the sector array.
    ///
    /// SIGN PREFAB NAMES ARE CONFIG, NOT CODE. They are data about the game's content, they
    /// drift with game patches and modded pieces, and a wrong name costs a SILENT zero
    /// matches — which is why the first completed rotation of a session always logs its
    /// per-prefab counts, whether or not anything was found.
    ///
    /// The interval defaults to 45s to stagger against AwayFromHome's 60s full-index rescan.
    /// </summary>
    public class LandmarkSystem : IWorldSystem
    {
        public string Name => "LandmarkSystem";
        public bool Enabled => ModConfig.EnableLandmarks.Value;
        public float IntervalSeconds => ModConfig.LandmarkIntervalSeconds.Value;

        private string[] _signPrefabs = Array.Empty<string>();
        private int _prefabCursor;

        // GetAllZDOsWithPrefabIterative's resume state for the prefab currently mid-walk.
        private readonly List<ZDO> _found = new List<ZDO>(64);
        private int _sweepIndex;

        /// <summary>
        /// What this rotation has seen so far, accumulated across every configured prefab and
        /// applied only when the rotation completes. Applying per-prefab would prune every
        /// landmark belonging to a prefab whose turn had not yet come up.
        /// </summary>
        private readonly Dictionary<LandmarkKey, Reading> _seen = new Dictionary<LandmarkKey, Reading>(64);

        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(4);

        /// <summary>
        /// False once any prefab in this rotation failed. Pruning is skipped for an unclean
        /// rotation: a sweep that threw halfway has not proved a landmark is gone, and
        /// deleting on incomplete evidence is how a ledger quietly empties itself.
        /// </summary>
        private bool _rotationClean = true;

        private bool _firstRotationLogged;

        private struct Reading
        {
            public string Name;
            public string Author;
        }

        public void Initialise()
        {
            _signPrefabs = (ModConfig.SignPrefabs.Value ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < _signPrefabs.Length; i++) _signPrefabs[i] = _signPrefabs[i].Trim();

            Cairn.Log.LogInfo(
                $"[{Name}] sweeping {_signPrefabs.Length} sign prefab(s) every {IntervalSeconds:F0}s: " +
                $"{string.Join(", ", _signPrefabs)}");
        }

        public void Tick(float deltaSeconds)
        {
            if (_signPrefabs.Length == 0) return;

            ZDOMan man = ZDOMan.instance;
            if (man == null) return;

            string prefab = _signPrefabs[_prefabCursor];

            try
            {
                bool done = false;
                while (!done)
                    done = man.GetAllZDOsWithPrefabIterative(prefab, _found, ref _sweepIndex);
            }
            catch (Exception ex)
            {
                // Do not advance the cursor: the same prefab is retried next tick. An
                // unrecognised prefab name does not throw — it returns no matches — so a throw
                // here means something else, and losing the rotation is the safe response.
                Cairn.Log.LogWarning($"[{Name}] sweep failed on '{prefab}': {ex.Message}");
                _found.Clear();
                _sweepIndex = 0;
                _rotationClean = false;
                return;
            }

            int accepted = 0;
            for (int i = 0; i < _found.Count; i++)
            {
                ZDO zdo = _found[i];
                if (zdo == null || !zdo.IsValid()) continue;

                string rawText = zdo.GetString(ZDOVars.s_text, "");
                string rawAuthor = zdo.GetString(ZDOVars.s_author, "");

                if (!SignReading.TryRead(rawText, rawAuthor, out string name, out string author))
                    continue;   // a blank sign is not a place

                Vector3 pos = zdo.GetPosition();
                _seen[LandmarkKey.FromPosition(pos)] = new Reading { Name = name, Author = author };
                accepted++;
            }

            _counts[prefab] = accepted;

            _found.Clear();
            _sweepIndex = 0;

            _prefabCursor++;
            if (_prefabCursor < _signPrefabs.Length) return;

            _prefabCursor = 0;
            CompleteRotation();
        }

        /// <summary>
        /// Every configured prefab has been walked, so <see cref="_seen"/> is now the complete
        /// set of named signs in the world — which is what makes pruning safe.
        /// </summary>
        private void CompleteRotation()
        {
            long now = DateTime.UtcNow.Ticks;
            int changed = 0;

            foreach (KeyValuePair<LandmarkKey, Reading> kv in _seen)
            {
                if (LandmarkStore.Upsert(kv.Key, kv.Value.Name, kv.Value.Author, now)) changed++;
            }

            int pruned = 0;
            if (_rotationClean)
            {
                foreach (Landmark landmark in LandmarkStore.Snapshot())
                {
                    if (_seen.ContainsKey(landmark.Key)) continue;

                    // Snapshot() is a copy, so removing while walking it is safe.
                    if (LandmarkStore.Remove(landmark.Key)) pruned++;
                }
            }

            bool interesting = changed > 0 || pruned > 0 || !_firstRotationLogged;
            if (interesting || ModConfig.VerboseLogging.Value)
            {
                var parts = new List<string>(_counts.Count);
                foreach (KeyValuePair<string, int> kv in _counts) parts.Add($"{kv.Key}:{kv.Value}");

                string tail = _seen.Count == 0
                    ? " — no named signs found: either this world has none, or SignPrefabs names the wrong prefab"
                    : "";

                Cairn.Log.LogInfo(
                    $"[{Name}] sweep complete ({string.Join(", ", parts)}) — " +
                    $"{LandmarkStore.Count} landmark(s), {changed} changed, {pruned} pruned" +
                    (_rotationClean ? "" : ", PRUNING SKIPPED (a prefab failed this rotation)") +
                    tail);
            }

            _firstRotationLogged = true;
            _seen.Clear();
            _counts.Clear();
            _rotationClean = true;
        }
    }
}
