using System;
using System.Collections.Generic;
using UnityEngine;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// Finding a cairn in a world full of stone.
    ///
    /// Cairn adds no prefabs, so a pile is DETECTED rather than provided: a small, tight
    /// stack of ordinary stone pieces is a cairn. That means the rule has to tell a waymark
    /// apart from a wall, a tower and a stone house — and it does it by FOOTPRINT, because
    /// that is the thing a cairn cannot fake. A cairn is narrow and short; a building is
    /// wide. Piece count alone would not do it: a tidy 6-piece wall is a wall.
    ///
    /// No game types beyond Vector3, so the whole rule is testable off-game. That matters
    /// more here than anywhere else in the mod — a detection rule that is slightly wrong
    /// does not throw, it just quietly decides someone's front step is a landmark.
    /// </summary>
    public static class PileDetection
    {
        public struct Pile
        {
            /// <summary>Centre of the footprint, at the height of the topmost stone — where a light would sit.</summary>
            public Vector3 Top;
            public int Pieces;
            public float ExtentXZ;
        }

        /// <summary>
        /// Group stones into piles and keep the ones shaped like a cairn.
        ///
        /// Grouping is single-linkage through a spatial grid rather than an all-pairs sweep:
        /// a world's stone pieces run to thousands, and O(n²) inside a tick is how a sweep
        /// becomes a stutter. Cell size is the link radius, so only the 27 neighbouring cells
        /// are ever compared.
        /// </summary>
        public static List<Pile> Find(
            IList<Vector3> stones,
            float linkMeters,
            int minPieces,
            int maxPieces,
            float maxExtentMeters)
        {
            var piles = new List<Pile>();
            if (stones == null || stones.Count == 0) return piles;
            if (linkMeters <= 0f) return piles;

            // Spatial grid: cell key -> indices in that cell.
            var grid = new Dictionary<long, List<int>>(stones.Count);
            for (int i = 0; i < stones.Count; i++)
            {
                long key = CellKey(stones[i], linkMeters);
                if (!grid.TryGetValue(key, out List<int> bucket))
                {
                    bucket = new List<int>(4);
                    grid[key] = bucket;
                }
                bucket.Add(i);
            }

            var visited = new bool[stones.Count];
            var queue = new Queue<int>();
            var cluster = new List<int>(16);

            for (int start = 0; start < stones.Count; start++)
            {
                if (visited[start]) continue;

                cluster.Clear();
                queue.Clear();
                queue.Enqueue(start);
                visited[start] = true;

                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    cluster.Add(i);

                    // A cluster may grow past the cap; it is still walked to completion so the
                    // whole structure is consumed and cannot be re-found as a smaller piece.
                    foreach (int j in Neighbours(grid, stones, i, linkMeters))
                    {
                        if (visited[j]) continue;
                        visited[j] = true;
                        queue.Enqueue(j);
                    }
                }

                if (cluster.Count < minPieces || cluster.Count > maxPieces) continue;

                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                float topY = float.MinValue;
                float sumX = 0f, sumZ = 0f;

                for (int k = 0; k < cluster.Count; k++)
                {
                    Vector3 p = stones[cluster[k]];
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z;
                    if (p.z > maxZ) maxZ = p.z;
                    if (p.y > topY) topY = p.y;
                    sumX += p.x;
                    sumZ += p.z;
                }

                float extent = Math.Max(maxX - minX, maxZ - minZ);
                if (extent > maxExtentMeters) continue;   // a building, not a waymark

                piles.Add(new Pile
                {
                    Top = new Vector3(sumX / cluster.Count, topY, sumZ / cluster.Count),
                    Pieces = cluster.Count,
                    ExtentXZ = extent
                });
            }

            return piles;
        }

        /// <summary>
        /// The nearest named sign to a pile, within reach. Returns -1 when the pile is
        /// unnamed — which is a legitimate cairn, not a failure: an unnamed pile is a lit
        /// waymark, and only its name is missing.
        ///
        /// Distance is XZ-planar. A sign set into the side of a cairn sits metres below its
        /// top, and a 3D check would push it out of reach for no reason a player could see —
        /// the same trap that made RW's announcements invisible to anyone standing on a hill.
        /// </summary>
        public static int NearestSign(Vector3 pileTop, IList<Vector3> signPositions, float withinMeters)
        {
            if (signPositions == null || signPositions.Count == 0) return -1;

            int best = -1;
            float bestSq = withinMeters * withinMeters;

            for (int i = 0; i < signPositions.Count; i++)
            {
                float dx = signPositions[i].x - pileTop.x;
                float dz = signPositions[i].z - pileTop.z;
                float sq = dx * dx + dz * dz;

                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }

            return best;
        }

        private static long CellKey(Vector3 p, float cell)
        {
            long cx = (long)Math.Floor(p.x / cell);
            long cy = (long)Math.Floor(p.y / cell);
            long cz = (long)Math.Floor(p.z / cell);

            // Three 21-bit fields. Worlds are ~10km across and cells are metres, so the range
            // is never approached; the mask keeps a rogue coordinate from corrupting a key.
            return ((cx & 0x1FFFFF) << 42) | ((cy & 0x1FFFFF) << 21) | (cz & 0x1FFFFF);
        }

        private static IEnumerable<int> Neighbours(
            Dictionary<long, List<int>> grid, IList<Vector3> stones, int index, float linkMeters)
        {
            Vector3 p = stones[index];
            float linkSq = linkMeters * linkMeters;

            long bx = (long)Math.Floor(p.x / linkMeters);
            long by = (long)Math.Floor(p.y / linkMeters);
            long bz = (long)Math.Floor(p.z / linkMeters);

            for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
            for (long dz = -1; dz <= 1; dz++)
            {
                long key = (((bx + dx) & 0x1FFFFF) << 42)
                         | (((by + dy) & 0x1FFFFF) << 21)
                         | ((bz + dz) & 0x1FFFFF);

                if (!grid.TryGetValue(key, out List<int> bucket)) continue;

                for (int k = 0; k < bucket.Count; k++)
                {
                    int j = bucket[k];
                    if (j == index) continue;

                    Vector3 q = stones[j];
                    float ex = q.x - p.x, ey = q.y - p.y, ez = q.z - p.z;
                    if (ex * ex + ey * ey + ez * ez <= linkSq) yield return j;
                }
            }
        }
    }
}
