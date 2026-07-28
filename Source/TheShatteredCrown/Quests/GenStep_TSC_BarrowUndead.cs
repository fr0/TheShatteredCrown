using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The barrow's tenants: a guaranteed pack of SHAMBLERS (Anomaly) risen
    /// around the mosskeeper's chest - the old kingdom's buried, medieval
    /// gear and all (generated from the bandit brigand kind). Skipped
    /// gracefully without Anomaly; the crypt layout's dormant insects are the
    /// fallback threat there.
    /// </summary>
    public class GenStep_TSC_BarrowUndead : GenStep
    {
        public IntRange count = new IntRange(4, 6);

        // Difficulty scaling clamp: even Peaceful keeps a token pair of
        // tenants (the barrow is never empty - that was the whole point),
        // and Losing Is Fun tops out at a crowded-but-fightable crypt.
        public IntRange scaledClamp = new IntRange(2, 10);

        public override int SeedPart => 918237412;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!ModsConfig.AnomalyActive)
            {
                return;
            }
            // Rise where the moss grows (it lies loose on the crypt floor);
            // the crypt is centered on the map, so the fallback also lands
            // inside it.
            Thing anchor = null;
            ThingDef mossDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_RemembranceMoss");
            if (mossDef != null)
            {
                foreach (Thing thing in map.listerThings.ThingsOfDef(mossDef))
                {
                    anchor = thing;
                    break;
                }
            }
            IntVec3 center = anchor != null ? anchor.Position : map.Center;
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand")
                ?? PawnKindDefOf.Villager;
            int n = TSC_Threat.Count(map, count, scaledClamp);
            for (int i = 0; i < n; i++)
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind, Faction.OfEntities, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false)
                {
                    ForcedMutant = MutantDefOf.Shambler,
                };
                Pawn shambler = PawnGenerator.GeneratePawn(request);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(center, map, 9);
                GenSpawn.Spawn(shambler, cell, map);
            }
        }
    }
}
