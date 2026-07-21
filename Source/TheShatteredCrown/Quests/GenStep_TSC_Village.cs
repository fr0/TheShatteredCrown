using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// Builds a small village: an inn near the center and a ring of buildings
    /// around it, each optionally housing a persistent named resident who stays
    /// near their own home. Configured entirely from the GenStepDef XML.
    /// </summary>
    public class GenStep_TSC_Village : GenStep
    {
        public PrefabDef innPrefab;
        public NamedNpcDef innkeeper;
        public List<VillageBuilding> buildings = new List<VillageBuilding>();

        public override int SeedPart => 190847312;

        public override void Generate(Map map, GenStepParams parms)
        {
            IntVec3 center = map.Center;
            Faction faction = VillagerFaction();

            // Village square
            foreach (IntVec3 cell in CellRect.CenteredOn(center, 5, 5))
            {
                if (cell.InBounds(map))
                {
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.PackedDirt);
                }
            }

            // The inn, north of the square
            if (innPrefab != null)
            {
                IntVec3 innPos = center + new IntVec3(0, 0, 10);
                SpawnBuilding(innPrefab, map, innPos);
                SpawnResident(innkeeper, faction, map, innPos);
            }

            // Buildings around the square
            List<IntVec3> offsets = new List<IntVec3>
            {
                new IntVec3(-16, 0, 0),
                new IntVec3(16, 0, 0),
                new IntVec3(-14, 0, -13),
                new IntVec3(14, 0, -13),
                new IntVec3(0, 0, -17),
            };
            for (int i = 0; i < buildings.Count && i < offsets.Count; i++)
            {
                IntVec3 pos = center + offsets[i];
                SpawnBuilding(buildings[i].prefab, map, pos);
                SpawnResident(buildings[i].resident, faction, map, pos);
            }
        }

        private static void SpawnBuilding(PrefabDef prefab, Map map, IntVec3 pos)
        {
            if (prefab == null)
            {
                return;
            }
            // Clear generously around the target spot (trees, rocks, chunks,
            // overhead mountain) so the prefab has room regardless of terrain.
            CellRect clearRect = CellRect.CenteredOn(pos, prefab.size.x + 6, prefab.size.z + 6);
            foreach (IntVec3 cell in clearRect)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                map.roofGrid.SetRoof(cell, null);
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing thing = things[i];
                    if (thing.def.category == ThingCategory.Plant
                        || thing.def.category == ThingCategory.Item
                        || (thing.def.category == ThingCategory.Building && thing.def.building != null && thing.def.building.isNaturalRock)
                        || thing.def.IsFilth)
                    {
                        thing.Destroy();
                    }
                }
            }
            PrefabUtility.SpawnPrefab(prefab, map, pos, Rot4.North);
        }

        private static void SpawnResident(NamedNpcDef npcDef, Faction faction, Map map, IntVec3 homePos)
        {
            if (npcDef == null || faction == null)
            {
                return;
            }
            Pawn pawn = DialogueStateManager.Current.GetOrGenerateNamedNpc(npcDef, faction);
            if (pawn.Dead || pawn.Spawned || pawn.Faction == Faction.OfPlayer)
            {
                return;
            }
            if (pawn.IsWorldPawn())
            {
                Find.WorldPawns.RemovePawn(pawn);
            }
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(homePos, map, 5);
            GenSpawn.Spawn(pawn, cell, map);
            if (pawn.Faction != null)
            {
                LordMaker.MakeNewLord(pawn.Faction, new LordJob_DefendPoint(homePos), map, Gen.YieldSingle(pawn));
            }
        }

        private static Faction VillagerFaction()
        {
            Faction best = null;
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (faction.IsPlayer || faction.Hidden || faction.defeated || !faction.def.humanlikeFaction)
                {
                    continue;
                }
                if (faction.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }
                if (best == null || (faction.def.techLevel <= TechLevel.Medieval && best.def.techLevel > TechLevel.Medieval))
                {
                    best = faction;
                }
            }
            return best;
        }
    }

    public class VillageBuilding
    {
        public PrefabDef prefab;
        public NamedNpcDef resident;
    }
}
