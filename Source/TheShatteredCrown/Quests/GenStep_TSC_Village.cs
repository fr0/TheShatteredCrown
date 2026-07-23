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
        /// <summary>Optional locked hatch spawned inside the inn (Maewyn's cellar).</summary>
        public ThingDef cellarHatch;
        /// <summary>Optional: skep hives scattered around the inn's yard (Maewyn's bees).</summary>
        public ThingDef beehive;
        public IntRange beehiveCount = new IntRange(3, 5);
        /// <summary>Optional: wild birds loitering around the inn (Maewyn's ravens; Odyssey crows). The first one is named Corvus.</summary>
        public PawnKindDef yardBird;
        public IntRange yardBirdCount = new IntRange(2, 4);
        public List<VillageBuilding> buildings = new List<VillageBuilding>();
        /// <summary>
        /// Named residents around the inn, re-spawned on EVERY map generation -
        /// this is what repopulates a persistent site on revisit (quest spawn
        /// nodes fire once and die with their quest). Serra and Oswin at camp.
        /// </summary>
        public List<NamedNpcDef> residents = new List<NamedNpcDef>();
        /// <summary>Skip the residents once this dialogue flag is set (the camp packs up after the rites).</summary>
        [NoTranslate]
        public string residentsSkipIfFlag;

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
                if (residentsSkipIfFlag.NullOrEmpty() || !DialogueStateManager.Current.IsSet(residentsSkipIfFlag))
                {
                    foreach (NamedNpcDef resident in residents)
                    {
                        SpawnResident(resident, faction, map, innPos);
                    }
                }
                if (cellarHatch != null)
                {
                    if (!CellFinder.TryFindRandomCellNear(innPos, map, 3,
                        c => c.InBounds(map) && c.Standable(map)
                            && c.GetFirstBuilding(map) == null && c.GetFirstItem(map) == null,
                        out IntVec3 hatchCell))
                    {
                        hatchCell = innPos;
                    }
                    GenSpawn.Spawn(ThingMaker.MakeThing(cellarHatch), hatchCell, map);
                }
                if (yardBird != null)
                {
                    int birds = yardBirdCount.RandomInRange;
                    for (int i = 0; i < birds; i++)
                    {
                        Pawn bird = PawnGenerator.GeneratePawn(yardBird);
                        IntVec3 birdCell = CellFinder.RandomClosewalkCellNear(innPos, map, 8);
                        GenSpawn.Spawn(bird, birdCell, map);
                        if (i == 0)
                        {
                            bird.Name = new NameSingle("Corvus");
                        }
                    }
                }
                if (beehive != null)
                {
                    int hives = beehiveCount.RandomInRange;
                    for (int i = 0; i < hives; i++)
                    {
                        // The yard, not the doorstep: outside the building
                        // footprint but within sight of the cottage.
                        if (CellFinder.TryFindRandomCellNear(innPos, map, 8,
                            c => c.InBounds(map) && c.Standable(map)
                                && c.GetFirstBuilding(map) == null && c.GetFirstItem(map) == null
                                && !c.Roofed(map) && c.DistanceTo(innPos) > 4f,
                            out IntVec3 hiveCell))
                        {
                            GenSpawn.Spawn(ThingMaker.MakeThing(beehive), hiveCell, map);
                        }
                    }
                }
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
