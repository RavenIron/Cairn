using System;
using System.Collections;
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
    /// A navigation channel cannot be built on that. A grace note can. So this adds a line to
    /// the raven's own queue and lets vanilla decide whether the bird ever shows up; when it
    /// does not, nothing is lost and nothing is logged as wrong.
    ///
    /// ADDITIVE MEANS ADDITIVE. Our text is APPENDED to the temp queue and given priority 0,
    /// so vanilla's own tutorials are found first and any static guide point ties against us
    /// and wins (`GetBestText` prefers a static on `>=`). We remove our entry when we leave;
    /// we never clear the list, never touch `m_staticTexts`, and never set a tutorial key —
    /// writing one would mark a tutorial seen on the player's own save forever.
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

        private object _ourText;              // the RavenText we appended, or null
        private LandmarkKey _spokenFor;
        private bool _holding;
        private float _sinceCheck;
        private bool _failed;

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

        private void Offer(LandmarkKey key, string storedName)
        {
            if (!(_tempTextsField.GetValue(null) is IList queue)) return;

            string spoken = DisplayText.ForSpeech(storedName, ModConfig.RavenNameMaxLength.Value);
            if (spoken.Length == 0) return;

            object text = Activator.CreateInstance(_textType);
            SetField(text, "m_text", spoken);
            SetField(text, "m_topic", "");        // no topic line: the name is the whole message
            SetField(text, "m_key", "");          // NEVER a tutorial key — that persists on the save
            SetField(text, "m_munin", false);     // Hugin, not Munin
            SetField(text, "m_priority", 0);      // vanilla wins every tie
            SetField(text, "m_alwaysSpawn", true);

            queue.Add(text);                      // APPENDED: vanilla's entries are found first

            _ourText = text;
            _spokenFor = key;
            _holding = true;

            if (ModConfig.VerboseLogging.Value)
                Cairn.Log.LogInfo($"HuginVoice: offered \"{spoken}\" for {key}.");
        }

        /// <summary>
        /// Take our line back out. Removal is by REFERENCE, so a vanilla entry can never be
        /// removed by accident even if its text happened to match ours.
        /// </summary>
        private void Release()
        {
            if (!_holding) return;
            _holding = false;

            try
            {
                if (_ourText != null && _tempTextsField != null &&
                    _tempTextsField.GetValue(null) is IList queue)
                {
                    for (int i = queue.Count - 1; i >= 0; i--)
                        if (ReferenceEquals(queue[i], _ourText)) queue.RemoveAt(i);
                }
            }
            catch
            {
                // Leaving a stale line in the queue is untidy; throwing here would be worse.
            }

            _ourText = null;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo f = AccessTools.Field(target.GetType(), name);
            f?.SetValue(target, value);
        }

        private void OnDestroy() => Release();
    }
}
