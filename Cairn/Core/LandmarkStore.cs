using System.Collections.Generic;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// The landmark ledger, in memory. Persistence serialises it; the sweep fills it; the
    /// console reads it.
    ///
    /// Write-behind, like every store in this studio's lineage: mutations set a dirty flag
    /// and the tick writes on a cadence. A store that wrote on every change would hit disk
    /// on every sweep of a large world; one that never tracked changes would write nothing
    /// on an idle server or everything on a busy one.
    /// </summary>
    public static class LandmarkStore
    {
        private static readonly Dictionary<LandmarkKey, Landmark> _landmarks =
            new Dictionary<LandmarkKey, Landmark>(64);

        private static bool _dirty;

        public static int Count => _landmarks.Count;
        public static bool IsDirty => _dirty;

        public static void MarkClean() => _dirty = false;
        public static void MarkDirty() => _dirty = true;

        public static bool TryGet(LandmarkKey key, out Landmark landmark) =>
            _landmarks.TryGetValue(key, out landmark);

        /// <summary>
        /// Record a sighting.
        ///
        /// FirstSeen is preserved across upserts — that is the whole reason this is an upsert
        /// and not an assignment. A landmark that has stood for a hundred days must not
        /// become new again because a sweep looked at it.
        ///
        /// Returns true when this call actually changed something. A sweep over an unchanged
        /// world should not mark the store dirty, or the autosave writes an identical file
        /// forever.
        /// </summary>
        public static bool Upsert(LandmarkKey key, string name, string author, long seenUtcTicks)
        {
            name = name ?? "";
            author = author ?? "";

            if (_landmarks.TryGetValue(key, out Landmark existing))
            {
                bool changed = existing.Name != name || existing.Author != author;

                existing.Name = name;
                existing.Author = author;
                existing.LastSeenUtcTicks = seenUtcTicks;

                // LastSeen moves on every sweep and is not worth a disk write on its own; it
                // rides along with the next real change or the next forced save.
                if (changed) _dirty = true;
                return changed;
            }

            _landmarks[key] = new Landmark(key, name, author, seenUtcTicks, seenUtcTicks);
            _dirty = true;
            return true;
        }

        /// <summary>Restore a landmark verbatim, preserving its stored timestamps. Load only.</summary>
        public static void Put(Landmark landmark)
        {
            if (landmark == null) return;
            _landmarks[landmark.Key] = landmark;
        }

        public static bool Remove(LandmarkKey key)
        {
            if (!_landmarks.Remove(key)) return false;
            _dirty = true;
            return true;
        }

        /// <summary>A copy, safe to enumerate while the sweep mutates the store.</summary>
        public static List<Landmark> Snapshot() => new List<Landmark>(_landmarks.Values);

        public static void Clear()
        {
            _landmarks.Clear();
            _dirty = false;
        }
    }
}
