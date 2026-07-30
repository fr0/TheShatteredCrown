using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// No rifles at the founding.
    ///
    /// Vanilla's GeneratePossessions has a 25% roll that grants starting
    /// items from a pawn's traits, and the "careful shooter" trait carries a
    /// bolt-action rifle in its possessions list - which is how a medieval
    /// company rode out with a Lee-Enfield (CE's rename of that def) and
    /// fifty rounds. Random trait roll, so it surfaces once in many starts.
    ///
    /// The filter is deliberately narrow: only WEAPONS above medieval tech,
    /// only in this mod's scenarios. An addicted founder keeps their drugs,
    /// other scenarios and mods keep vanilla behaviour, and a medieval-or-
    /// lower possession of any kind passes untouched.
    /// </summary>
    [HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.GeneratePossessions))]
    public static class Patch_StartingPossessions_MedievalWeapons
    {
        public static void Postfix(Pawn pawn)
        {
            Scenario scenario = Find.Scenario;
            if (scenario == null || pawn == null)
            {
                return;
            }
            bool ours = false;
            foreach (ScenPart part in scenario.AllParts)
            {
                if (part is ScenPart_TSC_AdventureSetup || part is ScenPart_TSC_IntroSetup)
                {
                    ours = true;
                    break;
                }
            }
            if (!ours)
            {
                return;
            }
            Dictionary<Pawn, List<ThingDefCount>> possessions = Find.GameInitData?.startingPossessions;
            if (possessions == null || !possessions.TryGetValue(pawn, out List<ThingDefCount> list))
            {
                return;
            }
            list.RemoveAll(entry => entry.ThingDef != null
                && entry.ThingDef.IsWeapon
                && entry.ThingDef.techLevel > TechLevel.Medieval);
        }
    }
}
