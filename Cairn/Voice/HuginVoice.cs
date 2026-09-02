using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using RavenIron.Cairn.Net;
using UnityEngine;

namespace RavenIron.Cairn.Voice
{
    /// <summary>
    /// Hugin says the name of the place you are standing in.
    ///
    /// FLAVOUR, ADDITIVE, NEVER LOAD-BEARING — the locked decision, and the constraints make
    /// it unavoidable rather than merely polite. Decompiled 2026-09-01, all still true:
    ///
    ///   • `Raven.FindSpawnPoint` demands `height > 30f`, so the bird CANNOT LAND BELOW 30m
    ///     of altitude — it is absent on exactly the coastlines a navigator cares about.
    ///   • It despawns when a hostile is within 10m (`LootSpawner.IsMonsterInRange`).
    ///   • It despawns during `RandEventSystem.InEvent()` — so a Ragnarok's Wrath storm
    ///     silences it at the moment you would most want a landmark named.
    ///
    /// A navigation channel cannot be built on that. A grace note can. So this offers a line
    /// and lets vanilla decide whether the bird ever shows up; when it does not, nothing is
    /// lost and nothing is logged as wrong.
    ///
    /// ADDITIVE, BUT NOT AT THE BACK OF A QUEUE. The first version appended a temp text so
    /// vanilla came first; measured live, the queue held five entries, four of them untriggered
    /// tutorials, and GetTempText returns the FIRST match and stops. A line at the back is a
    /// line never read. We plant a GUIDE POINT instead — vanilla's own way of marking a place —
    /// which competes on proximity and wins a priority tie without displacing anything. Its
    /// OnDestroy unregisters it, so we never touch the static list ourselves, and we never set
    /// a tutorial key: writing one marks a tutorial seen on the player's own save forever.
    ///
    /// Every member is reached by REFLECTION. `Raven.m_tempTexts` reads public in the
    /// publicized assembly and so did `Terminal.commands`, which was private at runtime and
    /// killed a server mid-boot; losing that way inside a client's frame loop would be worse.
    /// </summary>
    public class HuginVoice : MonoBehaviour
    {
        private Type _ravenType;
        private Type _textType;
        private FieldInfo _tempTextsField;
        private FieldInfo _instanceField;

        private GameObject _guidePost;        // the guide point we planted, or null
        private object _ourText;              // the RavenText it carries
        private LandmarkKey _spokenFor;
        private bool _holding;
        private float _sinceCheck;
        private bool _failed;
        private bool _warnedNoBird;

        /// <summary>The live voice, so the console can ask why it is quiet.</summary>
        public static HuginVoice Instance { get; private set; }

        private void Awake() => Instance = this;

        /// <summary>Is Hugin actually in this scene? Null-safe against Unity's fake null.</summary>
        public bool RavenExists()
        {
            if (_instanceField == null) return false;
            return _instanceField.GetValue(null) is UnityEngine.Object o && o != null;
        }

        /// <summary>
        /// Why is the bird quiet? Four different reasons look identical from where a player
        /// stands: no raven in the scene at all, nothing named within reach, the line queued
        /// but vanilla declining to land it, or reflection never resolving.
        /// </summary>
        public List<string> Describe()
        {
            var lines = new List<string>();

            if (_failed) { lines.Add("  voice DISABLED after an error — see the log"); return lines; }
            if (!ModConfig.EnableRavenVoice.Value) { lines.Add("  disabled by config"); return lines; }

            bool resolved = Resolve();
            lines.Add($"  reflection : {(resolved ? "resolved" : "NOT resolved — Valheim's API moved")}");
            lines.Add($"  raven in scene : {RavenExists()}");

            if (!RavenExists())
                lines.Add("    ^ vanilla spawns Hugin from a GuidePoint (the start temple has one). " +
                          "Until one loads, there is no bird to carry a line and the voice is " +
                          "silent BY DESIGN.");

            int queued = -1;
            try
            {
                if (resolved && _tempTextsField.GetValue(null) is IList q) queued = q.Count;
            }
            catch { }
            lines.Add($"  raven temp queue : {(queued < 0 ? "unreadable" : queued.ToString())} vanilla entry(s) " +
                      "— we no longer queue there, see Offer");
            lines.Add($"  our guide point : {(_holding ? "planted" : "none")}");

            // "Planted" only means WE made a GameObject. Everything below asks vanilla
            // whether it accepted it — registration, selection, and the bird's own state.
            // Three round trips were spent on the gap between those two claims.
            object raven = RavenInstance();
            if (raven != null)
            {
                object isMunin = AccessTools.Field(_ravenType, "m_isMunin")?.GetValue(raven);
                lines.Add($"  this raven is : {(isMunin is bool m && m ? "MUNIN" : "Hugin")}" +
                          " (our text must match, or it is filtered out before anything else)");

                lines.Add($"  registered : {(OurTextRegistered() ? "yes — vanilla holds our static text" : "NO — RegisterStaticText never took")}");

                object best = Invoke(raven, "GetBestText");
                string verdict =
                    best == null ? "nothing — the bird has no reason to come" :
                    ReferenceEquals(best, _ourText) ? "OURS — vanilla has chosen our line" :
                                                      "a VANILLA text, not ours";
                lines.Add($"  GetBestText : {verdict}");

                object away = Invoke(raven, "IsAway");
                lines.Add($"  bird state : {(away is bool a && a ? "away (free to arrive)" : "already perched somewhere")}");
            }

            Player local = Player.m_localPlayer;
            if (local == null)
            {
                lines.Add("  nearest named : no local player");
                return lines;
            }

            // Reported at ANY range, with the distance. "None within 12m" is a dead end: it
            // says you are in the wrong place without saying where the right one is, and that
            // answer cost two live round trips before it was worth fixing.
            Vector3 here = local.transform.position;
            float reach = ModConfig.RavenNameMeters.Value;

            if (NearestNamed(here, float.MaxValue, out LandmarkKey k, out string n))
            {
                float dx = k.X - here.x, dz = k.Z - here.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);

                lines.Add($"  nearest named : \"{n}\" at {k} — {dist:F0}m away" +
                          (dist <= reach
                              ? $", inside the {reach:F0}m reach"
                              : $" — WALK {(dist - reach):F0}m CLOSER to hear it"));
            }
            else
            {
                lines.Add("  nearest named : none anywhere in this world. Unnamed cairns never " +
                          "trigger the voice — put a sign within 6m of one and name it.");
            }

            lines.Add($"  your altitude : {here.y:F0}m " +
                      (here.y > 30f ? "(above the raven's 30m floor)" : "(BELOW the 30m floor — it cannot land)"));

            return lines;
        }

