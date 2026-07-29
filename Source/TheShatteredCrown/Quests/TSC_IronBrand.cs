using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// The Iron Brand's vault: runs after GenStep_AncientRuins has built the
    /// TSC_Castle layout. Finds the deepest enclosed room of the keep and
    /// stages the hoard there - the third crown shard buried under coin,
    /// plate and filled chests - with the Bandit Baron and his bodyguard
    /// posted on top of it. The Baron defends the hoard rather than joining
    /// wall patrols: the vision promised a lord sitting on his treasure,
    /// and that is what the party finds.
    /// </summary>
    public class GenStep_TSC_IronBrandHoard : GenStep
    {
        public IntRange silverStacks = new IntRange(3, 4);
        public IntRange silverPerStack = new IntRange(180, 340);
        public IntRange goldAmount = new IntRange(15, 35);
        public int lootChests = 2;

        public override int SeedPart => 918273645;

        public override void Generate(Map map, GenStepParams parms)
        {
            IntVec3 vault = FindVaultCell(map);
            if (!vault.IsValid)
            {
                vault = CellFinder.RandomNotEdgeCell(20, map);
            }

            SpawnAt(map, vault, "TSC_CrownShard_Hoard", 1);
            for (int i = 0; i < silverStacks.RandomInRange; i++)
            {
                SpawnAt(map, vault, "Silver", silverPerStack.RandomInRange);
            }
            SpawnAt(map, vault, "Gold", goldAmount.RandomInRange);
            for (int i = 0; i < lootChests; i++)
            {
                SpawnChest(map, vault);
            }

            Faction brand = TSC_BanditFactionUtility.Get();
            if (brand == null)
            {
                return;
            }
            List<Pawn> court = new List<Pawn>();
            PawnKindDef baronKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_IronBrandBaron");
            PawnKindDef guardKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand");
            if (baronKind != null)
            {
                court.Add(SpawnPawn(map, vault, baronKind, brand));
            }
            if (guardKind != null)
            {
                court.Add(SpawnPawn(map, vault, guardKind, brand));
                court.Add(SpawnPawn(map, vault, guardKind, brand));
            }
            court.RemoveAll(p => p == null);
            if (court.Count > 0)
            {
                LordMaker.MakeNewLord(brand, new LordJob_DefendPoint(vault), map, court);
            }
        }

        private static Pawn SpawnPawn(Map map, IntVec3 near, PawnKindDef kind, Faction faction)
        {
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(near, map, 4);
            if (!cell.IsValid)
            {
                return null;
            }
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, faction, PawnGenerationContext.NonPlayer, map.Tile,
                mustBeCapableOfViolence: true));
            return GenSpawn.Spawn(pawn, cell, map) as Pawn;
        }

        private static void SpawnAt(Map map, IntVec3 near, string defName, int count)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return;
            }
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            GenPlace.TryPlaceThing(thing, CellFinder.RandomClosewalkCellNear(near, map, 3),
                map, ThingPlaceMode.Near);
        }

        private static void SpawnChest(Map map, IntVec3 near)
        {
            ThingDef chestDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_LootChest");
            if (chestDef == null)
            {
                return;
            }
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(near, map, 4);
            if (!cell.Standable(map))
            {
                return;
            }
            if (!(GenSpawn.Spawn(ThingMaker.MakeThing(chestDef, GenStuff.DefaultStuffFor(chestDef)),
                cell, map) is Building_Casket casket))
            {
                return;
            }
            // TSC_LootChest is an empty shell; the placer stocks it.
            ThingSetMakerDef lootTable = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail("TSC_Loot_CommonCache");
            if (lootTable != null && casket.GetDirectlyHeldThings().Count == 0)
            {
                foreach (Thing item in lootTable.root.Generate())
                {
                    casket.GetDirectlyHeldThings().TryAdd(item);
                }
            }
        }

        /// <summary>
        /// The vault: deepest enclosed, standable, reachable room cell of
        /// the keep (same scoring as GenStep_TSC_PlaceInStructure - the back
        /// room, never the gatehouse).
        /// </summary>
        private static IntVec3 FindVaultCell(Map map)
        {
            // Rooms come from the region grid, which is commonly disabled
            // mid-generation: without this the whole keep reads as outdoors
            // and the hoard lands in the yard.
            GenStep_TSC_PlaceInStructure.EnsureRooms(map);
            IntVec3 best = IntVec3.Invalid;
            float bestScore = -1f;
            TraverseParms walk = TraverseParms.For(TraverseMode.PassDoors);
            foreach (IntVec3 cell in map.AllCells)
            {
                if (!cell.Standable(map))
                {
                    continue;
                }
                Room room = cell.GetRoom(map);
                if (room == null || room.PsychologicallyOutdoors || room.CellCount < 9)
                {
                    continue;
                }
                if (!map.reachability.CanReachMapEdge(cell, walk))
                {
                    continue;
                }
                float score = cell.DistanceToEdge(map) + (cell.Roofed(map) ? 12f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell;
                }
            }
            return best;
        }
    }
}
