using System.Text;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// Turning a stored name into something safe to SAY.
    ///
    /// The scope doc left one question deliberately open: a sign's text is player-authored,
    /// and Cairn gives it reach it never had — a name that only a visitor could read is about
    /// to be spoken aloud by a bird. That question comes due the moment a name leaves the sign
    /// it was written on, and this is that moment.
    ///
    /// The conservative answer, as the doc said it should be. STORAGE stays faithful to what
    /// the player typed — `Landmark.NormaliseName` deliberately preserves rich text — and the
    /// DISPLAY end strips it, so the rule can differ per channel without the ledger lying
    /// about its own contents. A name reaching a raven's mouth carries no colour tags, no size
    /// tags, and nothing that could paint over the rest of the dialogue.
    /// </summary>
    public static class DisplayText
    {
        /// <summary>
        /// Remove Unity/TMP rich-text markup, leaving the words.
        ///
        /// Deliberately narrow: only a `&lt;`, an optional `/`, an alphabetic tag name, an
        /// optional `=value` or attributes, and a `&gt;`. Ordinary prose almost never contains
        /// `&lt;word&gt;`, and being greedier would eat text a player meant to keep — a cairn
        /// called "&lt;-- the ford" should survive being spoken.
        /// </summary>
        public static string StripRichText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (raw.IndexOf('<') < 0) return raw;

            var sb = new StringBuilder(raw.Length);

            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] != '<') { sb.Append(raw[i]); continue; }

                int close = TagEnd(raw, i);
                if (close < 0) { sb.Append(raw[i]); continue; }   // not a tag: keep the '<'

                i = close;   // skip the whole tag
            }

            return sb.ToString();
        }

        /// <summary>
        /// Index of the '&gt;' closing a well-formed tag starting at <paramref name="open"/>,
        /// or -1 when what follows is not a tag at all.
        /// </summary>
        private static int TagEnd(string s, int open)
        {
            int i = open + 1;
            if (i < s.Length && s[i] == '/') i++;

            int nameStart = i;
            while (i < s.Length && (char.IsLetter(s[i]) || s[i] == '-')) i++;
            if (i == nameStart) return -1;   // no tag name: "< b" or "<3"

            // Attributes or a value, up to the closing bracket. A newline inside means this
            // was never a tag.
            while (i < s.Length && s[i] != '>')
            {
                if (s[i] == '<' || s[i] == '\n') return -1;
                i++;
            }

            return i < s.Length ? i : -1;
        }

        /// <summary>
        /// A name as the raven should say it: markup gone, whitespace tidied, and bounded so
        /// one enormous sign cannot fill the dialogue box.
        /// </summary>
        public static string ForSpeech(string storedName, int maxLength)
        {
            string clean = Landmark.NormaliseName(StripRichText(storedName));
            if (clean.Length <= maxLength) return clean;

            return clean.Substring(0, maxLength).TrimEnd() + "…";
        }
    }
}
