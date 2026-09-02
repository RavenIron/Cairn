using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using RavenIron.Cairn;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace Cairn.Tests
{
    /// <summary>
    /// Off-game harness for the pure-logic core. No test framework by design — a console
    /// program returning a nonzero exit code is enough, and adds no dependency to keep
    /// current.
    ///
    /// What it is actually for: serialization fails SILENTLY. A name containing a tab, a
    /// comma-decimal locale, or a BOM does not throw — it writes a file that reads back as
    /// plausible, wrong data on someone else's machine, weeks later. That is precisely the
    /// class of bug worth catching without launching the game.
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;
        private static string _tempRoot;

        public static int Main()
        {
            Console.WriteLine("Cairn — core tests\n");

            ModConfig.Bind(new ConfigFile());

            _tempRoot = Path.Combine(Path.GetTempPath(), "cairn-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            try
            {
                LandmarkKeyTests();
                NameTests();
                EscapeTests();
                FormatParseTests();
                SignReadingTests();
                DisplayTextTests();
                HexColourTests();
                PileDetectionTests();
                StoreTests();
                PersistenceTests();
            }
            finally
            {
                try { Directory.Delete(_tempRoot, recursive: true); } catch { }
            }

            Console.WriteLine($"\n{_passed} passed, {_failed} failed.");
            return _failed == 0 ? 0 : 1;
        }

        // ---- harness -------------------------------------------------------------------

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; return; }
            _failed++;
            Console.WriteLine($"  FAIL  {what}");
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine($"  FAIL  {what}\n          expected [{expected}]\n          actual   [{actual}]");
        }

        private static void Section(string name) => Console.WriteLine(name);

        // ---- LandmarkKey ---------------------------------------------------------------

        private static void LandmarkKeyTests()
        {
            Section("LandmarkKey");

            var a = LandmarkKey.FromPosition(new Vector3(10.2f, 30.4f, -5.1f));
            Equal(new LandmarkKey(10, 30, -5), a, "rounds to whole metres");

            var b = LandmarkKey.FromPosition(new Vector3(10.4f, 30.4f, -5.4f));
            Check(a == b, "positions within a metre collapse to one landmark");

            var c = LandmarkKey.FromPosition(new Vector3(11.6f, 30.4f, -5.1f));
            Check(a != c, "a metre and a half apart are different landmarks");

            // Banker's rounding would send 0.5 and 1.5 both to even metres, colliding pairs
            // that ought to differ. AwayFromZero keeps the grid uniform.
            Equal(new LandmarkKey(1, 0, 0), LandmarkKey.FromPosition(new Vector3(0.5f, 0f, 0f)),
                  "0.5 rounds away from zero, not to even");
            Equal(new LandmarkKey(2, 0, 0), LandmarkKey.FromPosition(new Vector3(1.5f, 0f, 0f)),
                  "1.5 rounds away from zero, not to even");
            Equal(new LandmarkKey(-1, 0, 0), LandmarkKey.FromPosition(new Vector3(-0.5f, 0f, 0f)),
                  "negative halves round away from zero too");

            var set = new HashSet<LandmarkKey> { new LandmarkKey(1, 2, 3), new LandmarkKey(1, 2, 3) };
            Equal(1, set.Count, "equal keys hash to one slot");

            Check(new LandmarkKey(1, 2, 3) != new LandmarkKey(3, 2, 1),
                  "axes are not interchangeable in the hash");
        }

        // ---- name normalisation --------------------------------------------------------

        private static void NameTests()
        {
            Section("Landmark.NormaliseName");

            Equal("Two Rocks", Landmark.NormaliseName("Two Rocks"), "plain name survives");
            Equal("Two Rocks", Landmark.NormaliseName("  Two Rocks  "), "ends are trimmed");
            Equal("Two Rocks", Landmark.NormaliseName("Two\nRocks"), "newline becomes a space");
            Equal("Two Rocks", Landmark.NormaliseName("Two \t\r\n Rocks"), "whitespace runs collapse");
            Equal("", Landmark.NormaliseName(""), "empty stays empty");
            Equal("", Landmark.NormaliseName(null), "null is not a crash");
            Equal("", Landmark.NormaliseName("   \n\t "), "whitespace only becomes empty");

            string long_ = new string('x', Landmark.MaxNameLength + 40);
            Equal(Landmark.MaxNameLength, Landmark.NormaliseName(long_).Length, "over-long names are bounded");

            // Rich text is storage-faithful on purpose; stripping is a display decision.
            Equal("<color=red>Home</color>", Landmark.NormaliseName("<color=red>Home</color>"),
                  "rich text is preserved, not stripped, at the storage layer");
        }

        // ---- escaping ------------------------------------------------------------------

        private static void EscapeTests()
        {
            Section("escaping");

            RoundTrip("plain");
            RoundTrip("with\ttab");
            RoundTrip("with\nnewline");
            RoundTrip("with\r\ncrlf");
            RoundTrip("with\\backslash");
            RoundTrip("literal \\t that is not a tab");
            RoundTrip("");
            RoundTrip("everything \\ \t \n \r at once");

            Check(Landmark.Escape("a\tb").IndexOf('\t') < 0, "no raw tab survives escaping");
            Check(Landmark.Escape("a\nb").IndexOf('\n') < 0, "no raw newline survives escaping");
            Equal("\\keep", Landmark.Unescape("\\keep"), "an unknown escape keeps both characters");
        }

        private static void RoundTrip(string s)
        {
            Equal(s, Landmark.Unescape(Landmark.Escape(s)), $"escape round trip: [{Show(s)}]");
        }

        private static string Show(string s) =>
            (s ?? "").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

        // ---- format / parse -------------------------------------------------------------

        private static void FormatParseTests()
        {
            Section("Landmark format/parse");

            var original = new Landmark(new LandmarkKey(-12, 34, 56), "Two Rocks", "host", 111L, 222L);
            Check(Landmark.TryParse(original.Format(), out Landmark back), "a formatted line parses");
            Equal(original.Key, back.Key, "key round trips");
            Equal("Two Rocks", back.Name, "name round trips");
            Equal("host", back.Author, "author round trips");
            Equal(111L, back.FirstSeenUtcTicks, "firstSeen round trips");
            Equal(222L, back.LastSeenUtcTicks, "lastSeen round trips");

            // The whole reason fields are escaped: a sign's text is whatever a player typed.
            var nasty = new Landmark(new LandmarkKey(1, 2, 3), "a\tb\nc\\d", "auth\tor", 1L, 2L);
            Check(Landmark.TryParse(nasty.Format(), out Landmark nastyBack),
                  "a name full of separators still parses");
            Equal("a\tb\nc\\d", nastyBack.Name, "separators in a name survive the round trip");
            Equal("auth\tor", nastyBack.Author, "separators in an author survive the round trip");

            // --- v2: the pile, the light, and the first floats to reach disk ----------------
            var lit = new Landmark(new LandmarkKey(10, 49, 27), "Two Rocks", "Steam_7656", 111L, 222L,
                                   true, new Vector3(10.25f, 51.5f, 27.75f));
            Check(Landmark.TryParse(lit.Format(), out Landmark litBack), "a v2 row parses");
            Check(litBack.HasPile, "the pile flag survives");
            Check(Math.Abs(litBack.Light.x - 10.25f) < 0.0001f, "light x round trips exactly");
            Check(Math.Abs(litBack.Light.y - 51.5f) < 0.0001f, "light y round trips exactly");
            Check(Math.Abs(litBack.Light.z - 27.75f) < 0.0001f, "light z round trips exactly");
            Equal("Two Rocks", litBack.Name, "the name still comes last and still round trips");

            var unlit = new Landmark(new LandmarkKey(1, 2, 3), "Plain", "host", 1L, 2L);
            Check(Landmark.TryParse(unlit.Format(), out Landmark unlitBack), "a pileless v2 row parses");
            Check(!unlitBack.HasPile, "no pile stays no pile");

            // --- v1 migration: a live server wrote one of these on 2026-09-02 ---------------
            const string v1 = "10\t49\t27\t639239624414309706\t639239624414309706\tSteam_76561198392625778\ttest";
            Check(Landmark.TryParse(v1, out Landmark old), "a v1 row still parses after the format bump");
            Equal("test", old.Name, "the v1 name is read from field 7, not field 11");
            Equal("Steam_76561198392625778", old.Author, "the v1 author survives");
            Equal(new LandmarkKey(10, 49, 27), old.Key, "the v1 key survives");
            Equal(639239624414309706L, old.FirstSeenUtcTicks, "the v1 history survives");
            Check(!old.HasPile, "a v1 row predates piles, so it has none");

            // --- what makes a landmark worth keeping ---------------------------------------
            Check(new Landmark(new LandmarkKey(0,0,0), "named", "host", 1L, 2L).IsWorthStoring,
                  "a named place is worth storing");
            Check(new Landmark(new LandmarkKey(0,0,0), "", "host", 1L, 2L, true, default).IsWorthStoring,
                  "an UNNAMED lit pile is worth storing — it is a waymark, not a blank");
            Check(!new Landmark(new LandmarkKey(0,0,0), "", "host", 1L, 2L).IsWorthStoring,
                  "neither named nor lit is nothing at all");

            Check(!Landmark.TryParse("", out _), "empty line is rejected");
            Check(!Landmark.TryParse(null, out _), "null line is rejected");
            Check(!Landmark.TryParse("1\t2\t3", out _), "a short line is rejected");
            Check(!Landmark.TryParse("x\t2\t3\t4\t5\tauthor\tname", out _), "a bad x is rejected");
            Check(!Landmark.TryParse("1\t2\t3\tzzz\t5\tauthor\tname", out _), "a bad timestamp is rejected");

            // A culture test is only worth having if it CAN fail.
            //
            // The first version of this used de-DE and was decoration: every field in this
            // format is an integer, and integers render identically under de-DE, so it passed
            // even with Format switched to CurrentCulture. Proven by reverting the fix, which
            // is the only reason it was caught.
            //
            // This culture's NEGATIVE SIGN is U+2212 MINUS SIGN rather than ASCII hyphen, so
            // the moment either side stops being invariant, a negative coordinate changes on
            // disk. Built by hand rather than picked from the OS: which real locale uses which
            // sign varies with the ICU version, and a test that depends on that is a test that
            // fails on someone else's machine for the wrong reason.
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                var hostile = (System.Globalization.CultureInfo)
                    new System.Globalization.CultureInfo("de-DE").Clone();
                hostile.NumberFormat.NegativeSign = "−";
                System.Threading.Thread.CurrentThread.CurrentCulture = hostile;

                var abroad = new Landmark(new LandmarkKey(-7, 8, 9), "Nordwacht", "host", 12345L, 67890L,
                                          true, new Vector3(-7.5f, 8.25f, 9f));
                string line = abroad.Format();

                Check(line.IndexOf('−') < 0, "no locale-specific minus sign reaches disk");
                // The light is the mod's first non-integer field, so a comma-decimal machine
                // would write "8,25" into a tab-separated file: parses at home, corrupts abroad.
                Check(line.IndexOf(',') < 0, "no comma-decimal reaches disk");
                Check(line.IndexOf("8.25", StringComparison.Ordinal) >= 0,
                      "a fractional light coordinate writes a POINT whatever the machine's locale");

                Check(Landmark.TryParse("-7\t8\t9\t1\t2\thost\t1\t-7.5\t8.25\t9\tNordwacht", out Landmark read),
                      "a stored row still parses under a hostile locale");
                Equal(-7, read.Key.X, "and parses to the right value");
                Check(Math.Abs(read.Light.y - 8.25f) < 0.0001f, "including its fractional light");
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        // ---- sign reading ---------------------------------------------------------------

        private static void SignReadingTests()
        {
            Section("SignReading");

            // A blank sign is not a place. Vanilla writes nothing under the text key until
            // someone writes on one, so an unwritten sign arrives here as an empty string —
            // this is what keeps every freshly built sign out of the ledger.
            Check(!SignReading.TryRead("", "host", out _, out _), "an unwritten sign is not a landmark");
            Check(!SignReading.TryRead(null, "host", out _, out _), "a null text is not a landmark");
            Check(!SignReading.TryRead("   \n\t ", "host", out _, out _),
                  "a sign of pure whitespace is not a landmark");

            Check(SignReading.TryRead("Two Rocks", "host", out string name, out string author),
                  "a named sign is a landmark");
            Equal("Two Rocks", name, "the name comes through");
            Equal("host", author, "the author comes through");

            SignReading.TryRead("Gull\nCliff", "host", out name, out _);
            Equal("Gull Cliff", name, "a two-line sign becomes a one-line name");

            SignReading.TryRead("Two Rocks", "", out _, out author);
            Equal(SignReading.UnknownAuthor, author, "a sign with no author is attributed to nobody");

            SignReading.TryRead("Two Rocks", "   ", out _, out author);
            Equal(SignReading.UnknownAuthor, author, "a whitespace author is attributed to nobody");

            SignReading.TryRead("Two Rocks", null, out _, out author);
            Equal(SignReading.UnknownAuthor, author, "a null author does not crash the sweep");

            SignReading.TryRead("Two Rocks", "  76561198000000000  ", out _, out author);
            Equal("76561198000000000", author, "an author id is trimmed");

            SignReading.TryRead(new string('x', Landmark.MaxNameLength + 50), "host", out name, out _);
            Equal(Landmark.MaxNameLength, name.Length, "an over-long sign is bounded before it is stored");
        }

        // ---- what the raven may say -------------------------------------------------------

        private static void DisplayTextTests()
        {
            Section("DisplayText");

            // The moderation question, answered: storage stays faithful, the display strips.
            Equal("Home", DisplayText.StripRichText("<color=red>Home</color>"), "colour tags are stripped");
            Equal("Home", DisplayText.StripRichText("<b><i>Home</i></b>"), "nested tags are stripped");
            Equal("Home", DisplayText.StripRichText("<size=200>Home</size>"), "size tags are stripped");
            Equal("Two Rocks", DisplayText.StripRichText("Two Rocks"), "plain text is untouched");
            Equal("", DisplayText.StripRichText(""), "empty stays empty");
            Equal("", DisplayText.StripRichText(null), "null is not a crash");

            // Narrow on purpose: a player's punctuation must survive being spoken.
            Equal("<-- the ford", DisplayText.StripRichText("<-- the ford"),
                  "an arrow is not a tag and is kept");
            Equal("a < b", DisplayText.StripRichText("a < b"), "a lone bracket is kept");
            Equal("cost <3 stone", DisplayText.StripRichText("cost <3 stone"), "<3 is not a tag");
            Equal("<unclosed", DisplayText.StripRichText("<unclosed"), "an unclosed tag is kept as text");

            // A tag cannot span a line break, so a two-line sign cannot smuggle one.
            Equal("<a\nb>", DisplayText.StripRichText("<a\nb>"), "a newline means it was never a tag");

            // Speech: stripped, collapsed, bounded.
            Equal("Gull Cliff", DisplayText.ForSpeech("<color=red>Gull</color>\nCliff", 64),
                  "a two-line coloured sign becomes one clean line");
            Check(DisplayText.ForSpeech(new string('x', 200), 20).Length <= 21,
                  "an enormous name is bounded before it reaches the dialogue box");
            Check(DisplayText.ForSpeech(new string('x', 200), 20).EndsWith("…"),
                  "and says it was cut");
        }

        // ---- the beacon colour -------------------------------------------------------------

        private static void HexColourTests()
        {
            Section("HexColour");

            Check(HexColour.TryParse("FFB85A", out float r, out float g, out float b, out float a),
                  "a plain six-digit hex parses");
            Check(Math.Abs(r - 1f) < 0.001f, "red is full");
            Check(Math.Abs(g - 0.722f) < 0.005f, "green is 0xB8");
            Check(Math.Abs(b - 0.353f) < 0.005f, "blue is 0x5A");
            Check(Math.Abs(a - 1f) < 0.001f, "alpha defaults to opaque");

            Check(HexColour.TryParse("#FFB85A", out _, out _, out _, out _), "a leading hash is allowed");
            Check(HexColour.TryParse("  #FFB85A  ", out _, out _, out _, out _), "surrounding space is allowed");
            Check(HexColour.TryParse("ffb85a", out _, out _, out _, out _), "lower case is allowed");

            // Shorthand, because people type it.
            Check(HexColour.TryParse("F00", out r, out g, out b, out _), "three-digit shorthand parses");
            Check(Math.Abs(r - 1f) < 0.001f && g < 0.001f && b < 0.001f, "and expands correctly");

            Check(HexColour.TryParse("FFB85A80", out _, out _, out _, out a), "eight digits carry alpha");
            Check(Math.Abs(a - 0.502f) < 0.005f, "and the alpha is read");

            // The failures that matter: refusing beats painting with garbage.
            Check(!HexColour.TryParse("", out _, out _, out _, out _), "empty is refused");
            Check(!HexColour.TryParse(null, out _, out _, out _, out _), "null is refused");
            Check(!HexColour.TryParse("FFBB", out _, out _, out _, out _), "four digits are refused");
            Check(!HexColour.TryParse("GGGGGG", out _, out _, out _, out _), "non-hex letters are refused");
            Check(!HexColour.TryParse("orange", out _, out _, out _, out _), "a colour name is refused");

            // A refusal must not scribble on the caller's outputs.
            r = 0.5f;
            HexColour.TryParse("nonsense", out r, out _, out _, out _);
            Check(Math.Abs(r) < 0.001f, "a refused parse zeroes rather than half-writing");
        }

        // ---- pile detection --------------------------------------------------------------

        private static void PileDetectionTests()
        {
            Section("PileDetection");

            const float link = 2.5f, extent = 4f;
            const int min = 3, max = 12;

            // A cairn: a short tight stack.
            var stack = new List<Vector3> {
                new Vector3(0f, 0f, 0f), new Vector3(0.3f, 0.8f, 0.1f), new Vector3(0f, 1.6f, 0.2f)
            };
            List<PileDetection.Pile> piles = PileDetection.Find(stack, link, min, max, extent);
            Equal(1, piles.Count, "a tight stack of three is a cairn");
            Equal(3, piles[0].Pieces, "all three stones belong to it");
            Check(Math.Abs(piles[0].Top.y - 1.6f) < 0.001f, "the light sits at the height of the topmost stone");
            Check(Math.Abs(piles[0].Top.x - 0.1f) < 0.01f, "the light sits over the centre of the footprint");

            // Too few.
            Equal(0, PileDetection.Find(
                new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(0f, 0.8f, 0f) },
                link, min, max, extent).Count, "two stones are not a cairn");

            // A WALL. Same piece count as a cairn, spread out — footprint is what tells them
            // apart, and piece count alone never could.
            var wall = new List<Vector3> {
                new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f),
                new Vector3(6f, 0f, 0f), new Vector3(8f, 0f, 0f)
            };
            Equal(0, PileDetection.Find(wall, link, min, max, extent).Count,
                  "a five-piece wall is not a cairn, despite a legal piece count");

            // A house: too many pieces AND too wide.
            var house = new List<Vector3>();
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                    house.Add(new Vector3(x * 2f, 0f, z * 2f));
            Equal(0, PileDetection.Find(house, link, min, max, extent).Count, "a stone house is not a cairn");

            // Two cairns far apart stay two.
            var pair = new List<Vector3> {
                new Vector3(0f, 0f, 0f), new Vector3(0.2f, 0.8f, 0f), new Vector3(0f, 1.6f, 0.2f),
                new Vector3(50f, 0f, 50f), new Vector3(50.2f, 0.8f, 50f), new Vector3(50f, 1.6f, 50.2f)
            };
            Equal(2, PileDetection.Find(pair, link, min, max, extent).Count,
                  "two distant stacks are two cairns");

            // Single-linkage transitivity: a chain of stones each within the link radius is
            // ONE cluster, so a long low chain is measured — and rejected — as one wall.
            var chain = new List<Vector3>();
            for (int i = 0; i < 6; i++) chain.Add(new Vector3(i * 2f, 0f, 0f));
            Equal(0, PileDetection.Find(chain, link, min, max, extent).Count,
                  "a chain links into one oversized cluster rather than several small ones");

            Equal(0, PileDetection.Find(null, link, min, max, extent).Count, "null input is not a crash");
            Equal(0, PileDetection.Find(new List<Vector3>(), link, min, max, extent).Count, "no stones, no cairns");
            Equal(0, PileDetection.Find(stack, 0f, min, max, extent).Count, "a zero link radius finds nothing");

            // --- pairing a pile with the sign that names it ---------------------------------
            var top = new Vector3(10f, 5f, 10f);
            var signs = new List<Vector3> { new Vector3(40f, 5f, 40f), new Vector3(12f, 1f, 10f) };
            Equal(1, PileDetection.NearestSign(top, signs, 6f), "the nearer sign names the cairn");

            // XZ-planar on purpose: a sign set into the side of a cairn sits well below its
            // top, and a 3D check would push it out of reach for no visible reason.
            Equal(1, PileDetection.NearestSign(top, signs, 3f),
                  "a sign four metres below still names the cairn it is set into");

            Equal(-1, PileDetection.NearestSign(top, new List<Vector3> { new Vector3(40f, 5f, 40f) }, 6f),
                  "a distant sign does not name it — an unnamed cairn is still a cairn");
            Equal(-1, PileDetection.NearestSign(top, null, 6f), "no signs at all is not a crash");

            // --- a drifting cairn keeps its identity ----------------------------------------
            // Live defect, 2026-09-02: an unnamed cairn is keyed on its own crown, a crown is a
            // computed centroid, and adding a stone moved it 0.8m — across a metre boundary,
            // so the landmark was pruned and re-founded with its history erased.
            var was = new LandmarkKey(52, 42, 18);
            var candidates = new List<LandmarkKey> { new LandmarkKey(51, 42, 19), new LandmarkKey(200, 42, 200) };
            Equal(0, PileDetection.NearestKey(was, candidates, 4f), "a cairn that drifted a metre is the same cairn");
            Equal(-1, PileDetection.NearestKey(was, new List<LandmarkKey> { new LandmarkKey(200, 42, 200) }, 4f),
                  "a cairn across the map is a different cairn");
            Equal(-1, PileDetection.NearestKey(was, candidates, 0f), "zero drift inherits nothing");
            Equal(-1, PileDetection.NearestKey(was, null, 4f), "no candidates is not a crash");

            // Height is excluded on purpose: a cairn grows upward as stones are added, so Y is
            // the axis most likely to move for the least meaningful reason.
            Equal(0, PileDetection.NearestKey(was, new List<LandmarkKey> { new LandmarkKey(52, 99, 18) }, 4f),
                  "a cairn that grew taller is still the same cairn");
        }

        // ---- store ----------------------------------------------------------------------

        private static void StoreTests()
        {
            Section("LandmarkStore");

            LandmarkStore.Clear();
            Equal(0, LandmarkStore.Count, "starts empty");
            Check(!LandmarkStore.IsDirty, "an empty store is clean");

            var key = new LandmarkKey(5, 6, 7);
            Check(LandmarkStore.Upsert(key, "Two Rocks", "host", 1000L), "first sighting is a change");
            Check(LandmarkStore.IsDirty, "a new landmark marks the store dirty");
            Equal(1, LandmarkStore.Count, "one landmark stored");

            LandmarkStore.MarkClean();
            Check(!LandmarkStore.Upsert(key, "Two Rocks", "host", 2000L),
                  "an unchanged sighting is not a change");
            Check(!LandmarkStore.IsDirty,
                  "re-seeing an unchanged landmark does not dirty the store");

            LandmarkStore.TryGet(key, out Landmark seen);
            Equal(1000L, seen.FirstSeenUtcTicks, "firstSeen is never overwritten by a later sighting");
            Equal(2000L, seen.LastSeenUtcTicks, "lastSeen advances");

            Check(LandmarkStore.Upsert(key, "Three Rocks", "host", 3000L), "a rename is a change");
            Check(LandmarkStore.IsDirty, "a rename dirties the store");
            LandmarkStore.TryGet(key, out seen);
            Equal("Three Rocks", seen.Name, "the new name is stored");
            Equal(1000L, seen.FirstSeenUtcTicks, "a rename still preserves firstSeen");

            Check(LandmarkStore.Remove(key), "remove reports the removal");
            Check(!LandmarkStore.Remove(key), "removing what is gone reports nothing");
            Equal(0, LandmarkStore.Count, "store is empty again");

            // --- carrying history across a drift ---------------------------------------------
            LandmarkStore.Clear();
            var moved = new LandmarkKey(51, 42, 19);
            LandmarkStore.Upsert(moved, "", SignReading.UnknownAuthor, true, new Vector3(51.5f, 42f, 19.3f), 5000L);
            LandmarkStore.MarkClean();

            Check(LandmarkStore.CarryHistory(moved, 1000L), "an older history is carried across");
            LandmarkStore.TryGet(moved, out Landmark inherited);
            Equal(1000L, inherited.FirstSeenUtcTicks, "the cairn is as old as the one it replaced");
            Equal(5000L, inherited.LastSeenUtcTicks, "but was last seen just now");
            Check(LandmarkStore.IsDirty, "carrying history dirties the store");

            Check(!LandmarkStore.CarryHistory(moved, 9000L),
                  "a LATER history is refused — a landmark cannot be made younger");
            LandmarkStore.TryGet(moved, out inherited);
            Equal(1000L, inherited.FirstSeenUtcTicks, "and the older date survives the attempt");

            Check(!LandmarkStore.CarryHistory(new LandmarkKey(9, 9, 9), 1L), "carrying to nothing is refused");
            Check(!LandmarkStore.CarryHistory(moved, 0L), "a zero history is refused");

            LandmarkStore.Clear();
            LandmarkStore.Upsert(new LandmarkKey(1, 1, 1), "A", "host", 1L);
            List<Landmark> snap = LandmarkStore.Snapshot();
            LandmarkStore.Upsert(new LandmarkKey(2, 2, 2), "B", "host", 1L);
            Equal(1, snap.Count, "a snapshot is a copy, not a live view");
        }

        // ---- persistence ----------------------------------------------------------------

        private static void PersistenceTests()
        {
            Section("Persistence");

            string dir = Path.Combine(_tempRoot, "world");
            Directory.CreateDirectory(dir);
            Persistence.OverrideDirectory = dir;
            Persistence.OverrideWorldUid = 4242UL;

            string path = Path.Combine(dir, "cairn_landmarks_4242.dat");

            // --- fresh world -------------------------------------------------------------
            Persistence.ResetForTests();
            Persistence.Load();
            Check(Persistence.IsLoaded, "a fresh world still counts as loaded");
            Equal(0, LandmarkStore.Count, "a fresh world has no landmarks");

            Persistence.Save();
            Check(!File.Exists(path), "a clean store writes nothing");

            // --- round trip through the SHIPPING writer ----------------------------------
            LandmarkStore.Upsert(new LandmarkKey(10, 20, 30), "Two Rocks", "host", 111L);
            LandmarkStore.Upsert(new LandmarkKey(-1, -2, -3), "Gull\tCliff", "76561198000000000", 222L);
            Persistence.Save();
            Check(File.Exists(path), "a dirty store writes a file");
            Check(!File.Exists(path + ".tmp"), "no .tmp is left orphaned");

            // Assert on the bytes the mod ACTUALLY wrote. Hand-built fixtures emit no BOM
            // while Encoding.UTF8 does; that mismatch once let a suite agree with itself and
            // disagree with disk for as long as it existed.
            byte[] raw = File.ReadAllBytes(path);
            Check(!(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF),
                  "the shipping writer emits no BOM");
            string text = Encoding.UTF8.GetString(raw);
            // Pinned to the current version deliberately, so a format bump has to be a
            // conscious edit here rather than something a loose assertion waves through.
            Check(text.StartsWith("version\t2\n", StringComparison.Ordinal),
                  "the file opens with a v2 version header");
            Check(text.IndexOf("Gull\\tCliff", StringComparison.Ordinal) >= 0,
                  "a tab inside a name is escaped on disk, not written raw");

            Persistence.ResetForTests();
            Persistence.Load();
            Equal(2, LandmarkStore.Count, "both landmarks come back");
            Check(LandmarkStore.TryGet(new LandmarkKey(-1, -2, -3), out Landmark gull), "the second is found by key");
            Equal("Gull\tCliff", gull.Name, "the tabbed name is restored exactly");
            Equal("76561198000000000", gull.Author, "the author is restored");
            Equal(222L, gull.FirstSeenUtcTicks, "firstSeen survives a save and load");
            Check(!LandmarkStore.IsDirty, "a freshly loaded store is clean");

            // --- an UNNAMED lit pile must survive a save ----------------------------------
            // The store's sparseness rule drops landmarks that are "nothing"; a pile with no
            // sign is not nothing, it is a lit waymark. Before the pile existed the rule was
            // "drop unless named", and that rule would silently delete every unnamed cairn on
            // the first autosave.
            LandmarkStore.Upsert(new LandmarkKey(70, 8, 70), "", SignReading.UnknownAuthor,
                                 true, new Vector3(70.5f, 10.25f, 70.5f), 999L);
            Persistence.Save();
            Persistence.ResetForTests();
            Persistence.Load();
            Check(LandmarkStore.TryGet(new LandmarkKey(70, 8, 70), out Landmark waymark),
                  "an unnamed lit pile is still on disk after a save and load");
            Check(waymark != null && waymark.HasPile, "and it comes back lit");
            Check(waymark != null && Math.Abs(waymark.Light.y - 10.25f) < 0.0001f,
                  "with its light where it was left");
            LandmarkStore.Remove(new LandmarkKey(70, 8, 70));
            Persistence.Save();

            // --- .bak rotation -----------------------------------------------------------
            LandmarkStore.Upsert(new LandmarkKey(10, 20, 30), "Two Rocks Renamed", "host", 333L);
            Persistence.Save();
            Check(File.Exists(path + ".bak"), "the previous file is kept as .bak");
            Check(!File.Exists(path + ".tmp"), "still no orphaned .tmp after a rotation");

            // --- world scoping -----------------------------------------------------------
            Persistence.OverrideWorldUid = 9999UL;
            Persistence.ResetForTests();
            Persistence.Load();
            Equal(0, LandmarkStore.Count, "a different world sees none of the first world's landmarks");
            LandmarkStore.Upsert(new LandmarkKey(1, 1, 1), "Other World", "host", 1L);
            Persistence.Save();
            string otherPath = Path.Combine(dir, "cairn_landmarks_9999.dat");
            Check(File.Exists(otherPath), "the second world writes its own file");
            Check(File.Exists(path), "the first world's file is untouched");

            // --- wholly corrupt: binary garbage ------------------------------------------
            string cdir = Path.Combine(_tempRoot, "corrupt");
            Directory.CreateDirectory(cdir);
            Persistence.OverrideDirectory = cdir;
            Persistence.OverrideWorldUid = 7L;
            string cpath = Path.Combine(cdir, "cairn_landmarks_7.dat");

            File.WriteAllBytes(cpath, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            RavenIron.Cairn.Cairn.Log.Clear();
            Persistence.ResetForTests();
            Persistence.Load();
            Equal(0, LandmarkStore.Count, "a corrupt file loads no landmarks");
            Check(!File.Exists(cpath), "the corrupt file is moved out of the way");
            Check(File.Exists(cpath + ".corrupt"), "it is quarantined as .corrupt");
            Check(RavenIron.Cairn.Cairn.Log.Errors.Count > 0, "corruption is reported at error level");

            // --- header only: the quiet path ---------------------------------------------
            File.WriteAllText(cpath, "version\t1\n# x\ty\tz\tfirstSeenUtcTicks\tlastSeenUtcTicks\tauthor\tname\n");
            RavenIron.Cairn.Cairn.Log.Clear();
            Persistence.ResetForTests();
            Persistence.Load();
            Equal(0, LandmarkStore.Count, "a header-only file has no landmarks");
            Check(File.Exists(cpath), "a header-only file is NOT quarantined — nothing failed");
            Equal(0, RavenIron.Cairn.Cairn.Log.Errors.Count, "a header-only file logs no error");

            // --- partially corrupt: per-line isolation ------------------------------------
            File.WriteAllText(cpath,
                "version\t1\n" +
                "5\t6\t7\t10\t20\thost\tGood One\n" +
                "this line is not a landmark\n");
            RavenIron.Cairn.Cairn.Log.Clear();
            Persistence.ResetForTests();
            Persistence.Load();
            Equal(1, LandmarkStore.Count, "the readable line survives a bad neighbour");
            Check(File.Exists(cpath), "a partially readable file is NOT quarantined");
            Check(RavenIron.Cairn.Cairn.Log.Warnings.Count > 0, "the skipped line is reported");

            // --- version header recognised by content, not position ------------------------
            // RW's bug: skipping line 1 unconditionally swallowed the only line a short binary
            // file has, which is exactly what hid its whole-file corruption case.
            File.WriteAllText(cpath,
                "5\t6\t7\t10\t20\thost\tFirst Line Data\n" +
                "version\t1\n");
            Persistence.ResetForTests();
            Persistence.Load();
            Equal(1, LandmarkStore.Count, "a data line at the top is read, and a later header is skipped");

            Persistence.OverrideDirectory = null;
            Persistence.OverrideWorldUid = null;
            Persistence.ResetForTests();
        }
    }
}
