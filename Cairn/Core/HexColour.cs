using System.Globalization;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// Reading a colour a server owner typed into a config file.
    ///
    /// Deliberately free of Unity types so the harness can cover it: hex parsing fails in
    /// quiet, plausible ways — a stray `#`, three digits instead of six, a typo'd letter —
    /// and the failure mode that matters is a beacon silently drawing black rather than
    /// refusing the value.
    /// </summary>
    public static class HexColour
    {
        /// <summary>
        /// Parse `RGB`, `RRGGBB` or `RRGGBBAA`, with or without a leading `#`, into 0..1
        /// components. Returns false and leaves the outputs alone on anything else, so a
        /// caller can keep its default rather than paint with garbage.
        /// </summary>
        public static bool TryParse(string text, out float r, out float g, out float b, out float a)
        {
            r = g = b = a = 0f;
            if (string.IsNullOrEmpty(text)) return false;

            string s = text.Trim();
            if (s.Length > 0 && s[0] == '#') s = s.Substring(1);

            // Shorthand: RGB -> RRGGBB, the way CSS does it, because people type it.
            if (s.Length == 3)
                s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });

            if (s.Length != 6 && s.Length != 8) return false;

            if (!TryByte(s, 0, out int ri)) return false;
            if (!TryByte(s, 2, out int gi)) return false;
            if (!TryByte(s, 4, out int bi)) return false;

            int ai = 255;
            if (s.Length == 8 && !TryByte(s, 6, out ai)) return false;

            r = ri / 255f;
            g = gi / 255f;
            b = bi / 255f;
            a = ai / 255f;
            return true;
        }

        private static bool TryByte(string s, int at, out int value) =>
            int.TryParse(s.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
