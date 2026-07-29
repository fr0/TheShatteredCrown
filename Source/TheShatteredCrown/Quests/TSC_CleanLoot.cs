using HarmonyLib;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Gear from THIS MOD's enemies drops clean. "Tainted" (dead man's
    /// apparel) is stamped in exactly one place - Apparel.Notify_PawnKilled
    /// when the wearer dies - so the rule lives there: if the dead wearer
    /// was one of ours (a TSC pawn kind, or any member of the mod's bandit
    /// faction), the stamp is skipped and the piece strips clean.
    ///
    /// Why: in this mod, defeated enemies ARE the loot table - the Iron
    /// Brand's armory is meant to be worth the assault, and a keen cuirass
    /// nobody will wear is a joke with a long fuse. Vanilla raiders keep
    /// vanilla taint: their drops were always meant as smelter feed, and
    /// colony-sim mood rules stay intact where the colony sim is running.
    /// </summary>
    [HarmonyPatch(typeof(Apparel), nameof(Apparel.Notify_PawnKilled))]
    public static class Patch_ApparelTaint_CleanModDrops
    {
        public static bool Prefix(Apparel __instance)
        {
            Pawn wearer = __instance.Wearer;
            if (wearer == null)
            {
                return true;
            }
            bool ours = (wearer.kindDef?.defName.StartsWith("TSC_") ?? false)
                || (wearer.Faction != null && wearer.Faction.def.defName == "TSC_Faction_Bandits");
            return !ours; // ours: skip the taint stamp entirely
        }
    }
}
