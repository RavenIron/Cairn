using System.Collections.Generic;
using UnityEngine;

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
            => Upsert(key, name, author, false, default, seenUtcTicks);

        public static bool Upsert(LandmarkKey key, string name, string author,
                                  bool hasPile, Vector3 light, long seenUtcTicks)
        {
            name = name ?? "";
            author = author ?? "";

            if (_landmarks.TryGetValue(key, out Landmark existing))
            {
                // A pile appearing beside a sign that was already a landmark flips HasPile on
                // the EXISTING row rather than making a new one, so a place that has stood for
                // a hundred days does not become new again the day someone stacks stone on it.
                bool changed = existing.Name != name
                            || existing.Author != author
                            || existing.HasPile != hasPile
                            || Moved(existing.Light, light);

                existing.Name = name;
                existing.Author = author;
                existing.HasPile = hasPile;
                existing.Light = light;
                existing.LastSeenUtcTicks = seenUtcTicks;

                // LastSeen moves on every sweep and is not worth a disk write on its own; it
                // rides along with the next real change or the next forced save.
                if (changed) _dirty = true;
                return changed;
            }

            _landmarks[key] = new Landmark(key, name, author, seenUtcTicks, seenUtcTicks, hasPile, light);
            _dirty = true;
            return true;
        }

        /// <summary>
        /// Has the light moved enough to be worth a disk write? A pile's centroid shifts by
        /// fractions of a metre as stones are added, and writing the file for a millimetre
        /// would make every sweep dirty the store forever.
        /// </summary>
        private static bool Moved(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return dx * dx + dy * dy + dz * dz > 0.01f;   // 10 cm
        }

        /// <summary>
        /// Give a landmark an older history than the one it was founded with.
        ///
        /// Used when an unnamed cairn's crown drifts far enough to round to a new key: the old
        /// row is about to be pruned, and its FirstSeen belongs to the row replacing it. Only
        /// ever moves the date EARLIER — a landmark cannot be made younger, and a carryover
        /// that tried to would be a bug wearing a helpful face.
        /// </summary>
        public static bool CarryHistory(LandmarkKey key, long earlierFirstSeenUtcTicks)
        {
            if (earlierFirstSeenUtcTicks <= 0) return false;
            if (!_landmarks.TryGetValue(key, out Landmark landmark)) return false;
            if (earlierFirstSeenUtcTicks >= landmark.FirstSeenUtcTicks) return false;

            landmark.FirstSeenUtcTicks = earlierFirstSeenUtcTicks;
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
