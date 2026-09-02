namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// The decision the sweep makes about one sign, with no game types involved so it can be
    /// tested off-game: is this a landmark, and if so what is it called and who wrote it?
    ///
    /// Deliberately separate from the sweep itself. The sweep is ZDO plumbing that only a
    /// running server can exercise; this is the part with rules in it, and rules are what
    /// fail silently.
    /// </summary>
    public static class SignReading
    {
        /// <summary>Author recorded when a sign carries no attribution at all.</summary>
        public const string UnknownAuthor = "unknown";

        /// <summary>
        /// Decide whether a sign's raw ZDO fields describe a landmark.
        ///
        /// A blank sign is not a place. Vanilla stores nothing under the text key until
        /// someone writes on one, so an unwritten sign arrives here as an empty string and is
        /// refused — which is also what keeps a newly built sign out of the ledger until it
        /// has been named.
        ///
        /// The AUTHOR recorded is the platform id, never the display name. Display names are
        /// player-authored strings, and putting one in the ledger would double the moderation
        /// surface that this mod has deliberately left unresolved — see the scope doc. The id
        /// is stable, and a name can always be resolved from it later at the display end.
        /// </summary>
        public static bool TryRead(string rawText, string rawAuthor, out string name, out string author)
        {
            name = Landmark.NormaliseName(rawText);
            author = string.IsNullOrWhiteSpace(rawAuthor) ? UnknownAuthor : rawAuthor.Trim();

            return name.Length > 0;
        }
    }
}
