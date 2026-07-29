using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The pocket-map portal dialog ("Exit the reliquary") builds its item
    /// manifest from everything haulable on the map, fog be damned - so the
    /// Kingsblade and the crown shard showed up on the exit manifest of a
    /// level the party had barely set foot on, lootable sight unseen. The
    /// caravan screen never hits this because home maps are explored;
    /// dungeon levels are MOSTLY fog when the dialog first opens.
    ///
    /// Rule: what nobody has seen is not on the manifest. Filtered by the
    /// held position (a sword inside a chest is as unseen as its chamber),
    /// silently - listing "???" rows would spoil the find just as hard.
    /// RPG mode only; vanilla's own portals (Anomaly) keep vanilla behavior.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_EnterPortal), "CalculateAndRecacheTransferables")]
    public static class Patch_EnterPortal_UnseenLoot
    {
        private static readonly System.Reflection.FieldInfo TransferablesField =
            AccessTools.Field(typeof(Dialog_EnterPortal), "transferables");

        public static void Postfix(Dialog_EnterPortal __instance)
        {
            if (!TSC_RpgMode.Active || TransferablesField == null)
            {
                return;
            }
            if (!(TransferablesField.GetValue(__instance) is List<TransferableOneWay> transferables))
            {
                return;
            }
            for (int i = transferables.Count - 1; i >= 0; i--)
            {
                TransferableOneWay transferable = transferables[i];
                if (transferable?.things == null || transferable.AnyThing is Pawn)
                {
                    continue;
                }
                transferable.things.RemoveAll(Unseen);
                if (transferable.things.Count == 0)
                {
                    transferables.RemoveAt(i);
                }
            }
        }

        internal static bool Unseen(Thing t)
        {
            Map map = t?.MapHeld;
            if (map == null)
            {
                return false; // carried by a pawn already off-map: not ours to judge
            }
            IntVec3 held = t.PositionHeld;
            return held.InBounds(map) && held.Fogged(map);
        }
    }

    /// <summary>
    /// The caravan reform screen has the same hole the portal dialog had:
    /// it lists every haulable on the map, fog included, so the Iron
    /// Brand's hoard shard (or any deep prize) could be queued for pickup
    /// sight unseen straight off the reform manifest. Same rule as the
    /// portal fix: what nobody has seen is not on the manifest. RPG mode
    /// only.
    /// </summary>
    [HarmonyPatch(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.AllReachableColonyItems))]
    public static class Patch_CaravanItems_UnseenLoot
    {
        public static void Postfix(System.Collections.Generic.List<Thing> __result)
        {
            if (TSC_RpgMode.Active)
            {
                __result?.RemoveAll(Patch_EnterPortal_UnseenLoot.Unseen);
            }
        }
    }
}
