using System;
using System.Collections.Generic;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Systems
{
    /// <summary>
    /// The sweep: stacked stone becomes a cairn, and a named sign beside it gives it a name.
    ///
    /// Reads ZDOs rather than instantiated objects, so it sees everything the server knows
    /// about and not merely what is near a player. That is the whole reason the ledger can
    /// be complete on an empty server.
    ///
    /// THE WALK. `ZDOMan.GetAllZDOsWithPrefabIterative` is vanilla's own self-chunking
    /// traversal (decompile-verified 2026-09-02: appends matches by prefab hash, advances an
    /// index through the sector array, returns true once past the end and the outside-sector
    /// list is drained). One WHOLE prefab is drained per tick — RW learned that resuming one
    /// chunk per tick stretches a rotation across the better part of an hour, and vanilla's
    /// own callers drain it in a loop within one frame.
    ///
    /// A ROTATION covers every sign prefab and every stone prefab. Nothing is applied until
    /// it completes, because a pile cannot be judged from the stone of one prefab and a sign
    /// cannot be paired with a pile that has not been found yet.
    ///
    /// PREFAB NAMES ARE CONFIG, NOT CODE. They are data about the game's content, they drift
    /// with patches and modded pieces, and a wrong one costs a SILENT zero matches — which is
    /// why every completed rotation logs its per-prefab counts, always.
    /// </summary>
    public class LandmarkSystem : IWorldSystem
    {
        public string Name => "LandmarkSystem";
        public bool Enabled => ModConfig.EnableLandmarks.Value;
        public float IntervalSeconds => ModConfig.LandmarkIntervalSeconds.Value;

        private struct Target
        {
            public string Prefab;
            public bool IsSign;
        }

        private readonly List<Target> _targets = new List<Target>(8);
        private int _cursor;

        // GetAllZDOsWithPrefabIterative's resume state for the prefab currently mid-walk.
        private readonly List<ZDO> _found = new List<ZDO>(64);
        private int _sweepIndex;

        // Accumulated across the WHOLE rotation, applied only when it completes.
        private readonly List<Vector3> _stones = new List<Vector3>(256);
        private readonly List<Vector3> _signPositions = new List<Vector3>(32);
        private readonly List<string> _signNames = new List<string>(32);
        private readonly List<string> _signAuthors = new List<string>(32);

        private readonly Dictionary<string, int> _foundCounts = new Dictionary<string, int>(8);
        private readonly Dictionary<string, int> _keptCounts = new Dictionary<string, int>(8);

        /// <summary>
        /// False once any prefab in this rotation failed. Pruning is skipped for an unclean
        /// rotation: a sweep that threw halfway has not proved a landmark is gone, and
        /// deleting on incomplete evidence is how a ledger quietly empties itself.
        /// </summary>
        private bool _rotationClean = true;

        public void Initialise()
        {
            _targets.Clear();
            AddTargets(ModConfig.SignPrefabs.Value, isSign: true);
            AddTargets(ModConfig.StonePrefabs.Value, isSign: false);

            var signs = new List<string>();
            var stones = new List<string>();
            foreach (Target t in _targets) (t.IsSign ? signs : stones).Add(t.Prefab);

            Cairn.Log.LogInfo(
                $"[{Name}] sweeping every {IntervalSeconds:F0}s — " +
                $"signs: {(signs.Count > 0 ? string.Join(", ", signs.ToArray()) : "none")}; " +
                $"stone: {(stones.Count > 0 ? string.Join(", ", stones.ToArray()) : "none")}. " +
                $"A pile is {ModConfig.PileMinPieces.Value}-{ModConfig.PileMaxPieces.Value} pieces " +
                $"within {ModConfig.PileMaxExtentMeters.Value:F1}m of footprint; a sign names one " +
                $"from {ModConfig.LandmarkPairMeters.Value:F0}m.");
        }

        private void AddTargets(string csv, bool isSign)
        {
            foreach (string raw in (csv ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string prefab = raw.Trim();
                if (prefab.Length > 0) _targets.Add(new Target { Prefab = prefab, IsSign = isSign });
            }
        }

        public void Tick(float deltaSeconds)
        {
            if (_targets.Count == 0) return;

            ZDOMan man = ZDOMan.instance;
            if (man == null) return;

            Target target = _targets[_cursor];

            try
            {
                bool done = false;
                while (!done)
                    done = man.GetAllZDOsWithPrefabIterative(target.Prefab, _found, ref _sweepIndex);
            }
            catch (Exception ex)
            {
                // Do not advance the cursor: the same prefab is retried next tick. An
                // unrecognised prefab name does not throw — it returns no matches — so a throw
                // here means something else, and losing the rotation is the safe response.
                Cairn.Log.LogWarning($"[{Name}] sweep failed on '{target.Prefab}': {ex.Message}");
                _found.Clear();
                _sweepIndex = 0;
                _rotationClean = false;
                return;
            }

            int valid = 0, kept = 0;
            for (int i = 0; i < _found.Count; i++)
            {
                ZDO zdo = _found[i];
                if (zdo == null || !zdo.IsValid()) continue;
                valid++;

                if (target.IsSign)
                {
                    string rawText = zdo.GetString(ZDOVars.s_text, "");
                    string rawAuthor = zdo.GetString(ZDOVars.s_author, "");
                    if (!SignReading.TryRead(rawText, rawAuthor, out string name, out string author))
                        continue;   // a blank sign is not a place

                    _signPositions.Add(zdo.GetPosition());
                    _signNames.Add(name);
                    _signAuthors.Add(author);
                    kept++;
                }
                else
                {
                    _stones.Add(zdo.GetPosition());
                    kept++;
                }
            }

            _foundCounts[target.Prefab] = valid;
            _keptCounts[target.Prefab] = kept;

            _found.Clear();
            _sweepIndex = 0;

            _cursor++;
            if (_cursor < _targets.Count) return;

            _cursor = 0;
            CompleteRotation();
        }

        /// <summary>
        /// Every prefab has been walked, so what was collected is the COMPLETE picture of the
        /// world's stone and named signs — which is what makes both pairing and pruning safe.
        /// </summary>
        private void CompleteRotation()
        {
            long now = DateTime.UtcNow.Ticks;

            List<PileDetection.Pile> piles = PileDetection.Find(
                _stones,
                ModConfig.PileLinkMeters.Value,
                ModConfig.PileMinPieces.Value,
                ModConfig.PileMaxPieces.Value,
                ModConfig.PileMaxExtentMeters.Value);

            var seen = new HashSet<LandmarkKey>();
            var claimedSign = new bool[_signPositions.Count];
            int changed = 0;

            float pairMeters = ModConfig.LandmarkPairMeters.Value;

            foreach (PileDetection.Pile pile in piles)
            {
                int sign = PileDetection.NearestSign(pile.Top, _signPositions, pairMeters);

                // A named sign is the landmark's identity when one is in reach, so building a
                // cairn around a sign that was ALREADY a landmark flips its light on rather
                // than founding a second place a metre away. The sign does not move; the
                // pile's centroid does.
                LandmarkKey key = sign >= 0
                    ? LandmarkKey.FromPosition(_signPositions[sign])
                    : LandmarkKey.FromPosition(pile.Top);

                string name = sign >= 0 ? _signNames[sign] : "";
                string author = sign >= 0 ? _signAuthors[sign] : SignReading.UnknownAuthor;
                if (sign >= 0) claimedSign[sign] = true;

                if (LandmarkStore.Upsert(key, name, author, true, pile.Top, now)) changed++;
                seen.Add(key);
            }

            // Named signs no pile has claimed: a place with a name and no light.
            for (int i = 0; i < _signPositions.Count; i++)
            {
                if (claimedSign[i]) continue;

                LandmarkKey key = LandmarkKey.FromPosition(_signPositions[i]);
                if (seen.Contains(key)) continue;

                if (LandmarkStore.Upsert(key, _signNames[i], _signAuthors[i], false, default, now)) changed++;
                seen.Add(key);
            }

            int pruned = 0;
            if (_rotationClean)
            {
                foreach (Landmark landmark in LandmarkStore.Snapshot())
                {
                    if (seen.Contains(landmark.Key)) continue;
                    if (LandmarkStore.Remove(landmark.Key)) pruned++;   // Snapshot is a copy
                }
            }

            LogRotation(piles.Count, changed, pruned);

            _stones.Clear();
            _signPositions.Clear();
            _signNames.Clear();
            _signAuthors.Clear();
            _foundCounts.Clear();
            _keptCounts.Clear();
            _rotationClean = true;
        }

        /// <summary>
        /// EVERY completed rotation logs, unconditionally.
        ///
        /// Logging only "interesting" rotations makes SILENCE the answer in the ordinary case,
        /// and silence cannot be told apart from a stopped tick, a disabled system or a
        /// crashed server. On 2026-09-02 a live test sat through several quiet rotations and
        /// the log could not say which of those it was watching. One line per rotation is not
        /// noise; an unfalsifiable quiet is.
        /// </summary>
        private void LogRotation(int pileCount, int changed, int pruned)
        {
            var parts = new List<string>(_foundCounts.Count);
            int signsFound = 0, stonesFound = 0;

            foreach (Target t in _targets)
            {
                _foundCounts.TryGetValue(t.Prefab, out int found);
                _keptCounts.TryGetValue(t.Prefab, out int kept);
                parts.Add($"{t.Prefab} found={found} kept={kept}");

                if (t.IsSign) signsFound += found; else stonesFound += found;
            }

            // The zero cases mean different things and must never share a message.
            string tail = "";
            if (signsFound == 0 && stonesFound == 0)
                tail = " — NOTHING FOUND AT ALL: either this world is bare, or every prefab name " +
                       "is wrong. Check with `cairn prefabs sign` and `cairn prefabs stone`.";
            else if (stonesFound > 0 && pileCount == 0)
                tail = " — stone exists but none of it is shaped like a cairn: too few pieces, " +
                       "too many, or too wide a footprint.";

            Cairn.Log.LogInfo(
                $"[{Name}] sweep complete ({string.Join(", ", parts.ToArray())}) — " +
                $"{pileCount} pile(s), {LandmarkStore.Count} landmark(s), {changed} changed, {pruned} pruned" +
                (_rotationClean ? "" : ", PRUNING SKIPPED (a prefab failed this rotation)") +
                tail);
        }
    }
}
