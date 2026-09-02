using System;
using System.Globalization;
using System.Text;

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

        public Landmark() { }

        public Landmark(LandmarkKey key, string name, string author, long firstSeen, long lastSeen)
        {
            Key = key;
            Name = name ?? "";
            Author = author ?? "";
            FirstSeenUtcTicks = firstSeen;
            LastSeenUtcTicks = lastSeen;
        }

        public bool IsNamed => !string.IsNullOrEmpty(Name);

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

        /// <summary>
        /// Tab-separated, name last. Every free-text field is escaped, because a sign's text
        /// is whatever a player typed and a raw tab or newline in it would silently shift
        /// every field after it — a corruption that parses cleanly into wrong data, which is
        /// worse than one that fails.
        /// </summary>
        public string Format()
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(96);
            sb.Append(Key.X.ToString(c)).Append('\t')
              .Append(Key.Y.ToString(c)).Append('\t')
              .Append(Key.Z.ToString(c)).Append('\t')
              .Append(FirstSeenUtcTicks.ToString(c)).Append('\t')
              .Append(LastSeenUtcTicks.ToString(c)).Append('\t')
              .Append(Escape(Author)).Append('\t')
              .Append(Escape(Name));
            return sb.ToString();
        }

        public static bool TryParse(string line, out Landmark landmark)
        {
            landmark = null;
            if (string.IsNullOrEmpty(line)) return false;

            string[] f = line.Split('\t');
            if (f.Length < 7) return false;

            var c = CultureInfo.InvariantCulture;
            if (!int.TryParse(f[0], NumberStyles.Integer, c, out int x)) return false;
            if (!int.TryParse(f[1], NumberStyles.Integer, c, out int y)) return false;
            if (!int.TryParse(f[2], NumberStyles.Integer, c, out int z)) return false;
            if (!long.TryParse(f[3], NumberStyles.Integer, c, out long first)) return false;
            if (!long.TryParse(f[4], NumberStyles.Integer, c, out long last)) return false;

            landmark = new Landmark(
                new LandmarkKey(x, y, z),
                Unescape(f[6]),
                Unescape(f[5]),
                first,
                last);
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
