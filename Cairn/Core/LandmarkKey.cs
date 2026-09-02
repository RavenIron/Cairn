using System;
using System.Globalization;
using UnityEngine;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// A landmark's identity: its place, to the nearest metre.
    ///
    /// Keyed by POSITION rather than by the sign's ZDO, deliberately. A player who breaks a
    /// weathered sign and plants a fresh one on the same cairn has not founded a new place —
    /// they have repaired an old one, and the ledger should agree. Keying by object would
    /// silently reset that landmark's history instead.
    ///
    /// The cost of that choice, accepted: two signs within a metre of each other in ALL
    /// THREE axes collapse into one landmark, last writer winning. A metre is smaller than
    /// anything a person would call two different places.
    /// </summary>
    public struct LandmarkKey : IEquatable<LandmarkKey>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public LandmarkKey(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Round to whole metres. MidpointRounding.AwayFromZero on purpose: the default
        /// banker's rounding sends x=0.5 and x=1.5 to the same metre, which would make two
        /// landmarks a metre apart collide while their neighbours did not.
        /// </summary>
        public static LandmarkKey FromPosition(Vector3 p) => new LandmarkKey(
            (int)Math.Round(p.x, MidpointRounding.AwayFromZero),
            (int)Math.Round(p.y, MidpointRounding.AwayFromZero),
            (int)Math.Round(p.z, MidpointRounding.AwayFromZero));

        public bool Equals(LandmarkKey other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is LandmarkKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = X;
                h = (h * 397) ^ Y;
                h = (h * 397) ^ Z;
                return h;
            }
        }

        public static bool operator ==(LandmarkKey a, LandmarkKey b) => a.Equals(b);
        public static bool operator !=(LandmarkKey a, LandmarkKey b) => !a.Equals(b);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
    }
}
