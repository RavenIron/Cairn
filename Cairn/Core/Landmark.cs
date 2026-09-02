using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// One named place: where it is, what it is called, who wrote the sign, and when it was
    /// first and last seen by a sweep.
    ///
    /// FirstSeen is the only field the sweep may not overwrite. It is what makes a landmark
    /// something with a history rather than a row in a cache, and it is why the store
    /// upserts rather than replaces.
    /// </summary>
    public sealed class Landmark
    {
        /// <summary>
        /// Storage bound, not a display rule. Vanilla's own `Sign.m_characterLimit` is 50, so
        /// anything longer arrived from another mod; 128 keeps such a name rather than
        /// truncating it to vanilla's taste, while refusing to store a novel.
        /// </summary>
        public const int MaxNameLength = 128;

        public LandmarkKey Key;
        public string Name;
        public string Author;
        public long FirstSeenUtcTicks;
        public long LastSeenUtcTicks;

        /// <summary>Is there a stack of stone here? This is what earns the light.</summary>
        public bool HasPile;

        /// <summary>
        /// Where the light burns: the crown of the pile, which is NOT the landmark's key.
        /// A sign names a place from beside it or below it, so the thing you read and the
        /// thing you steer by sit metres apart. Stored rather than recomputed so a client
        /// can be told where to draw before the first sweep after a restart.
        /// </summary>
        public Vector3 Light;

        public Landmark() { }

        public Landmark(LandmarkKey key, string name, string author, long firstSeen, long lastSeen)
            : this(key, name, author, firstSeen, lastSeen, false, default) { }

        public Landmark(LandmarkKey key, string name, string author, long firstSeen, long lastSeen,
                        bool hasPile, Vector3 light)
        {
            Key = key;
            Name = name ?? "";
            Author = author ?? "";
            FirstSeenUtcTicks = firstSeen;
            LastSeenUtcTicks = lastSeen;
            HasPile = hasPile;
            Light = light;
        }

        public bool IsNamed => !string.IsNullOrEmpty(Name);

        /// <summary>
        /// Worth keeping on disk. A named sign is a place; an unnamed pile is a lit waymark
        /// and just as real. Only a landmark that is neither is nothing at all.
        /// </summary>
        public bool IsWorthStoring => IsNamed || HasPile;

        /// <summary>
        /// Collapse a sign's raw text into a one-line name: whitespace runs (newlines
        /// included — sign text is multi-line) become single spaces, ends are trimmed, and
        /// the result is bounded.
        ///
        /// Rich-text tags are deliberately NOT stripped here. Storage should be faithful to
        /// what the player typed; deciding that `&lt;color=red&gt;` should not reach a raven's
        /// mouth is a display concern, and it belongs at the display end where the rule can
        /// differ per channel.
        /// </summary>
        public static string NormaliseName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            var sb = new StringBuilder(raw.Length);
            bool pendingSpace = false;

            foreach (char c in raw)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0) pendingSpace = true;
                    continue;
                }

                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }

                sb.Append(c);
                if (sb.Length >= MaxNameLength) break;
            }

            return sb.ToString();
        }

        // ---- serialization --------------------------------------------------------------

        // Field counts, by format version. v1 had no pile: seven fields ending in the name.
        private const int V1Fields = 7;
        private const int V2Fields = 11;

        /// <summary>
        /// Tab-separated, name last. Every free-text field is escaped, because a sign's text
        /// is whatever a player typed and a raw tab or newline in it would silently shift
        /// every field after it — a corruption that parses cleanly into wrong data, which is
        /// worse than one that fails.
        /// </summary>
        public string Format()
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(128);
            sb.Append(Key.X.ToString(c)).Append('\t')
              .Append(Key.Y.ToString(c)).Append('\t')
              .Append(Key.Z.ToString(c)).Append('\t')
              .Append(FirstSeenUtcTicks.ToString(c)).Append('\t')
              .Append(LastSeenUtcTicks.ToString(c)).Append('\t')
              .Append(Escape(Author)).Append('\t')
              .Append(HasPile ? '1' : '0').Append('\t')
              // "R" round-trips a float exactly. These are the mod's first non-integer
              // fields on disk, so this is where InvariantCulture stops being a formality:
              // a comma-decimal machine would otherwise write "12,5" into a tab-separated
              // file that parses fine at home and corrupts abroad.
              .Append(Light.x.ToString("R", c)).Append('\t')
              .Append(Light.y.ToString("R", c)).Append('\t')
              .Append(Light.z.ToString("R", c)).Append('\t')
              .Append(Escape(Name));
            return sb.ToString();
        }

        /// <summary>
        /// Reads v2 and v1 alike. A v1 row predates the pile entirely, so it restores as a
        /// named place with no light — which is exactly what it was. Migration is a read
        /// concern only: the next save writes it back as v2.
        ///
        /// Not hypothetical. A live server wrote a v1 store on 2026-09-02, and it should
        /// keep its landmark rather than quietly lose it to a format bump.
        /// </summary>
        public static bool TryParse(string line, out Landmark landmark)
        {
            landmark = null;
            if (string.IsNullOrEmpty(line)) return false;

            string[] f = line.Split('\t');
            if (f.Length < V1Fields) return false;

            var c = CultureInfo.InvariantCulture;
            if (!int.TryParse(f[0], NumberStyles.Integer, c, out int x)) return false;
            if (!int.TryParse(f[1], NumberStyles.Integer, c, out int y)) return false;
            if (!int.TryParse(f[2], NumberStyles.Integer, c, out int z)) return false;
            if (!long.TryParse(f[3], NumberStyles.Integer, c, out long first)) return false;
            if (!long.TryParse(f[4], NumberStyles.Integer, c, out long last)) return false;

            string author = Unescape(f[5]);
            var key = new LandmarkKey(x, y, z);

            if (f.Length < V2Fields)
            {
                landmark = new Landmark(key, Unescape(f[6]), author, first, last);
                return true;
            }

            bool hasPile = f[6] == "1";
            if (!float.TryParse(f[7], NumberStyles.Float, c, out float lx)) return false;
            if (!float.TryParse(f[8], NumberStyles.Float, c, out float ly)) return false;
            if (!float.TryParse(f[9], NumberStyles.Float, c, out float lz)) return false;

            landmark = new Landmark(key, Unescape(f[10]), author, first, last,
                                    hasPile, new Vector3(lx, ly, lz));
            return true;
        }

        /// <summary>
        /// Backslash escaping for the four characters that would break a line-and-tab format.
        /// Backslash goes first on the way out and is handled by the state machine on the way
        /// back, so a name that legitimately contains "\t" survives a round trip intact.
        /// </summary>
        internal static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        internal static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf('\\') < 0) return s;

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(s[i]);
                    continue;
                }

                i++;
                switch (s[i])
                {
                    case '\\': sb.Append('\\'); break;
                    case 't': sb.Append('\t'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;

                    // An unknown escape is data, not an error: keep both characters rather
                    // than silently eating the backslash a player typed.
                    default: sb.Append('\\').Append(s[i]); break;
                }
            }
            return sb.ToString();
        }
    }
}
