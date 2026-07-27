using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Strips anachronisms from the crownless bandits' camps: vanilla's
    /// BanditCamp site machinery unconditionally adds power (solar generator,
    /// conduits - the BanditCampPower genstep) and the outpost generator can
    /// place other industrial gear. Runs on every BanditCamp site but only
    /// ACTS when the site belongs to TSC_Faction_Bandits, so vanilla bandit
    /// camps elsewhere in the save keep their vanilla look.
    /// </summary>
    public class GenStep_TSC_MedievalPurge : GenStep
    {
        private static readonly string[] ModernTerrains =
        {
            "Concrete", "PavedTile", "MetalTile", "SterileTile", "BrokenAsphalt",
        };

        public override int SeedPart => 771203944;

        public override void Generate(Map map, GenStepParams parms)
        {
            // Ours if the SITE belongs to the crownless bandits - or, fallback,
            // if its defenders do (covers any path where the world object's
            // faction ends up as the rolled vanilla faction instead of the
            // quest's override).
            bool ours = map.ParentFaction?.def?.defName == "TSC_Faction_Bandits";
            if (!ours)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (pawns[i].Faction?.def?.defName == "TSC_Faction_Bandits")
                    {
                        ours = true;
                        break;
                    }
                }
            }
            if (!ours)
            {
                Log.Message($"[The Shattered Crown] MedievalPurge: skipping bandit camp (site faction: {map.ParentFaction?.Name ?? "none"}) - not the crownless bandits.");
                return;
            }
            Purge(map, "the bandit camp");
        }

        /// <summary>Strips above-medieval buildings and modern floors from a map. Also used on peacefully visited cities.</summary>
        public static void Purge(Map map, string context)
        {
            // Collect first: destroying mutates the lister mid-iteration.
            List<Thing> anachronisms = new List<Thing>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing.def.category != ThingCategory.Building || !thing.def.destroyable)
                {
                    continue;
                }
                // Above-medieval tech goes - but many electrics (the
                // wood-fired generator) ship with NO techLevel at all, so
                // anything that generates, stores, uses, or carries POWER is
                // anachronistic by definition.
                if ((int)thing.def.techLevel > (int)TechLevel.Medieval
                    || thing.def.HasAssignableCompFrom(typeof(CompPower)))
                {
                    anachronisms.Add(thing);
                }
            }
            foreach (Thing thing in anachronisms)
            {
                thing.Destroy();
            }
            Log.Message($"[The Shattered Crown] MedievalPurge: stripped {anachronisms.Count} above-medieval building(s) from {context}.");
            // Modern floors go back to earth.
            foreach (IntVec3 cell in map.AllCells)
            {
                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                if (terrain != null && System.Array.IndexOf(ModernTerrains, terrain.defName) >= 0)
                {
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Gravel);
                }
            }
        }
    }
}
