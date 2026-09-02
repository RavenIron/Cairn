using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// The landmark store on disk. Deliberately the same shape as Ragnarok's Wrath's
    /// Persistence, down to the failure modes, because every one of them was paid for once
    /// already:
    ///
    /// 1. WORLD-SCOPED. One file per world uid, so two worlds in the same directory keep
    ///    two ledgers.
    /// 2. ATOMIC WRITES. Write .tmp, rotate the old file to .bak, then move into place. A
    ///    crash mid-write otherwise corrupts the only copy that exists.
    /// 3. FAIL-SAFE. Never throws at a caller. A missing file is a new world; an unreadable
    ///    one is quarantined to .corrupt so the next autosave cannot overwrite the evidence.
    /// 4. INVARIANT CULTURE, always. A comma-decimal locale otherwise writes files that work
    ///    locally and corrupt on a European server owner's machine.
    /// 5. NO BOM. `Encoding.UTF8` emits one; hand-built test fixtures do not. That mismatch
    ///    once let a whole test suite agree with itself and disagree with disk.
    /// </summary>
    public static class Persistence
    {
        private const int FormatVersion = 2;
        private const string FileStem = "cairn_landmarks";
        private const string VersionTag = "version\t";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static bool _loaded;
        private static bool _warnedNoSavePath;

        /// <summary>Test seam. When set, replaces the resolved world save directory.</summary>
        public static string OverrideDirectory;

        /// <summary>Test seam. When set, replaces the live world's uid.</summary>
        public static ulong? OverrideWorldUid;

        public static bool IsLoaded => _loaded;

        /// <summary>Test seam: forget everything, as though the plugin had just loaded.</summary>
        public static void ResetForTests()
        {
            LandmarkStore.Clear();
            _loaded = false;
            _warnedNoSavePath = false;
        }

        // ---- path resolution ------------------------------------------------------------

        private static World ResolveHostWorld()
        {
            try
            {
                return ZNet.GetWorldIfIsHost();
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"Persistence: could not resolve host world: {ex.Message}");
                return null;
            }
        }

        private static ulong ResolveWorldUid(World world)
        {
            try
            {
                // Rule 5: game internals through reflection, never named directly.
                //
                // m_uid is declared `long` on World and FieldRefAccess is TYPE-EXACT — asking
                // for ulong throws rather than converting. In RW that produced one warning, a
                // uid of 0, and a store that then silently never wrote anything.
                return (ulong)AccessTools.FieldRefAccess<World, long>(world, "m_uid");
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"Persistence: could not resolve world uid: {ex.Message}");
                return 0UL;
            }
        }

        private static string ResolveDirectory()
        {
            if (!string.IsNullOrEmpty(OverrideDirectory)) return OverrideDirectory;

            try
            {
                // Explicitly LOCAL — never Auto, and never the world's own FileSource.
                //
                // Utils.GetSaveDataPath returns "" for Auto and Cloud whenever Steam Cloud is
                // enabled, because a cloud save is addressed by a RELATIVE path through Steam's
                // cloud API. Concatenated, that yields "\worlds\..." — which reads as a broken
                // absolute path and is a correct relative one.
                //
                // Deliberate consequence, inherited from RW: on a cloud-saved world the ledger
                // stays on this machine and does not travel with the save.
                return World.GetWorldSavePath(FileHelpers.FileSource.Local);
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"Persistence: could not resolve save path: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolve the store path, reporting WHICH part failed. The failures look identical
        /// from outside and mean opposite things: no host world is a client that legitimately
        /// does not persist, while a uid of 0 is the host failing to read m_uid — after which
        /// every save is a silent no-op forever.
        /// </summary>
        private static string ResolvePath(out string detail)
        {
            string dir = ResolveDirectory();
            ulong uid = OverrideWorldUid ?? 0UL;

            if (!OverrideWorldUid.HasValue)
            {
                World world = ResolveHostWorld();
                if (world == null)
                {
                    detail = "no host world (client, or world not loaded yet)";
                    return null;
                }

                uid = ResolveWorldUid(world);
            }

            if (string.IsNullOrEmpty(dir))
            {
                detail = "world save directory came back empty";
                return null;
            }

            if (uid == 0UL)
            {
                detail = "world uid resolved as 0 — the m_uid field access failed";
                return null;
            }

            detail = $"uid {uid}, dir {dir}";
            return Path.Combine(dir, $"{FileStem}_{uid}.dat");
        }

        // ---- load -----------------------------------------------------------------------

        /// <summary>
        /// Read the ledger. Never throws; a failure leaves an empty, usable store.
        /// </summary>
        public static void Load()
        {
            if (_loaded) return;

            LandmarkStore.Clear();

            string path = ResolvePath(out string detail);
            if (path == null)
            {
                Cairn.Log.LogInfo($"Persistence: no store path ({detail}). Nothing loaded.");
                _loaded = true;
                return;
            }

            if (!File.Exists(path))
            {
                Cairn.Log.LogInfo($"Persistence: no existing store — fresh world ({detail}).");
                _loaded = true;
                LandmarkStore.MarkClean();
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                int good = 0, bad = 0;

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line[0] == '#') continue;

                    // Recognise the header by CONTENT, not by being line 1. Skipping the first
                    // line unconditionally swallows the only line a short binary file has,
                    // which in RW is precisely what hid a whole-file corruption bug.
                    if (line.StartsWith(VersionTag, StringComparison.Ordinal)) continue;

                    if (Landmark.TryParse(line, out Landmark landmark))
                    {
                        LandmarkStore.Put(landmark);
                        good++;
                    }
                    else
                    {
                        bad++;
                    }
                }

                // `File.ReadAllLines` does not throw on binary garbage — it returns junk
                // strings that each fail per-line parsing. So "the file had content, nothing
                // parsed, and at least one line failed" is itself a corruption signal. A
                // header-only file stays on the quiet path: nothing failed, so nothing is wrong.
                if (good == 0 && bad > 0)
                {
                    Cairn.Log.LogError(
                        $"Persistence: {Path.GetFileName(path)} has content but not one readable " +
                        $"landmark ({bad} unparsable line(s)). Treating it as corrupt and keeping " +
                        "it for inspection.");

                    TryQuarantine(path);
                    LandmarkStore.Clear();
                    _loaded = true;
                    return;
                }

                _loaded = true;
                LandmarkStore.MarkClean();

                if (bad > 0)
                    Cairn.Log.LogWarning(
                        $"Persistence: loaded {good} landmark(s), skipped {bad} unreadable line(s) " +
                        $"from {Path.GetFileName(path)}.");
                else
                    Cairn.Log.LogInfo($"Persistence: loaded {good} landmark(s).");
            }
            catch (Exception ex)
            {
                Cairn.Log.LogError(
                    $"Persistence: could not read {Path.GetFileName(path)} ({ex.Message}). " +
                    "Continuing with no stored landmarks. The file has been kept for inspection.");

                TryQuarantine(path);
                LandmarkStore.Clear();
                _loaded = true;
            }
        }

        private static void TryQuarantine(string path)
        {
            try
            {
                string dead = path + ".corrupt";
                if (File.Exists(dead)) File.Delete(dead);
                File.Move(path, dead);
            }
            catch
            {
                // Nothing useful to do; the load already degraded safely.
            }
        }

        // ---- save -----------------------------------------------------------------------

        /// <summary>
        /// Write the ledger if anything changed. Never throws.
        /// </summary>
        public static void Save(bool force = false)
        {
            if (!_loaded) return;
            if (!LandmarkStore.IsDirty && !force) return;

            string path = ResolvePath(out string detail);
            if (path == null)
            {
                // Load resolved a path or we would not be _loaded, so losing it here is an
                // anomaly rather than the ordinary client case. Warn once: repeating it every
                // autosave would bury the world it happened in.
                if (!_warnedNoSavePath)
                {
                    _warnedNoSavePath = true;
                    Cairn.Log.LogWarning(
                        $"Persistence: cannot save, no world path ({detail}). Landmarks kept in memory.");
                }
                return;
            }

            string tmp = path + ".tmp";
            string bak = path + ".bak";

            try
            {
                List<Landmark> all = LandmarkStore.Snapshot();

                var sb = new StringBuilder(all.Count * 64 + 128);
                sb.Append(VersionTag).Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("# x\ty\tz\tfirstSeenUtcTicks\tlastSeenUtcTicks\tauthor\thasPile\tlightX\tlightY\tlightZ\tname\n");

                foreach (Landmark landmark in all)
                {
                    if (!landmark.IsWorthStoring) continue;   // sparseness, enforced at the boundary
                    sb.Append(landmark.Format()).Append('\n');
                }

                // Vanilla does the same before writing a world: worlds_local need not exist
                // yet on a machine that has only ever used cloud saves.
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                File.WriteAllText(tmp, sb.ToString(), Utf8NoBom);

                // Atomic-ish replace: the old file survives until the new one is fully written.
                if (File.Exists(path))
                {
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
                File.Move(tmp, path);

                LandmarkStore.MarkClean();

                if (Config.ModConfig.VerboseLogging.Value)
                    Cairn.Log.LogInfo($"Persistence: saved {all.Count} landmark(s).");
            }
            catch (Exception ex)
            {
                Cairn.Log.LogError($"Persistence: save failed ({ex.Message}). Landmarks kept in memory.");

                // Never leave a half-written .tmp behind to be mistaken for a real file.
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }
}
