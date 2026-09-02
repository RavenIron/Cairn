using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RavenIron.Cairn.Config;
using UnityEngine;

namespace RavenIron.Cairn.Patches
{
    /// <summary>
    /// Makes a cairn affordable by rewriting `stone_pile`'s stone cost.
    ///
    /// THIS IS A BALANCE CHANGE TO A VANILLA PIECE, and it is the one thing in this mod that
    /// reaches outside its own scope. Recorded plainly because it was argued against and
    /// then decided: at vanilla's 50 stone a piece, a two-pile cairn costs 100 stone, which
    /// is enough that nobody builds waymarks casually — and a navigation mod whose waymarks
    /// are too expensive to litter has failed at the only thing it does. The owner's call,
    /// 2026-09-02, over a recorded objection that `stone_pile` is not our prefab and people
    /// build them for decoration too.
    ///
    /// Two things keep it honest. `StonePileStoneCost = 0` leaves vanilla entirely alone, so
    /// a server owner who disagrees has a switch rather than an argument. And the change
    /// announces itself at boot with the before and after, because a mod that silently
    /// halves a recipe is exactly the kind of surprise this studio complains about in others.
    ///
    /// Applied from a ZNetScene.Awake postfix: prefabs do not exist before that, and the
    /// Piece component's requirement array is shared prefab data, so one write covers every
    /// placement afterwards.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    public static class Patch_PieceCost
    {
        private const string PilePrefab = "stone_pile";
        private const string StoneItem = "Stone";

        private static bool _applied;

        private static void Postfix(ZNetScene __instance)
        {
            if (_applied) return;

            int target = ModConfig.StonePileStoneCost.Value;
            if (target <= 0) return;   // 0 means: leave vanilla alone

            try
            {
                _applied = Rewrite(__instance, target);
            }
            catch (Exception ex)
            {
                // A cosmetic-adjacent convenience must never take the scene down with it.
                Cairn.Log.LogWarning($"stone cost override failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Every game member here is reached by reflection.
        ///
        /// `Piece.m_resources` and `Requirement.m_amount` read as public in the publicized
        /// assembly, and so did `Terminal.commands` — which was private at runtime and killed
        /// a dedicated server mid-boot on 2026-09-02, before its own try/catch could run,
        /// because Mono raises FieldAccessException when the METHOD IS COMPILED. Naming them
        /// directly here would risk the same failure inside ZNetScene.Awake, which is a far
        /// worse place to lose.
        /// </summary>
        private static bool Rewrite(ZNetScene scene, int target)
        {
            GameObject prefab = scene.GetPrefab(PilePrefab);
            if (prefab == null)
            {
                Cairn.Log.LogWarning(
                    $"stone cost override: no prefab named '{PilePrefab}'. Vanilla left alone.");
                return false;
            }

            Component piece = prefab.GetComponent("Piece");
            if (piece == null)
            {
                Cairn.Log.LogWarning(
                    $"stone cost override: '{PilePrefab}' has no Piece component. Vanilla left alone.");
                return false;
            }

            FieldInfo resourcesField = AccessTools.Field(piece.GetType(), "m_resources");
            if (!(resourcesField?.GetValue(piece) is IEnumerable resources))
            {
                Cairn.Log.LogWarning(
                    "stone cost override: could not read Piece.m_resources — Valheim's API moved.");
                return false;
            }

            foreach (object req in resources)
            {
                if (req == null) continue;

                FieldInfo itemField = AccessTools.Field(req.GetType(), "m_resItem");
                FieldInfo amountField = AccessTools.Field(req.GetType(), "m_amount");
                if (itemField == null || amountField == null) continue;

                // The requirement's item is an ItemDrop component; its GameObject carries the
                // prefab name, which is the only stable way to tell stone from anything else.
                var item = itemField.GetValue(req) as Component;
                if (item == null || item.gameObject == null) continue;
                if (!string.Equals(item.gameObject.name, StoneItem, StringComparison.OrdinalIgnoreCase))
                    continue;

                int before = (int)amountField.GetValue(req);
                if (before == target)
                {
                    Cairn.Log.LogInfo($"stone cost override: '{PilePrefab}' already costs {target} stone.");
                    return true;
                }

                amountField.SetValue(req, target);
                Cairn.Log.LogWarning(
                    $"stone cost override ACTIVE: '{PilePrefab}' now costs {target} stone, was {before}. " +
                    "This edits a VANILLA recipe — set StonePileStoneCost = 0 to leave the game alone.");
                return true;
            }

            Cairn.Log.LogWarning(
                $"stone cost override: '{PilePrefab}' has no '{StoneItem}' requirement to change.");
            return false;
        }
    }
}