        private void Update()
        {
            if (_failed || !ModConfig.EnableRavenVoice.Value) { Release(); return; }

            _sinceCheck += Time.deltaTime;
            if (_sinceCheck < 1f) return;     // the bird is not in a hurry
            _sinceCheck = 0f;

            try
            {
                Player local = Player.m_localPlayer;
                if (local == null) { Release(); return; }

                if (!Resolve()) { Release(); return; }

                Vector3 here = local.transform.position;
                float reach = ModConfig.RavenNameMeters.Value;

                if (!NearestNamed(here, reach, out LandmarkKey key, out string name))
                {
                    Release();
                    return;
                }

                if (_holding && key == _spokenFor) return;   // already queued for this place

                // Say it once, the first time we have something to say and there is nobody
                // to say it. Silence here is the DESIGNED behaviour, but silence that never
                // explains itself is indistinguishable from a broken feature.
                if (!RavenExists() && !_warnedNoBird)
                {
                    _warnedNoBird = true;
                    Cairn.Log.LogInfo(
                        "HuginVoice: a named landmark is in reach, but there is no Raven in this " +
                        "scene to carry the line. Vanilla spawns Hugin from a GuidePoint — the " +
                        "start temple has one — so this world has simply never loaded one. The " +
                        "voice stays silent by design; `cairn raven` reports this too.");
                }

                Release();
                Offer(key, name);
            }
            catch (Exception ex)
            {
                _failed = true;
                Release();
                Cairn.Log.LogWarning($"HuginVoice disabled after an error: {ex.Message}");
            }
        }

        /// <summary>
        /// The nearest NAMED landmark within reach, XZ-planar. Unnamed cairns are skipped —
        /// the bird has nothing to say about a place nobody has called anything.
        /// </summary>
        private static bool NearestNamed(Vector3 here, float reach, out LandmarkKey key, out string name)
        {
            key = default;
            name = null;

            float bestSq = reach * reach;
            bool found = false;

            foreach (LandmarkSync.Beacon b in LandmarkSync.Current())
            {
                if (string.IsNullOrEmpty(b.Name)) continue;

                float dx = b.Key.X - here.x;
                float dz = b.Key.Z - here.z;
                float sq = dx * dx + dz * dz;

                if (sq > bestSq) continue;

                bestSq = sq;
                key = b.Key;
                name = b.Name;
                found = true;
            }

            return found;
        }

        // ---- the raven's own queue ----------------------------------------------------------

        private bool Resolve()
        {
            // Never latched: the Raven does not exist until some guide point spawns it, which
            // may be long after we start looking.
            if (_ravenType == null) _ravenType = AccessTools.TypeByName("Raven");
            if (_ravenType == null) return false;

            if (_textType == null) _textType = AccessTools.Inner(_ravenType, "RavenText");
            if (_tempTextsField == null) _tempTextsField = AccessTools.Field(_ravenType, "m_tempTexts");
            if (_instanceField == null) _instanceField = AccessTools.Field(_ravenType, "m_instance");

            return _textType != null && _tempTextsField != null;
        }

