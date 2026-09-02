using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace RavenIron.CairnProbe
{
    /// <summary>
    /// Research instrument, not a feature. It answers one question: how far can a
    /// client-drawn beacon column be seen in Valheim, and what erases it first.
    ///
    /// Four numbers cannot be read out of the assembly because they are serialized in
    /// prefabs and the scene rather than written in code:
    ///   RenderSettings.fogDensity / fogMode   - blended per frame from the current
    ///                                           environment's four day-phase values
    ///   Camera.main.farClipPlane              - GameCamera only touches the NEAR plane
    ///   ZoneSystem.m_activeArea / m_activeDistantArea
    ///                                         - code initializers read 1 and 1, but the
    ///                                           prefab overrides them; do not trust the
    ///                                           decompile here
    ///
    /// Run it for one in-game day and read the CSV. Then close the client and delete the
    /// DLL: it writes nothing to the world and patches nothing.
    ///
    /// House rules honoured on purpose: no coroutine (an Update-driven accumulator),
    /// every game read in a try/catch that cannot take the frame down with it, and
    /// InvariantCulture on everything that reaches disk.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class CairnProbePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.raveniron.cairnprobe";
        public const string PluginName = "Cairn Probe";
        public const string PluginVersion = "0.1.0";

        private const string Tag = "[cairn]";

        private ConfigEntry<float> _sampleSeconds;
        private ConfigEntry<float> _heartbeatSeconds;
        private ConfigEntry<float> _densityChangeFraction;
        private ConfigEntry<string> _probeDistances;
        private ConfigEntry<float> _contrastThreshold;
        private ConfigEntry<float> _beaconHeight;
        private ConfigEntry<bool> _writeCsv;

        private float[] _distances = { 100f, 200f, 400f, 800f };
        private float _sinceSample;
        private float _sinceHeartbeat;
        private bool _armed;
        private bool _firstRow = true;

        private StreamWriter _csv;
        private string _csvPath;

        private int _failures;
        private bool _failureLogged;

        // Change detection.
        private string _lastEnv;
        private FogMode _lastMode;
        private bool _lastFogOn;
        private float _lastDensity = float.NaN;
        private string _lastCameraNote;

        private void Awake()
        {
            _sampleSeconds = Config.Bind("probe", "SampleSeconds", 2f,
                "How often to look. Cheap: a few Unity property reads and a little reflection.");
            _heartbeatSeconds = Config.Bind("probe", "HeartbeatSeconds", 300f,
                "Emit a row even when nothing changed, so a silent probe is distinguishable from a stopped one.");
            _densityChangeFraction = Config.Bind("probe", "DensityChangeFraction", 0.05f,
                "Relative change in fog density that counts as an event worth a row.");
            _probeDistances = Config.Bind("probe", "ProbeDistances", "100,200,400,800",
                "Metres to report contrast at. 400 is the beacon question.");
            _contrastThreshold = Config.Bind("probe", "ContrastThreshold", 0.10f,
                "Transmittance defining the practical horizon. 0.10 = a tenth of the contrast survives.");
            _beaconHeight = Config.Bind("probe", "BeaconHeightMeters", 40f,
                "Height of the hypothetical column, for the on-screen pixel figure.");
            _writeCsv = Config.Bind("probe", "WriteCsv", true,
                "Write a CSV beside the BepInEx config for plotting.");

            ParseDistances();

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Logger.LogInfo(Tag + " no renderer on this process - fog and camera do not exist here. " +
                                     "The probe is CLIENT-side; run it on the client, not the server.");
                return;
            }

            if (_writeCsv.Value) OpenCsv();

            _armed = true;
            Logger.LogInfo(Tag + " armed. Sampling every " +
                           _sampleSeconds.Value.ToString("0.#", CultureInfo.InvariantCulture) + "s" +
                           (_csvPath != null ? ", csv -> " + _csvPath : ", csv disabled") + ".");
        }

        private void ParseDistances()
        {
            var parsed = new List<float>();
            foreach (string part in _probeDistances.Value.Split(','))
            {
                if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float d) && d > 0f)
                    parsed.Add(d);
            }
            if (parsed.Count > 0) _distances = parsed.ToArray();
        }

        private void OpenCsv()
        {
            try
            {
                _csvPath = Path.Combine(Paths.ConfigPath, "cairn-probe-fog.csv");
                bool fresh = !File.Exists(_csvPath);

                _csv = new StreamWriter(new FileStream(_csvPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                                        new UTF8Encoding(false));

                if (fresh)
                {
                    var head = new StringBuilder(
                        "utc,trigger,day,dayFraction,env,fogOn,fogMode,density,fogStart,fogEnd,farClip,fov,activeArea,distantArea,nearRadiusM,horizonM");
                    foreach (float d in _distances) head.Append(",t@").Append(d.ToString("0", CultureInfo.InvariantCulture));
                    foreach (float d in _distances) head.Append(",px@").Append(d.ToString("0", CultureInfo.InvariantCulture));
                    _csv.WriteLine(head.ToString());
                    _csv.Flush();
                }
            }
            catch (Exception ex)
            {
                _csv = null;
                _csvPath = null;
                Logger.LogWarning(Tag + " could not open the csv (" + ex.GetType().Name + "). Log-only from here.");
            }
        }

        private void Update()
        {
            if (!_armed) return;

            _sinceSample += Time.deltaTime;
            _sinceHeartbeat += Time.deltaTime;
            if (_sinceSample < _sampleSeconds.Value) return;
            _sinceSample = 0f;

            try
            {
                Sample();
            }
            catch (Exception ex)
            {
                _failures++;
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    Logger.LogWarning(Tag + " sample threw (" + ex.GetType().Name + ": " + ex.Message +
                                      "). Further failures are counted, not logged.");
                }
            }
        }

        private void Sample()
        {
            bool fogOn = RenderSettings.fog;
            FogMode mode = RenderSettings.fogMode;
            float density = RenderSettings.fogDensity;
            float fogStart = RenderSettings.fogStartDistance;
            float fogEnd = RenderSettings.fogEndDistance;

            Camera cam = Camera.main;
            float farClip = cam != null ? cam.farClipPlane : float.NaN;
            float fov = cam != null ? cam.fieldOfView : float.NaN;
            string cameraNote = DescribeLayerCulling(cam);

            // The world may not be loaded yet. Blanks are honest, so nothing gates on them.
            object envMan = Reflect.Singleton(Reflect.EnvManType);
            string env = "?";
            if (Reflect.TryCall(envMan, "GetCurrentEnvironment", out object envSetup) &&
                Reflect.TryField(envSetup, "m_name", out string envName) && !string.IsNullOrEmpty(envName))
                env = envName;

            float dayFraction = Reflect.TryCall(envMan, "GetDayFraction", out float df) ? df : float.NaN;
            int day = Reflect.TryCall(envMan, "GetCurrentDay", out int d) ? d : -1;

            object zoneSystem = Reflect.Singleton(Reflect.ZoneSystemType);
            int activeArea = Reflect.TryField(zoneSystem, "m_activeArea", out int aa) ? aa : -1;
            int distantArea = Reflect.TryField(zoneSystem, "m_activeDistantArea", out int da) ? da : -1;
            float nearRadius = activeArea >= 0 ? activeArea * 64f : float.NaN;

            string trigger = Trigger(env, mode, fogOn, density, cameraNote);
            if (trigger == null) return;

            float horizon = fogOn
                ? FogMath.Horizon(mode, density, fogStart, fogEnd, _contrastThreshold.Value)
                : float.PositiveInfinity;

            var t = new float[_distances.Length];
            var px = new float[_distances.Length];
            for (int i = 0; i < _distances.Length; i++)
            {
                t[i] = fogOn ? FogMath.Transmittance(mode, density, fogStart, fogEnd, _distances[i]) : 1f;
                px[i] = FogMath.PixelsTall(_beaconHeight.Value, _distances[i],
                                           float.IsNaN(fov) ? 65f : fov, Screen.height);
            }

            EmitLog(trigger, env, day, dayFraction, fogOn, mode, density, horizon, farClip, fov,
                    activeArea, distantArea, nearRadius, t, px, cameraNote);
            EmitCsv(trigger, env, day, dayFraction, fogOn, mode, density, fogStart, fogEnd, farClip, fov,
                    activeArea, distantArea, nearRadius, horizon, t, px);

            _lastEnv = env;
            _lastMode = mode;
            _lastFogOn = fogOn;
            _lastDensity = density;
            _lastCameraNote = cameraNote;
            _firstRow = false;
            _sinceHeartbeat = 0f;
        }

        private string Trigger(string env, FogMode mode, bool fogOn, float density, string cameraNote)
        {
            if (_firstRow) return "first";
            if (env != _lastEnv) return "env";
            if (mode != _lastMode || fogOn != _lastFogOn) return "mode";
            if (cameraNote != _lastCameraNote) return "camera";

            if (!float.IsNaN(_lastDensity))
            {
                float delta = Mathf.Abs(density - _lastDensity);
                float floor = Mathf.Max(Mathf.Abs(_lastDensity) * _densityChangeFraction.Value, 1e-7f);
                if (delta > floor) return "density";
            }

            if (_sinceHeartbeat >= _heartbeatSeconds.Value) return "heartbeat";
            return null;
        }

        /// <summary>
        /// Per-layer cull distances are the one culling mechanism that could quietly kill a
        /// distant effect. Zero means "use the far plane", so only non-zero entries matter.
        /// </summary>
        private static string DescribeLayerCulling(Camera cam)
        {
            if (cam == null) return "no-camera";

            float[] distances;
            try { distances = cam.layerCullDistances; }
            catch { return "unreadable"; }

            if (distances == null) return "none";

            var sb = new StringBuilder();
            for (int i = 0; i < distances.Length; i++)
            {
                if (distances[i] <= 0f) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(LayerMask.LayerToName(i)).Append(':')
                  .Append(distances[i].ToString("0", CultureInfo.InvariantCulture));
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        private void EmitLog(string trigger, string env, int day, float dayFraction, bool fogOn, FogMode mode,
                             float density, float horizon, float farClip, float fov,
                             int activeArea, int distantArea, float nearRadius,
                             float[] t, float[] px, string cameraNote)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(Tag);
            sb.Append(' ').Append(trigger).Append(" | env=").Append(env);
            if (day >= 0) sb.Append(" day=").Append(day.ToString(c));
            if (!float.IsNaN(dayFraction)) sb.Append(" t=").Append(dayFraction.ToString("0.00", c));

            sb.Append(" | fog=").Append(fogOn ? mode.ToString() : "OFF")
              .Append(" density=").Append(density.ToString("0.######", c));

            sb.Append(" | contrast");
            for (int i = 0; i < _distances.Length; i++)
            {
                sb.Append(' ').Append(_distances[i].ToString("0", c)).Append("m=")
                  .Append((t[i] * 100f).ToString("0", c)).Append('%');
            }

            sb.Append(" | ").Append(_contrastThreshold.Value.ToString("0.##", c)).Append(" horizon=")
              .Append(float.IsInfinity(horizon) ? "inf" : horizon.ToString("0", c) + "m");

            sb.Append(" | ").Append(_beaconHeight.Value.ToString("0", c)).Append("m column");
            for (int i = 0; i < _distances.Length; i++)
            {
                sb.Append(' ').Append(_distances[i].ToString("0", c)).Append("m=")
                  .Append(px[i].ToString("0", c)).Append("px");
            }

            sb.Append(" | farClip=").Append(float.IsNaN(farClip) ? "?" : farClip.ToString("0", c))
              .Append(" fov=").Append(float.IsNaN(fov) ? "?" : fov.ToString("0.#", c))
              .Append(" layerCull=").Append(cameraNote);

            sb.Append(" | zones near=").Append(activeArea.ToString(c))
              .Append(" distant=").Append(distantArea.ToString(c));
            if (!float.IsNaN(nearRadius))
                sb.Append(" (~").Append(nearRadius.ToString("0", c)).Append("m of loaded objects)");

            Logger.LogInfo(sb.ToString());
        }

        private void EmitCsv(string trigger, string env, int day, float dayFraction, bool fogOn, FogMode mode,
                             float density, float fogStart, float fogEnd, float farClip, float fov,
                             int activeArea, int distantArea, float nearRadius, float horizon,
                             float[] t, float[] px)
        {
            if (_csv == null) return;

            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append(DateTime.UtcNow.ToString("o", c)).Append(',')
              .Append(trigger).Append(',')
              .Append(day.ToString(c)).Append(',')
              .Append(dayFraction.ToString("0.####", c)).Append(',')
              .Append(env.Replace(',', ' ')).Append(',')
              .Append(fogOn ? "1" : "0").Append(',')
              .Append(mode.ToString()).Append(',')
              .Append(density.ToString("0.########", c)).Append(',')
              .Append(fogStart.ToString("0.##", c)).Append(',')
              .Append(fogEnd.ToString("0.##", c)).Append(',')
              .Append(farClip.ToString("0.##", c)).Append(',')
              .Append(fov.ToString("0.##", c)).Append(',')
              .Append(activeArea.ToString(c)).Append(',')
              .Append(distantArea.ToString(c)).Append(',')
              .Append(nearRadius.ToString("0.##", c)).Append(',')
              .Append(float.IsInfinity(horizon) ? "" : horizon.ToString("0.##", c));

            foreach (float v in t) sb.Append(',').Append(v.ToString("0.#####", c));
            foreach (float v in px) sb.Append(',').Append(v.ToString("0.#", c));

            try
            {
                _csv.WriteLine(sb.ToString());
                _csv.Flush();   // a crash mid-session must not cost the day's samples
            }
            catch
            {
                // Disk trouble is not worth a frame. Stop writing, keep logging.
                _csv = null;
            }
        }

        private void OnDestroy()
        {
            if (_failures > 0)
                Logger.LogInfo(Tag + " " + _failures.ToString(CultureInfo.InvariantCulture) +
                               " sample failure(s) this session.");

            try
            {
                _csv?.Flush();
                _csv?.Dispose();
            }
            catch
            {
                // Shutting down; nothing left to salvage.
            }

            _csv = null;
        }
    }
}
