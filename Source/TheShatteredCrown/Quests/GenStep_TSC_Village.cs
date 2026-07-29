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
    /// <summary>
    /// Runs AFTER vanilla GenStep_Fog (order 1230 vs our 1300): clears all
    /// fog so a friendly village's homes aren't black boxes on arrival. The
    /// villagers live here openly; there is nothing to "discover".
    /// </summary>
    public class GenStep_TSC_Unfog : GenStep
    {
        public override int SeedPart => 190847313;

        public override void Generate(Map map, GenStepParams parms)
        {
            map.fogGrid.ClearAllFog();
        }
    }

    public class GenStep_TSC_Village : GenStep
    {
        public PrefabDef innPrefab;
        public NamedNpcDef innkeeper;
        /// <summary>Optional locked hatch spawned inside the inn (Maewyn's cellar).</summary>
        public ThingDef cellarHatch;
        /// <summary>Optional: skep hives scattered around the inn's yard (Maewyn's bees).</summary>
        public ThingDef beehive;
        public IntRange beehiveCount = new IntRange(3, 5);
        /// <summary>Optional: wild birds loitering around the inn (Maewyn's ravens; Odyssey crows). Corvus himself is her bonded pet, spawned by SpawnResident.</summary>
        public PawnKindDef yardBird;
        public IntRange yardBirdCount = new IntRange(2, 4);
        /// <summary>Optional centerpiece of the packed-dirt square (Harrowfield's well).</summary>
        public ThingDef well;

        /// <summary>
        /// Optional resident posted at the square instead of a house
        /// (Thornden's Odo, who sits on the well kerb and sees everything
        /// that happens there). Give them homebody on their NamedNpcDef: the
        /// square IS their spot, and a witness who wanders is a witness the
        /// player never finds.
        /// </summary>
        public NamedNpcDef squareResident;

        /// <summary>
        /// Optional perimeter fence drawn around everything the village spawns
        /// (houses, yards, fields), with a gate at the middle of each side.
        /// People step over fences and animals do not, so this pens the
        /// livestock without walling anybody in. The dialogue leans on it:
        /// Bryn holds court on the rail, Serra camps "outside the fence".
        /// </summary>
        public ThingDef fence;
        public ThingDef fenceGate;
        public ThingDef fenceStuff;
        /// <summary>Open ground left between the outermost building or field and the fence line.</summary>
        public int fenceMargin = 4;
        public List<VillageBuilding> buildings = new List<VillageBuilding>();
        /// <summary>The stock background villagers are cut from (fillerCount on each building).</summary>
        public PawnKindDef fillerKind;
        /// <summary>
        /// Named residents around the inn, re-spawned on EVERY map generation -
        /// this is what repopulates a persistent site on revisit (quest spawn
        /// nodes fire once and die with their quest). Serra and Oswin at camp.
        /// </summary>
        public List<NamedNpcDef> residents = new List<NamedNpcDef>();
        /// <summary>Skip the residents once this dialogue flag is set (the camp packs up after the rites).</summary>
        [NoTranslate]
        public string residentsSkipIfFlag;

        /// <summary>
        /// Follow-on camp pitched just outside the village from the SECOND
        /// visit on: Serra and Oswin keep the rites' promise and meet the
        /// courier at Harrowfield. First generation marks campVisitedFlag;
        /// later generations (with campRequireFlag met) raise the camp and
        /// set campArrivedFlag, which gates their village dialogue.
        /// </summary>
        public PrefabDef campPrefab;
        public List<NamedNpcDef> campResidents = new List<NamedNpcDef>();
        [NoTranslate]
        public string campVisitedFlag;
        [NoTranslate]
        public string campRequireFlag;
        [NoTranslate]
        public string campArrivedFlag;

        public override int SeedPart => 190847312;

        public override void Generate(Map map, GenStepParams parms)
        {
            IntVec3 center = map.Center;
            Faction faction = VillagerFaction();
            // Everything the village occupies, for the fence line to enclose.
            List<CellRect> footprint = new List<CellRect> { CellRect.CenteredOn(center, 5, 5) };

            // Village square
            foreach (IntVec3 cell in CellRect.CenteredOn(center, 5, 5))
            {
                if (cell.InBounds(map))
                {
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.PackedDirt);
                }
            }
            // The well anchors the square (and gives "ask at the well" a place).
            if (well != null)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(well), center, map);
            }

            // The inn, north of the square
            if (innPrefab != null)
            {
                IntVec3 innPos = center + new IntVec3(0, 0, 10);
                SpawnBuilding(innPrefab, map, innPos);
                footprint.Add(CellRect.CenteredOn(innPos, innPrefab.size.x, innPrefab.size.z));
                List<Building_Bed> innBeds = SpawnBedsFor(map, faction, innPos, innPrefab,
                    (innkeeper != null ? 1 : 0) + residents.Count, CountCouples(Gen.YieldSingle(innkeeper), residents));
                List<Pawn> innPawns = new List<Pawn>();
                AddIfSpawned(innPawns, SpawnResident(innkeeper, faction, map, innPos, innPrefab, center));
                if (residentsSkipIfFlag.NullOrEmpty() || !DialogueStateManager.Current.IsSet(residentsSkipIfFlag))
                {
                    foreach (NamedNpcDef resident in residents)
                    {
                        AddIfSpawned(innPawns, SpawnResident(resident, faction, map, innPos, innPrefab, center));
                    }
                }
                AssignBeds(innPawns, innBeds);
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

            // Buildings around the square. SOUTHERN slots first: crop fields
            // extend south of their building, so list field-bearing buildings
            // (the farms) first and they take the outer ring where the fields
            // have open ground.
            List<IntVec3> offsets = new List<IntVec3>
            {
                new IntVec3(-14, 0, -13),
                new IntVec3(14, 0, -13),
                new IntVec3(0, 0, -17),
                new IntVec3(-16, 0, 0),
                new IntVec3(16, 0, 0),
            };
            for (int i = 0; i < buildings.Count && i < offsets.Count; i++)
            {
                VillageBuilding building = buildings[i];
                IntVec3 pos = center + offsets[i];
                SpawnBuilding(building.prefab, map, pos);
                if (building.prefab != null)
                {
                    footprint.Add(CellRect.CenteredOn(pos, building.prefab.size.x, building.prefab.size.z));
                }
                // Persistent household: the count is rolled ONCE per site and
                // the same folk come back every visit - new faces on every
                // approach read as a village of strangers.
                int fillers = TSC_FillerRegistry.Current?.HouseholdSize(map, i) ?? -1;
                if (fillers < 0)
                {
                    fillers = building.fillerCount.RandomInRange;
                }
                List<Building_Bed> beds = SpawnBedsFor(map, faction, pos, building.prefab,
                    (building.resident != null ? 1 : 0) + building.residents.Count + fillers,
                    CountCouples(Gen.YieldSingle(building.resident), building.residents));
                // Work spot: the farm's field, or the yard in front of the
                // building (facing the square) for everyone else.
                IntVec3 workSpot = pos + new IntVec3(0, 0, -((building.prefab?.size.z ?? 6) / 2 + 3));
                if (building.cropField && building.prefab != null)
                {
                    CellRect field = new CellRect(pos.x - 5, pos.z - building.prefab.size.z / 2 - 10, 11, 8);
                    SpawnCropField(map, field);
                    footprint.Add(field);
                    workSpot = field.CenterCell;
                }
                List<Pawn> housePawns = new List<Pawn>();
                AddIfSpawned(housePawns, SpawnResident(building.resident, faction, map, pos, building.prefab, workSpot));
                foreach (NamedNpcDef resident in building.residents)
                {
                    AddIfSpawned(housePawns, SpawnResident(resident, faction, map, pos, building.prefab, workSpot));
                }
                SpawnFillerHousehold(map, faction, building, i, fillers, pos, building.prefab, workSpot, housePawns);
                AssignBeds(housePawns, beds);
                SpawnLivestock(map, faction, pos, building);
                if (building.yardProp != null && building.prefab != null)
                {
                    // The woodpile: in the yard, clear of the door line.
                    if (CellFinder.TryFindRandomCellNear(pos, map, building.prefab.size.x / 2 + 3,
                        c => c.InBounds(map) && c.Standable(map)
                            && c.GetFirstBuilding(map) == null && c.GetFirstItem(map) == null
                            && c.DistanceTo(pos) > building.prefab.size.x / 2f + 1f,
                        out IntVec3 propCell))
                    {
                        GenSpawn.Spawn(ThingMaker.MakeThing(building.yardProp), propCell, map);
                    }
                }
            }

            // The square's own fixture, after the buildings so nothing lands
            // on top of them: no house, no bed, no errands. Home and work are
            // both the well.
            SpawnResident(squareResident, faction, map, center, null, center);

            // Fence last: the camp's ground-clearing would chew through a line
            // drawn before it, and the fence must read as enclosing what is
            // already there.
            SpawnFollowOnCamp(map, faction, center);
            SpawnFence(map, faction, footprint);
        }

        /// <summary>
        /// Runs a fence around the bounding box of everything the village
        /// spawned, with a gate at the middle of each side. Skips cells that
        /// are already built on or that cannot take a structure (water, the
        /// crop fields' edges are fine), so a fence never cuts through a wall.
        /// </summary>
        private void SpawnFence(Map map, Faction faction, List<CellRect> footprint)
        {
            if (fence == null || footprint.Count == 0)
            {
                return;
            }
            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (CellRect part in footprint)
            {
                minX = System.Math.Min(minX, part.minX);
                maxX = System.Math.Max(maxX, part.maxX);
                minZ = System.Math.Min(minZ, part.minZ);
                maxZ = System.Math.Max(maxZ, part.maxZ);
            }
            CellRect perimeter = new CellRect(
                minX - fenceMargin, minZ - fenceMargin,
                maxX - minX + 1 + fenceMargin * 2, maxZ - minZ + 1 + fenceMargin * 2)
                .ClipInsideMap(map);
            ThingDef stuff = fenceStuff ?? ThingDefOf.WoodLog;
            HashSet<IntVec3> gates = new HashSet<IntVec3>();
            if (fenceGate != null)
            {
                IntVec3 middle = perimeter.CenterCell;
                gates.Add(new IntVec3(middle.x, 0, perimeter.maxZ));
                gates.Add(new IntVec3(middle.x, 0, perimeter.minZ));
                gates.Add(new IntVec3(perimeter.minX, 0, middle.z));
                gates.Add(new IntVec3(perimeter.maxX, 0, middle.z));
            }
            foreach (IntVec3 cell in perimeter.EdgeCells)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                bool blocked = false;
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing thing = things[i];
                    if (thing.def.category == ThingCategory.Plant || thing.def.IsFilth)
                    {
                        thing.Destroy();
                    }
                    else if (thing.def.category == ThingCategory.Building)
                    {
                        blocked = true;
                    }
                }
                if (blocked)
                {
                    continue;
                }
                ThingDef def = gates.Contains(cell) ? fenceGate : fence;
                if (!GenConstruct.CanPlaceBlueprintAt(def, cell, Rot4.North, map).Accepted)
                {
                    continue;
                }
                Thing post = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);
                post.SetFaction(faction);
                GenSpawn.Spawn(post, cell, map, Rot4.North);
            }
        }

        /// <summary>The Wayfarers' camp outside the village, second visit onward.</summary>
        private void SpawnFollowOnCamp(Map map, Faction faction, IntVec3 center)
        {
            if (campPrefab == null || campVisitedFlag.NullOrEmpty())
            {
                return;
            }
            DialogueStateManager state = DialogueStateManager.Current;
            bool visitedBefore = state.IsSet(campVisitedFlag);
            state.Set(campVisitedFlag);
            if (!visitedBefore
                || (!campRequireFlag.NullOrEmpty() && !state.IsSet(campRequireFlag)))
            {
                return;
            }
            // Act 2: the company settled in the bard's city - the Harrowfield
            // camp does not quietly reclaim them on village regeneration.
            if (state.IsSet("TSC_CompanionsSettled"))
            {
                return;
            }
            // North of the square, clear of the building ring: outside the
            // village, within sight of the well.
            IntVec3 campPos = center + new IntVec3(0, 0, 18);
            SpawnBuilding(campPrefab, map, campPos);
            List<Building_Bed> beds = SpawnBedsFor(map, faction, campPos, campPrefab,
                campResidents.Count, CountCouples(new List<NamedNpcDef>(), campResidents));
            List<Pawn> pawns = new List<Pawn>();
            foreach (NamedNpcDef resident in campResidents)
            {
                AddIfSpawned(pawns, SpawnResident(resident, faction, map, campPos, campPrefab, campPos));
            }
            AssignBeds(pawns, beds);
            if (!campArrivedFlag.NullOrEmpty() && pawns.Count > 0)
            {
                state.Set(campArrivedFlag);
            }
        }

        /// <summary>
        /// Yard animals (the chickens Tam disowns, Hessa's argumentative
        /// goat): tame, villager faction, held near their building by a
        /// stay-home villager lord. Regenerated per visit like the crows.
        /// </summary>
        private static void SpawnLivestock(Map map, Faction faction, IntVec3 pos, VillageBuilding building)
        {
            if (faction == null || building.livestock.NullOrEmpty())
            {
                return;
            }
            IntVec3 yard = pos + new IntVec3(0, 0, -((building.prefab?.size.z ?? 6) / 2 + 2));
            Lord lord = null;
            foreach (LivestockEntry entry in building.livestock)
            {
                if (entry?.kind == null)
                {
                    continue;
                }
                int count = entry.count.RandomInRange;
                for (int i = 0; i < count; i++)
                {
                    Pawn animal = PawnGenerator.GeneratePawn(entry.kind, faction);
                    GenSpawn.Spawn(animal, CellFinder.RandomClosewalkCellNear(yard, map, 6), map);
                    if (lord == null)
                    {
                        lord = LordMaker.MakeNewLord(faction,
                            new LordJob_TSC_Villager(yard, yard, stayHome: true), map, Gen.YieldSingle(animal));
                    }
                    else
                    {
                        lord.AddPawn(animal);
                    }
                }
            }
        }

        /// <summary>A worked field: soil and half-grown potatoes in neat misery.</summary>
        private static void SpawnCropField(Map map, CellRect field)
        {
            foreach (IntVec3 cell in field)
            {
                if (!cell.InBounds(map) || cell.GetEdifice(map) != null)
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    if (things[i].def.category == ThingCategory.Plant || things[i].def.IsFilth)
                    {
                        things[i].Destroy();
                    }
                }
                TerrainDef terrain = cell.GetTerrain(map);
                if (terrain == null || terrain.IsWater)
                {
                    continue;
                }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                Plant plant = (Plant)GenSpawn.Spawn(ThingDefOf.Plant_Potato, cell, map);
                plant.Growth = Rand.Range(0.3f, 0.85f);
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

        private static Pawn SpawnResident(NamedNpcDef npcDef, Faction faction, Map map, IntVec3 homePos,
            PrefabDef homePrefab, IntVec3 workSpot)
        {
            if (npcDef == null || faction == null)
            {
                return null;
            }
            // The quest owns their whereabouts while it runs (the Root
            // children are IN THE HILLS, not at the woodpile).
            if (npcDef.awayWhileQuestActive != null)
            {
                foreach (Quest quest in Find.QuestManager.QuestsListForReading)
                {
                    if (quest.root == npcDef.awayWhileQuestActive
                        && (quest.State == QuestState.Ongoing || quest.State == QuestState.NotYetAccepted))
                    {
                        return null;
                    }
                }
            }
            // Gone missing between visits: after awayIfFlag is set they stop
            // spawning at home (until backAfterQuest succeeds), and the skip
            // announces itself via setFlagWhenAway - which is what gates the
            // rescue quest's offer. No on-screen vanish, ever.
            if (!npcDef.awayIfFlag.NullOrEmpty() && DialogueStateManager.Current.IsSet(npcDef.awayIfFlag))
            {
                bool rescued = false;
                if (npcDef.backAfterQuest != null)
                {
                    foreach (Quest quest in Find.QuestManager.QuestsListForReading)
                    {
                        if (quest.root == npcDef.backAfterQuest && quest.State == QuestState.EndedSuccess)
                        {
                            rescued = true;
                            break;
                        }
                    }
                }
                if (!rescued)
                {
                    if (!npcDef.setFlagWhenAway.NullOrEmpty())
                    {
                        DialogueStateManager.Current.Set(npcDef.setFlagWhenAway);
                    }
                    return null;
                }
            }
            Pawn pawn = DialogueStateManager.Current.GetOrGenerateNamedNpc(npcDef, faction);
            if (pawn.Dead || pawn.Spawned || pawn.Faction == Faction.OfPlayer)
            {
                return null;
            }
            if (pawn.IsWorldPawn())
            {
                Find.WorldPawns.RemovePawn(pawn);
            }
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(homePos, map, 5);
            GenSpawn.Spawn(pawn, cell, map);
            // Persistent pawns respawn across many map generations; make sure
            // the render tree rebuilds cleanly each time ("Node is null ...
            // EnsureGraphicsInitialized" seen on revisits).
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            if (pawn.Faction != null)
            {
                // Day at the work spot, night indoors: the villager routine.
                // Homebodies (Old Wick) keep to the hearth around the clock.
                IntVec3 indoors = InteriorCell(map, homePos, homePrefab);
                Lord lord = LordMaker.MakeNewLord(pawn.Faction,
                    new LordJob_TSC_Villager(indoors, workSpot, npcDef.homebody), map, Gen.YieldSingle(pawn));
                // Companion animal (Betsy): bonded, and in the same lord so
                // the pet keeps its owner's daily routine.
                if (npcDef.petKind != null)
                {
                    Pawn pet = PawnGenerator.GeneratePawn(npcDef.petKind, pawn.Faction);
                    if (!npcDef.petName.NullOrEmpty())
                    {
                        pet.Name = new NameSingle(npcDef.petName);
                    }
                    GenSpawn.Spawn(pet, CellFinder.RandomClosewalkCellNear(cell, map, 3), map);
                    if (pawn.relations != null && pet.relations != null)
                    {
                        // The owner persists across map generations but the pet
                        // is remade each visit; drop the bonds to the retired
                        // stand-ins so only the live one is ever "their" animal.
                        DropStaleAnimalBonds(pawn);
                        pawn.relations.AddDirectRelation(PawnRelationDefOf.Bond, pet);
                    }
                    lord.AddPawn(pet);
                }
            }
            EnsureTrader(pawn, npcDef, map);
            // Pantry top-up: villagers only eat from their pockets (the duty
            // tree's GetFood; they can't harvest the growing crops), so every
            // spawn guarantees a stock. TSC_StoryHubGuard also refeeds any
            // named NPC who runs dry mid-visit - they never starve.
            if (pawn.inventory != null)
            {
                bool hasFood = false;
                foreach (Thing thing in pawn.inventory.innerContainer)
                {
                    if (thing.def.IsNutritionGivingIngestible)
                    {
                        hasFood = true;
                        break;
                    }
                }
                if (!hasFood)
                {
                    Thing pemmican = ThingMaker.MakeThing(ThingDefOf.Pemmican);
                    pemmican.stackCount = 10;
                    pawn.inventory.innerContainer.TryAdd(pemmican, false);
                }
            }
            return pawn;
        }

        /// <summary>
        /// Background villagers: generated ONCE per site (TSC_FillerRegistry
        /// remembers them by site + building) and re-spawned with the same
        /// names and faces on every visit, on the same day/night routine as
        /// the household they lodge with. The dead stay dead - a filler
        /// killed in the square is a face missing next visit, not replaced.
        /// No dialogue: their kind carries no DialogueExtension.
        /// </summary>
        private void SpawnFillerHousehold(Map map, Faction faction, VillageBuilding building,
            int buildingIndex, int count, IntVec3 homePos, PrefabDef homePrefab,
            IntVec3 workSpot, List<Pawn> housePawns)
        {
            if (faction == null || count <= 0)
            {
                return;
            }
            TSC_FillerRegistry registry = TSC_FillerRegistry.Current;
            List<Pawn> household = registry?.GetHousehold(map, buildingIndex);
            if (household == null)
            {
                household = new List<Pawn>();
                PawnKindDef kind = fillerKind ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager");
                if (kind == null)
                {
                    return;
                }
                for (int f = 0; f < count; f++)
                {
                    household.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        kind, faction, PawnGenerationContext.NonPlayer,
                        forceGenerateNewPawn: true, canGeneratePawnRelations: false)));
                }
                registry?.SetHousehold(map, buildingIndex, household);
            }
            IntVec3 indoors = InteriorCell(map, homePos, homePrefab);
            foreach (Pawn pawn in household)
            {
                if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Spawned
                    || pawn.Faction == Faction.OfPlayer)
                {
                    continue;
                }
                if (pawn.IsWorldPawn())
                {
                    Find.WorldPawns.RemovePawn(pawn);
                }
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(homePos, map, 5);
                GenSpawn.Spawn(pawn, cell, map);
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                LordMaker.MakeNewLord(pawn.Faction ?? faction, new LordJob_TSC_Villager(indoors, workSpot, false),
                    map, Gen.YieldSingle(pawn));
                if (pawn.inventory != null)
                {
                    bool hasFood = false;
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        if (thing.def.IsNutritionGivingIngestible)
                        {
                            hasFood = true;
                            break;
                        }
                    }
                    if (!hasFood)
                    {
                        Thing pemmican = ThingMaker.MakeThing(ThingDefOf.Pemmican);
                        pemmican.stackCount = 10;
                        pawn.inventory.innerContainer.TryAdd(pemmican, false);
                    }
                }
                AddIfSpawned(housePawns, pawn);
            }
        }

        /// <summary>
        /// Clears an owner's bonds to companion animals that are no longer in
        /// play. Owners persist between visits but their pet is generated
        /// fresh each time, so without this the bond list grows by one retired
        /// animal per map generation - and "their" animal stops being singular.
        /// </summary>
        private static void DropStaleAnimalBonds(Pawn owner)
        {
            if (owner.relations == null)
            {
                return;
            }
            List<Pawn> stale = new List<Pawn>();
            foreach (DirectPawnRelation relation in owner.relations.DirectRelations)
            {
                Pawn other = relation.otherPawn;
                if (relation.def == PawnRelationDefOf.Bond && other != null
                    && other.RaceProps.Animal && (other.Dead || !other.Spawned))
                {
                    stale.Add(other);
                }
            }
            foreach (Pawn animal in stale)
            {
                owner.relations.RemoveDirectRelation(PawnRelationDefOf.Bond, animal);
            }
        }

        /// <summary>
        /// Standing merchants (Haldor): attach the trader tracker (as the
        /// TraderKindDef carrier) and (re)generate stock from the def
        /// whenever they lack signature goods. PUBLIC: the dialogue trade()
        /// effect calls this too, so trading self-heals at the moment of
        /// trading regardless of when/how the pawn was generated (map-gen-
        /// only stocking left sold-out or pre-feature pawns stranded).
        /// </summary>
        public static void EnsureTrader(Pawn pawn, NamedNpcDef npcDef, Map map)
        {
            if (npcDef?.traderKind == null || pawn.inventory == null || map == null)
            {
                return;
            }
            if (pawn.trader == null)
            {
                pawn.trader = new Pawn_TraderTracker(pawn);
            }
            pawn.trader.traderKind = npcDef.traderKind;
            // "Already stocked" means signature goods on hand - weapons or
            // apparel - NOT merely a non-empty inventory: villagers spawn
            // with pocket food, which once fooled this check into never
            // stocking the shop at all (the browse-his-lunch bug).
            foreach (Thing thing in pawn.inventory.innerContainer)
            {
                if (thing.def.IsWeapon || thing.def.IsApparel)
                {
                    return;
                }
            }
            foreach (StockGenerator generator in npcDef.traderKind.stockGenerators)
            {
                foreach (Thing thing in generator.GenerateThings(map.Tile, pawn.Faction))
                {
                    if (thing is Pawn)
                    {
                        thing.Destroy(); // a smithy sells steel, not sheep
                        continue;
                    }
                    pawn.inventory.innerContainer.TryAdd(thing, false);
                }
            }
        }

        /// <summary>Married residents of one building: pairs where one def names the other as spouse.</summary>
        private static int CountCouples(IEnumerable<NamedNpcDef> single, List<NamedNpcDef> rest)
        {
            List<NamedNpcDef> all = new List<NamedNpcDef>();
            foreach (NamedNpcDef def in single)
            {
                if (def != null)
                {
                    all.Add(def);
                }
            }
            all.AddRange(rest);
            int couples = 0;
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    if (all[i].spouse == all[j] || all[j].spouse == all[i])
                    {
                        couples++;
                    }
                }
            }
            return couples;
        }

        private static void AddIfSpawned(List<Pawn> list, Pawn pawn)
        {
            if (pawn != null)
            {
                list.Add(pawn);
            }
        }

        /// <summary>
        /// Beds for the household, CLAIMED for the villagers' faction - NPC
        /// rest-seeking only takes beds of the sleeper's own faction (the
        /// same rule that beds sleeping raiders at outposts). Prefab-placed
        /// beds (cottage furniture, camp bedrolls) are adopted first; then a
        /// wooden DOUBLE bed per married couple, then singles until everyone
        /// has a slot, greedily on free roofed interior cells. Returns every
        /// bed of the building so residents can be assigned to their own home.
        /// </summary>
        private static List<Building_Bed> SpawnBedsFor(Map map, Faction faction, IntVec3 pos, PrefabDef prefab, int count, int couples)
        {
            List<Building_Bed> beds = new List<Building_Bed>();
            if (prefab == null || faction == null)
            {
                return beds;
            }
            CellRect rect = CellRect.CenteredOn(pos, prefab.size.x, prefab.size.z).ContractedBy(1);
            int capacity = 0;
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing is Building_Bed existing && !beds.Contains(existing))
                    {
                        if (existing.Faction != faction)
                        {
                            existing.SetFaction(faction);
                        }
                        beds.Add(existing);
                        capacity += existing.SleepingSlotsCount;
                    }
                }
            }
            ThingDef doubleBedDef = DefDatabase<ThingDef>.GetNamedSilentFail("DoubleBed");
            int doublesToPlace = doubleBedDef != null ? couples : 0;
            foreach (IntVec3 cell in rect)
            {
                if (doublesToPlace <= 0 && capacity >= count)
                {
                    break;
                }
                if (!cell.InBounds(map) || !cell.Roofed(map))
                {
                    continue;
                }
                ThingDef bedDef = doublesToPlace > 0 ? doubleBedDef : ThingDefOf.Bed;
                if (!GenConstruct.CanPlaceBlueprintAt(bedDef, cell, Rot4.South, map).Accepted)
                {
                    continue;
                }
                Building_Bed bed = (Building_Bed)ThingMaker.MakeThing(bedDef, ThingDefOf.WoodLog);
                bed.SetFaction(faction);
                GenSpawn.Spawn(bed, cell, map, Rot4.South);
                beds.Add(bed);
                capacity += bed.SleepingSlotsCount;
                if (doublesToPlace > 0)
                {
                    doublesToPlace--;
                }
            }
            return beds;
        }

        /// <summary>
        /// Bed OWNERSHIP ties each villager to their own house: rest-seeking
        /// always prefers the owned bed, so Haldor no longer crosses the
        /// square to crash in the Root farmhouse just because it is a few
        /// cells closer to the forge yard at dusk. Couples claim the double
        /// bed together; everyone else takes singles.
        /// </summary>
        private static void AssignBeds(List<Pawn> pawns, List<Building_Bed> beds)
        {
            HashSet<Pawn> bedded = new HashSet<Pawn>();
            // Couples first, onto the widest unowned bed.
            foreach (Pawn pawn in pawns)
            {
                if (bedded.Contains(pawn) || pawn.ownership == null || pawn.relations == null)
                {
                    continue;
                }
                Pawn mate = null;
                foreach (Pawn other in pawns)
                {
                    if (other != pawn && !bedded.Contains(other)
                        && pawn.relations.DirectRelationExists(PawnRelationDefOf.Spouse, other))
                    {
                        mate = other;
                        break;
                    }
                }
                if (mate?.ownership == null)
                {
                    continue;
                }
                foreach (Building_Bed bed in beds)
                {
                    if (bed.SleepingSlotsCount >= 2 && bed.OwnersForReading.Count == 0)
                    {
                        pawn.ownership.ClaimBedIfNonMedical(bed);
                        mate.ownership.ClaimBedIfNonMedical(bed);
                        bedded.Add(pawn);
                        bedded.Add(mate);
                        break;
                    }
                }
            }
            // Everyone else: prefer single beds, spill onto free double slots.
            foreach (Pawn pawn in pawns)
            {
                if (bedded.Contains(pawn) || pawn.ownership == null)
                {
                    continue;
                }
                Building_Bed pick = null;
                foreach (Building_Bed bed in beds)
                {
                    if (bed.OwnersForReading.Count >= bed.SleepingSlotsCount)
                    {
                        continue;
                    }
                    if (pick == null || (bed.SleepingSlotsCount < pick.SleepingSlotsCount))
                    {
                        pick = bed;
                    }
                }
                if (pick != null)
                {
                    pawn.ownership.ClaimBedIfNonMedical(pick);
                    bedded.Add(pawn);
                }
            }
        }

        /// <summary>A standable roofed cell inside the building; the spawn point itself when there is none (open camps).</summary>
        private static IntVec3 InteriorCell(Map map, IntVec3 pos, PrefabDef prefab)
        {
            if (prefab != null)
            {
                CellRect rect = CellRect.CenteredOn(pos, prefab.size.x, prefab.size.z).ContractedBy(1);
                foreach (IntVec3 cell in rect)
                {
                    if (cell.InBounds(map) && cell.Standable(map) && cell.Roofed(map))
                    {
                        return cell;
                    }
                }
            }
            return pos;
        }

        internal static Faction VillagerFaction()
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

    /// <summary>
    /// Remembers each village's background households (site + building ->
    /// the same pawns, every visit), the same way named NPCs persist: hard
    /// references scribed by Reference, with off-map pawns kept alive as
    /// KeepForever world pawns so world-pawn GC never eats a face between
    /// visits. Dead pawns are pruned on read and never replaced.
    /// </summary>
    public class TSC_FillerRegistry : RimWorld.Planet.WorldComponent
    {
        private Dictionary<string, TSC_FillerHousehold> households = new Dictionary<string, TSC_FillerHousehold>();

        public TSC_FillerRegistry(RimWorld.Planet.World world) : base(world)
        {
        }

        public static TSC_FillerRegistry Current => Find.World.GetComponent<TSC_FillerRegistry>();

        private static string KeyFor(Map map, int buildingIndex) =>
            $"{map.Parent?.ID ?? -1}_{buildingIndex}";

        public int HouseholdSize(Map map, int buildingIndex)
        {
            List<Pawn> household = GetHousehold(map, buildingIndex);
            return household?.Count ?? -1;
        }

        public List<Pawn> GetHousehold(Map map, int buildingIndex)
        {
            if (!households.TryGetValue(KeyFor(map, buildingIndex), out TSC_FillerHousehold household))
            {
                return null;
            }
            household.pawns.RemoveAll(p => p == null || p.Dead || p.Destroyed);
            return household.pawns;
        }

        public void SetHousehold(Map map, int buildingIndex, List<Pawn> pawns)
        {
            households[KeyFor(map, buildingIndex)] = new TSC_FillerHousehold { pawns = pawns };
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (Find.TickManager.TicksGame % 250 != 0)
            {
                return;
            }
            // Keep-alive sweep: an off-map filler must be a KeepForever world
            // pawn or the GC treats them as an unimportant stranger.
            foreach (TSC_FillerHousehold household in households.Values)
            {
                foreach (Pawn pawn in household.pawns)
                {
                    if (pawn != null && !pawn.Dead && !pawn.Destroyed && !pawn.Spawned
                        && !Find.WorldPawns.Contains(pawn))
                    {
                        Find.WorldPawns.PassToWorld(pawn, RimWorld.Planet.PawnDiscardDecideMode.KeepForever);
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref households, "households", LookMode.Value, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && households == null)
            {
                households = new Dictionary<string, TSC_FillerHousehold>();
            }
        }
    }

    public class TSC_FillerHousehold : IExposable
    {
        public List<Pawn> pawns = new List<Pawn>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pawns == null)
                {
                    pawns = new List<Pawn>();
                }
                pawns.RemoveAll(p => p == null);
            }
        }
    }

    public class VillageBuilding
    {
        public PrefabDef prefab;
        public NamedNpcDef resident;
        /// <summary>Whole households (the farm families): all spawn at this building and share its routine.</summary>
        public List<NamedNpcDef> residents = new List<NamedNpcDef>();
        /// <summary>
        /// Background villagers at this building: randomly generated, no
        /// dialogue, re-rolled each visit. They share the household's beds,
        /// routine and food, and exist so a village with a reeve and a
        /// smith also has people worth reeving and smithing FOR.
        /// </summary>
        public IntRange fillerCount = IntRange.Zero;
        /// <summary>Lay a worked potato field south of the building; residents work it by day.</summary>
        public bool cropField;
        /// <summary>Yard animals kept at this building (chickens, the goat) - tame, villager faction, wandering the yard.</summary>
        public List<LivestockEntry> livestock = new List<LivestockEntry>();
        /// <summary>A single yard fixture spawned beside the building (THE woodpile at the Root farm).</summary>
        public ThingDef yardProp;
    }

    public class LivestockEntry
    {
        public PawnKindDef kind;
        public IntRange count = new IntRange(2, 4);
    }
}
