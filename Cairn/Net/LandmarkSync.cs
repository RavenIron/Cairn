using System;
using System.Collections.Generic;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace RavenIron.Cairn.Net
{
    /// <summary>
    /// Server → client: where the lit cairns are.
    ///
    /// The beacon is drawn client-side from this cache and never from a networked object,
    /// which is the locked decision and the reason it can be seen at all. At beacon range the
    /// cairn's own ZDOs are not instantiated on the client — `ZDO.Distant` comes off the
    /// prefab and vanilla stone is near-only — so anything that waited for the object to load
    /// would light up only once you had already arrived.
    ///
    /// ABSOLUTE SNAPSHOTS, BROADCAST, UNCONDITIONAL. Every push carries every lit cairn in
    /// the world, sent to everyone on a fixed cadence whether or not anything changed. Deltas
    /// drift forever on one dropped packet, and a per-peer ring — which is right for zone
    /// state — is exactly wrong here: the whole point of a beacon is the one you can see from
    /// somewhere else. A hundred cairns is a few kilobytes.
    ///
    /// The RPC name carries a version suffix. A version-skewed pair then no-ops cleanly
    /// instead of misparsing a payload whose shape moved.
    /// </summary>
    public static class LandmarkSync
    {
        public const string RpcName = "com.raveniron.cairn.beacons1";

        /// <summary>One lit cairn, as a client knows it.</summary>
        public struct Beacon
        {
            public LandmarkKey Key;
            public Vector3 Light;
            public string Name;
        }

        private static readonly List<Beacon> _cache = new List<Beacon>(32);
        private static ZRoutedRpc _registeredOn;

        /// <summary>Bumped on every received push, so a renderer can rebuild only on change.</summary>
        public static int Revision { get; private set; }

        /// <summary>
        /// The lit cairns this machine knows about. The authority reads its own store — a
        /// listen host must not wait on a network round-trip to itself — and everyone else
        /// reads the synced cache.
        /// </summary>
        public static List<Beacon> Current()
        {
            if (!Persistence.IsLoaded) return _cache;

            var live = new List<Beacon>(16);
            foreach (Landmark l in LandmarkStore.Snapshot())
            {
                if (!l.HasPile) continue;
                live.Add(new Beacon { Key = l.Key, Light = l.Light, Name = l.Name });
            }
            return live;
        }

        public static void EnsureRegistered()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            try
            {
                rpc.Register<ZPackage>(RpcName, RPC_Beacons);
                _registeredOn = rpc;
                _cache.Clear();
                Revision++;
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"LandmarkSync: register failed: {ex.Message}");
                _registeredOn = rpc;   // do not retry every tick against a broken registration
            }
        }

        /// <summary>Authority-side: broadcast every lit cairn to everyone.</summary>
        public static void Broadcast(int maxBeacons)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) return;

            try
            {
                var pkg = new ZPackage();
                var lit = new List<Landmark>(16);

                foreach (Landmark l in LandmarkStore.Snapshot())
                {
                    if (!l.HasPile) continue;
                    lit.Add(l);
                    if (lit.Count >= maxBeacons) break;
                }

                pkg.Write(lit.Count);
                foreach (Landmark l in lit)
                {
                    pkg.Write(l.Key.X);
                    pkg.Write(l.Key.Y);
                    pkg.Write(l.Key.Z);
                    pkg.Write(l.Light.x);
                    pkg.Write(l.Light.y);
                    pkg.Write(l.Light.z);
                    pkg.Write(l.Name ?? "");
                }

                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pkg);
            }
            catch (Exception ex)
            {
                Cairn.Log.LogWarning($"LandmarkSync: broadcast failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Receiver. Refuses its own broadcast on a listen host, which would otherwise
        /// overwrite a cache the authority reads straight from the store anyway.
        /// </summary>
        private static void RPC_Beacons(long sender, ZPackage pkg)
        {
            if (Persistence.IsLoaded) return;   // authority: the store is the truth here

            try
            {
                var received = new List<Beacon>(16);
                int count = pkg.ReadInt();

                for (int i = 0; i < count; i++)
                {
                    int kx = pkg.ReadInt();
                    int ky = pkg.ReadInt();
                    int kz = pkg.ReadInt();
                    float lx = pkg.ReadSingle();
                    float ly = pkg.ReadSingle();
                    float lz = pkg.ReadSingle();
                    string name = pkg.ReadString();

                    received.Add(new Beacon
                    {
                        Key = new LandmarkKey(kx, ky, kz),
                        Light = new Vector3(lx, ly, lz),
                        Name = name
                    });
                }

                _cache.Clear();
                _cache.AddRange(received);
                Revision++;
            }
            catch (Exception ex)
            {
                // A malformed payload must not poison a cache that is about to be replaced
                // wholesale by the next push anyway.
                Cairn.Log.LogWarning($"LandmarkSync: receive failed: {ex.Message}");
            }
        }
    }
}