        /// <summary>
        /// Offer the line as a GUIDE POINT, which is how vanilla marks a PLACE rather than a
        /// tutorial — and the only route that can actually be chosen.
        ///
        /// The first version appended a temp text, deliberately last so vanilla's own entries
        /// came first. Measured live on 2026-09-02: the queue held FIVE entries, four of them
        /// vanilla tutorials the player had never triggered, and `GetTempText` returns the
        /// FIRST match and stops. Untriggered tutorials never drain, so a line at the back is
        /// a line that is never read. Being maximally deferential made the feature permanently
        /// silent — the politeness was the bug.
        ///
        /// A guide point competes properly instead of queueing: `GetClosestStaticText` finds
        /// the nearest one within 15m, and `GetBestText` prefers a static over a temp text on
        /// `>=` priority, so at priority 0 we win a tie against a tutorial without displacing
        /// it. Vanilla's queue is left completely untouched.
        ///
        /// Guarded on a raven existing, because `GuidePoint.Start` instantiates the raven
        /// prefab when none does — and that field is null on a component we created.
        /// </summary>
        private void Offer(LandmarkKey key, string storedName)
        {
            if (!RavenExists()) return;

            Type guideType = AccessTools.TypeByName("GuidePoint");
            if (guideType == null) return;

            string spoken = DisplayText.ForSpeech(storedName, ModConfig.RavenNameMaxLength.Value);
            if (spoken.Length == 0) return;

            // Created INACTIVE so Start cannot run before the text is in place; a guide point
            // that registers an empty RavenText would put a blank line in the raven's mouth.
            var post = new GameObject("cairn_guidepoint");
            post.SetActive(false);
            post.transform.position = new Vector3(key.X, key.Y, key.Z);

            Component guide = post.AddComponent(guideType);

            object text = Activator.CreateInstance(_textType);
            SetField(text, "m_text", spoken);
            SetField(text, "m_topic", "");        // the name is the whole message
            SetField(text, "m_key", "");          // NEVER a tutorial key — that persists on the save
            // MATCH the live bird. GetClosestStaticText skips any text whose m_munin differs
            // from the raven's own m_isMunin, so hardcoding Hugin means silence in any world
            // where the instance happens to be Munin - filtered out before distance, priority
            // or anything else is even considered.
            SetField(text, "m_munin", RavenIsMunin());
            SetField(text, "m_priority", 0);      // ties, and a tie is enough for a static
            SetField(text, "m_alwaysSpawn", true);
            SetField(guide, "m_text", text);

            post.SetActive(true);                 // Start registers it with the raven

            _guidePost = post;
            _ourText = text;
            _spokenFor = key;
            _holding = true;

            if (ModConfig.VerboseLogging.Value)
                Cairn.Log.LogInfo($"HuginVoice: guide point for \"{spoken}\" at {key}.");
        }

        /// <summary>
        /// Take the guide point away again. Destroying it runs `GuidePoint.OnDestroy`, which
        /// calls `Raven.UnregisterStaticText` — vanilla removes its own registration, so we
        /// never touch the static list ourselves and cannot drop somebody else's entry.
        /// </summary>
        private void Release()
        {
            if (!_holding) return;
            _holding = false;

            try
            {
                if (_guidePost != null) Destroy(_guidePost);
            }
            catch
            {
                // A stale guide point is untidy; throwing here would be worse.
            }

            _guidePost = null;
            _ourText = null;
        }

        /// <summary>The live Raven, or null. Unity fake-null aware.</summary>
        private object RavenInstance()
        {
            if (_instanceField == null) return null;
            object v = _instanceField.GetValue(null);
            if (v is UnityEngine.Object o && o == null) return null;
            return v;
        }

        /// <summary>Did vanilla actually take our static text, or did we only think so?</summary>
        private bool OurTextRegistered()
        {
            try
            {
                if (_ourText == null) return false;
                if (!(AccessTools.Field(_ravenType, "m_staticTexts")?.GetValue(null) is IList statics))
                    return false;

                foreach (object t in statics)
                    if (ReferenceEquals(t, _ourText)) return true;

                return false;
            }
            catch { return false; }
        }

        /// <summary>Is this raven Munin? Our text must carry the same flag or it is skipped.</summary>
        private bool RavenIsMunin()
        {
            object raven = RavenInstance();
            if (raven == null) return false;
            return AccessTools.Field(_ravenType, "m_isMunin")?.GetValue(raven) is bool m && m;
        }

        private object Invoke(object target, string method)
        {
            try
            {
                MethodInfo mi = AccessTools.Method(target.GetType(), method, Type.EmptyTypes);
                return mi?.Invoke(target, null);
            }
            catch { return null; }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo f = AccessTools.Field(target.GetType(), name);
            f?.SetValue(target, value);
        }

        private void OnDestroy() => Release();
    }
}
