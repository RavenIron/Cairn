using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;

namespace RavenIron.Cairn
{
    /// <summary>
    /// Task 0, the skeleton. It loads, it says what process it is, and it registers the
    /// `cairn` console. Nothing else exists yet, and nothing here pretends otherwise.
    ///
    /// One role-aware DLL, as in Ragnarok's Wrath: a headless server simulates, a pure
    /// client renders, a listen host does both. The three are distinguished at runtime,
    /// never at build time.
    /// </summary>
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public class Cairn : BaseUnityPlugin
    {
        public const string PluginId      = "com.raveniron.cairn";
        public const string PluginName    = "Cairn";
        public const string PluginVersion = "0.1.0";

        public static Cairn Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        /// <summary>
        /// True once ZNet exists and this process owns the landmark ledger. Null before
        /// ZNet.Start — callers must handle "not known yet", which is why this is a method
        /// rather than a bool cached at Awake.
        /// </summary>
        public static bool IsSimulationAuthority()
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return false;
            return znet.IsServer();
        }

        /// <summary>
        /// Headless dedicated server: no local player, no camera, nothing to render a
        /// beacon on.
        ///
        /// NOTE, decompile-verified 2026-09-01: in the CLIENT's assembly_valheim this is a
        /// hardcoded `return false`. That is correct at runtime — the server binary loads
        /// its own assembly with the real implementation — but it means the reference DLL
        /// we compile against cannot be used to reason about the server. Anything that must
        /// decide before ZNet exists uses the graphics-device tell below instead.
        /// </summary>
        public static bool IsDedicated()
        {
            ZNet znet = ZNet.instance;
            return znet != null && znet.IsDedicated();
        }

        /// <summary>
        /// Can this process draw anything at all? Decided at Awake, before ZNet exists, so
        /// visual components are never even added on a headless server.
        /// </summary>
        public static bool HasRenderer =>
            UnityEngine.SystemInfo.graphicsDeviceType !=
            UnityEngine.Rendering.GraphicsDeviceType.Null;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModConfig.Bind(base.Config);

            _harmony = new Harmony(PluginId);
            _harmony.PatchAll();

            // CairnTick is a plain MonoBehaviour driven from Update — deliberately NOT a
            // coroutine. See house style rule 2: every long-lived coroutine in this
            // studio's lineage independently grew a `continue`-past-`yield` hard-lock.
            gameObject.AddComponent<CairnTick>();

            RegisterSystems();

            // Proof of life. A silent success and a silent no-op are indistinguishable from
            // outside the game, so this line exists before there is anything to report.
            Log.LogInfo(
                $"{PluginName} v{PluginVersion} loaded — renderer={HasRenderer}, " +
                $"systems={CairnTick.SystemCount}. Nothing is simulated yet: this is the skeleton.");
        }

        /// <summary>
        /// Every system registers here, in one place, ticked in registration order by
        /// CairnTick's round-robin cursor. Ordering is a mild scheduling hint only — no
        /// system may depend on another having ticked first within the same frame.
        /// </summary>
        private static void RegisterSystems()
        {
            CairnTick.Register(new Systems.LandmarkSystem());
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            Instance = null;
        }
    }
}
