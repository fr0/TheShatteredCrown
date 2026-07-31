using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// Act 4's stage manager, on the monastery surface map:
    ///  - the doorkeeper's ghost greets the first colonist inside the walls;
    ///  - the writing count is watched, and at four of six pieces the
    ///    investigation quest completes (TSC_MonWritingsRead);
    ///  - after that, the abbot's ghost makes his stand at the chapel altar
    ///    the next time somebody approaches it - his scene carries the
    ///    judgment and the rite.
    /// </summary>
    public class MapComponent_TSC_MonasteryGhost : MapComponent
    {
        private const int Interval = 60;
        private const float AltarRadius = 7.9f;
        private bool checkedMap;
        private bool isMonastery;
        private CellRect walls = CellRect.Empty;
        // The curtain's exact interior, stored by GenStep_TSC_Curtain at
        // generation. Scribed as four ints; the wall-scan fallback only
        // exists for maps generated before this was recorded.
        private int curtainMinX = -1;
        private int curtainMinZ = -1;
        private int curtainWidth;
        private int curtainHeight;

        public void SetCurtain(CellRect inner)
        {
            curtainMinX = inner.minX;
            curtainMinZ = inner.minZ;
            curtainWidth = inner.Width;
            curtainHeight = inner.Height;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref curtainMinX, "curtainMinX", -1);
            Scribe_Values.Look(ref curtainMinZ, "curtainMinZ", -1);
            Scribe_Values.Look(ref curtainWidth, "curtainWidth");
            Scribe_Values.Look(ref curtainHeight, "curtainHeight");
        }

        private static readonly string[] WritingFlags =
        {
            "TSC_MonWriting_Confessions",
            "TSC_MonWriting_Infirmary",
            "TSC_MonWriting_CellWall",
            "TSC_MonWriting_AbbotLetter",
            "TSC_MonWriting_Tallies",
            "TSC_MonWriting_Slates",
        };
        public const int WritingsNeeded = 4;

        public MapComponent_TSC_MonasteryGhost(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            if (!checkedMap)
            {
                checkedMap = true;
                if (map.Parent is RimWorld.Planet.Site site)
                {
                    for (int i = 0; i < site.parts.Count; i++)
                    {
                        if (site.parts[i].def?.defName == "TSC_SilentMonastery")
                        {
                            isMonastery = true;
                            break;
                        }
                    }
                }
            }
            if (!isMonastery)
            {
                return;
            }
            HealGraveRow();
            HealStable();
            HealChests();
            WatchWritings();
            if (Find.WindowStack.WindowOfType<Dialog_Conversation>() != null)
            {
                return;
            }
            TryGreet();
            TryAbbot();
        }

        private bool gravesHealed;
        private bool stableHealed;
        private bool chestsHealed;

        /// <summary>
        /// "Old supply cache" is ruin salvage and this house is not a ruin:
        /// monasteries generated before the cellarer's chest existed get
        /// their UNOPENED caches re-minted in place. Opened ones stay -
        /// their loot already spawned.
        /// </summary>
        private void HealChests()
        {
            if (chestsHealed)
            {
                return;
            }
            chestsHealed = true;
            ThingDef oldDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_SupplyCache");
            ThingDef newDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_MonasteryChest");
            if (oldDef == null || newDef == null)
            {
                return;
            }
            List<Thing> caches = new List<Thing>(map.listerThings.ThingsOfDef(oldDef));
            foreach (Thing cache in caches)
            {
                // Loot stocks at OPEN time, so contents cannot tell an
                // opened crate from a waiting one. The seal can: the crate
                // is only openable once its check is spent, so unspent
                // means untouched.
                Comp_TSC_CheckSpot seal = cache.TryGetComp<Comp_TSC_CheckSpot>();
                if (seal != null && seal.Spent)
                {
                    continue;
                }
                IntVec3 at = cache.Position;
                cache.Destroy(DestroyMode.Vanish);
                GenSpawn.Spawn(ThingMaker.MakeThing(newDef, GenStuff.DefaultStuffFor(newDef)), at, map);
            }
        }

        /// <summary>
        /// Monasteries generated before the exterior stable existed get it
        /// built on load. The rear door is found by its signature: a wooden
        /// door set between wall segments with open sky on both sides -
        /// interior doors are roofed on at least one side.
        /// </summary>
        private void HealStable()
        {
            if (stableHealed)
            {
                return;
            }
            stableHealed = true;
            Building_Door rearDoor = null;
            foreach (Building building in map.listerBuildings.allBuildingsNonColonist)
            {
                if (!(building is Building_Door door) || door.Stuff != ThingDefOf.WoodLog)
                {
                    continue;
                }
                IntVec3 pos = door.Position;
                bool walled = (pos + IntVec3.East).GetEdifice(map) is Building e1 && e1.def == ThingDefOf.Wall
                    && (pos + IntVec3.West).GetEdifice(map) is Building e2 && e2.def == ThingDefOf.Wall;
                bool openBothSides = !(pos + IntVec3.North).Roofed(map) && !(pos + IntVec3.South).Roofed(map);
                if (walled && openBothSides && (rearDoor == null || pos.z > rearDoor.Position.z))
                {
                    rearDoor = door;
                }
            }
            if (rearDoor == null)
            {
                return;
            }
            // Already built? Any wood wall in the stable's footprint says so.
            CellRect rect = new CellRect(rearDoor.Position.x + 2, rearDoor.Position.z + 2, 6, 4).ClipInsideMap(map);
            foreach (IntVec3 cell in rect)
            {
                Building edifice = cell.GetEdifice(map);
                if (edifice != null && edifice.def == ThingDefOf.Wall && edifice.Stuff == ThingDefOf.WoodLog)
                {
                    return;
                }
            }
            GenStep_TSC_Curtain.BuildStable(map, rearDoor.Position);
        }

        /// <summary>
        /// A monastery generated before the grave row existed shows one
        /// mound where the text counts seven. Same cure as ever: heal the
        /// saved map on load.
        /// </summary>
        private void HealGraveRow()
        {
            if (gravesHealed)
            {
                return;
            }
            gravesHealed = true;
            ThingDef mound = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_PilgrimMound");
            ThingDef spotDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_MonSpot_PilgrimGraves");
            if (mound == null || spotDef == null
                || map.listerThings.ThingsOfDef(mound).Count > 0)
            {
                return;
            }
            List<Thing> spots = map.listerThings.ThingsOfDef(spotDef);
            if (spots.Count > 0)
            {
                GenStep_TSC_OutsideWallSpot.PlaceRow(map, spots[0].Position, mound, 6);
            }
        }

        /// <summary>
        /// Four of six pieces is enough to know the shape of it. The count
        /// lives here rather than in a quest part because the flags are set
        /// on three different maps (surface and two floors below), and this
        /// component outlives all their visits.
        /// </summary>
        private void WatchWritings()
        {
            if (DialogueStateManager.Current.IsSet("TSC_MonWritingsRead"))
            {
                return;
            }
            int found = 0;
            for (int i = 0; i < WritingFlags.Length; i++)
            {
                if (DialogueStateManager.Current.IsSet(WritingFlags[i]))
                {
                    found++;
                }
            }
            if (found < WritingsNeeded)
            {
                return;
            }
            DialogueStateManager.Current.Set("TSC_MonWritingsRead");
            TSC_QuestSignals.Send("TSC_Act4_QuietBrothers", "TSC_MonWritingsRead");
        }

        private void TryGreet()
        {
            if (DialogueStateManager.Current.IsSet("TSC_MetMonkGhost"))
            {
                return;
            }
            CellRect interior;
            if (curtainWidth > 0)
            {
                interior = new CellRect(curtainMinX, curtainMinZ, curtainWidth, curtainHeight).ContractedBy(1);
            }
            else
            {
                if (walls.Width <= 0)
                {
                    walls = WallBounds();
                    if (walls.Width <= 0)
                    {
                        return;
                    }
                }
                interior = walls.ContractedBy(2);
            }
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.Downed || !interior.Contains(colonist.Position))
                {
                    continue;
                }
                DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_MonkGhost");
                if (def == null)
                {
                    return;
                }
                DialogueStateManager.Current.Set("TSC_MetMonkGhost");
                Find.WindowStack.Add(new Dialog_Conversation(def, colonist, colonist));
                return;
            }
        }

        /// <summary>
        /// The abbot does not wander and does not greet. Once the writings
        /// have told the party what he did, he is waiting at his altar for
        /// whoever comes to say so.
        /// </summary>
        private void TryAbbot()
        {
            if (!DialogueStateManager.Current.IsSet("TSC_MonWritingsRead")
                || DialogueStateManager.Current.IsSet("TSC_MetAbbotGhost"))
            {
                return;
            }
            Thing altar = null;
            ThingDef altarDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_ShrineAltar");
            if (altarDef != null)
            {
                List<Thing> altars = map.listerThings.ThingsOfDef(altarDef);
                if (altars.Count > 0)
                {
                    altar = altars[0];
                }
            }
            if (altar == null)
            {
                // A layout roll without its altar: the abbot stands in the
                // middle of the house instead, and the scene still happens.
                altar = TSC_KeepCellar.FindStair(map);
            }
            IntVec3 anchor = altar?.Position ?? map.Center;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.Downed || !colonist.Position.InHorDistOf(anchor, AltarRadius))
                {
                    continue;
                }
                DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_AbbotGhost");
                if (def == null)
                {
                    return;
                }
                DialogueStateManager.Current.Set("TSC_MetAbbotGhost");
                if (altar != null)
                {
                    CameraJumper.TryJump(altar);
                }
                Find.WindowStack.Add(new Dialog_Conversation(def, colonist, colonist));
                return;
            }
        }

        private CellRect WallBounds()
        {
            List<Thing> wallThings = map.listerThings.ThingsOfDef(ThingDefOf.Wall);
            if (wallThings.Count == 0)
            {
                return CellRect.Empty;
            }
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minZ = int.MaxValue;
            int maxZ = int.MinValue;
            foreach (Thing wall in wallThings)
            {
                IntVec3 pos = wall.Position;
                minX = UnityEngine.Mathf.Min(minX, pos.x);
                maxX = UnityEngine.Mathf.Max(maxX, pos.x);
                minZ = UnityEngine.Mathf.Min(minZ, pos.z);
                maxZ = UnityEngine.Mathf.Max(maxZ, pos.z);
            }
            return CellRect.FromLimits(minX, minZ, maxX, maxZ);
        }
    }

    /// <summary>
    /// Save-heal for undercells generated while the reliquary inference was
    /// wrong ("fixed depth, level 3" also matched the monastery): Act 2's
    /// finale - Aldis, his altar, the Kingsblade chest, and a DUPLICATE
    /// second shard - spawned into Act 4's bottom floor. Runs once on any
    /// map containing the undercell door and clears the intruders. Aldis is
    /// a named story pawn and goes back to the world, not to the void.
    /// </summary>
    public class MapComponent_TSC_UndercellHeal : MapComponent
    {
        private bool done;

        public MapComponent_TSC_UndercellHeal(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (done || Find.TickManager.TicksGame % 250 != 0)
            {
                return;
            }
            done = true;
            ThingDef doorDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_UndercellDoor");
            if (doorDef == null || map.listerThings.ThingsOfDef(doorDef).Count == 0)
            {
                return; // not an undercell
            }
            // Aldis, mistakenly re-summoned four maps from his cellar.
            NamedNpcDef cantor = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Cantor");
            Pawn aldis = cantor == null ? null : DialogueStateManager.Current.GetNamedNpcIfExists(cantor);
            if (aldis != null && aldis.Map == map)
            {
                aldis.GetLord()?.Notify_PawnLost(aldis, PawnLostCondition.ExitedMap);
                if (aldis.Spawned)
                {
                    aldis.DeSpawn();
                }
                if (!Find.WorldPawns.Contains(aldis))
                {
                    Find.WorldPawns.PassToWorld(aldis, RimWorld.Planet.PawnDiscardDecideMode.KeepForever);
                }
                Log.Message("[The Shattered Crown] Aldis had been mistakenly re-summoned to the monastery undercell; he has gone back to his own story.");
            }
            RemoveAll("TSC_ReliquaryAltar");
            RemoveAll("TSC_IronboundChest_Kingsblade");
            // The duplicate shard: only when the real one is already in the
            // party's hands - never delete the player's only copy.
            ThingDef shardDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard_Reliquary");
            if (shardDef != null && PartyHoldsElsewhere(shardDef))
            {
                RemoveAll("TSC_CrownShard_Reliquary");
            }
        }

        private void RemoveAll(string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return;
            }
            foreach (Thing thing in new List<Thing>(map.listerThings.ThingsOfDef(def)))
            {
                if (thing.Spawned)
                {
                    thing.DeSpawn();
                }
                if (!thing.Destroyed && thing.def.destroyable)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static bool PartyHoldsElsewhere(ThingDef def)
        {
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn.inventory?.innerContainer != null && pawn.inventory.innerContainer.Contains(def))
                {
                    return true;
                }
                if (pawn.carryTracker?.CarriedThing?.def == def)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public class TSC_FurnitureSpot
    {
        public ThingDef def;
        /// <summary>Furniture the spot stands beside; first anchor kind found wins.</summary>
        public List<ThingDef> anchors = new List<ThingDef>();
    }

    /// <summary>
    /// Places check spots BESIDE the furniture they belong to: the letter at
    /// a copyist's desk, the ledgers by shelving, a written wall over a bunk.
    /// A journal lying in the middle of a floor reads as set dressing gone
    /// astray; the same journal beside a desk reads as somebody's. Falls back
    /// to a random interior cell when the layout rolled no anchor.
    /// </summary>
    public class GenStep_TSC_PlaceAtFurniture : GenStep
    {
        public List<TSC_FurnitureSpot> spots = new List<TSC_FurnitureSpot>();

        public override int SeedPart => 402173958;

        public override void Generate(Map map, GenStepParams parms)
        {
            foreach (TSC_FurnitureSpot spot in spots)
            {
                if (spot.def == null)
                {
                    continue;
                }
                IntVec3 cell = FindAnchoredCell(map, spot);
                if (!cell.IsValid)
                {
                    cell = FindFallbackCell(map);
                }
                if (cell.IsValid)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(spot.def), cell, map);
                }
            }
        }

        private static IntVec3 FindAnchoredCell(Map map, TSC_FurnitureSpot spot)
        {
            List<Thing> anchors = new List<Thing>();
            foreach (ThingDef anchorDef in spot.anchors)
            {
                if (anchorDef != null)
                {
                    anchors.AddRange(map.listerThings.ThingsOfDef(anchorDef));
                }
            }
            anchors.Shuffle();
            foreach (Thing anchor in anchors)
            {
                foreach (IntVec3 cell in GenAdj.CellsAdjacentCardinal(anchor))
                {
                    if (cell.InBounds(map) && cell.Standable(map)
                        && cell.GetEdifice(map) == null && cell.GetFirstItem(map) == null
                        && cell.GetFirstBuilding(map) == null && cell.Roofed(map))
                    {
                        return cell;
                    }
                }
            }
            return IntVec3.Invalid;
        }

        private static IntVec3 FindFallbackCell(Map map)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                IntVec3 candidate = CellFinder.RandomNotEdgeCell(20, map);
                if (candidate.Standable(map) && candidate.GetEdifice(map) == null
                    && candidate.GetFirstBuilding(map) == null)
                {
                    return candidate;
                }
            }
            return IntVec3.Invalid;
        }
    }

    /// <summary>
    /// Places one thing OUTSIDE the curtain wall, near the gate side: the
    /// pilgrim graves, dug by the people who knocked and waited and were
    /// buried by whoever waited next.
    /// </summary>
    public class GenStep_TSC_OutsideWallSpot : GenStep
    {
        public ThingDef thingDef;
        /// <summary>Decorative things continuing the anchor in a row (the other six mounds).</summary>
        public ThingDef rowDef;
        public int rowCount;

        public override int SeedPart => 719284355;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (thingDef == null)
            {
                return;
            }
            List<Thing> wallThings = map.listerThings.ThingsOfDef(ThingDefOf.Wall);
            if (wallThings.Count == 0)
            {
                return;
            }
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minZ = int.MaxValue;
            foreach (Thing wall in wallThings)
            {
                minX = UnityEngine.Mathf.Min(minX, wall.Position.x);
                maxX = UnityEngine.Mathf.Max(maxX, wall.Position.x);
                minZ = UnityEngine.Mathf.Min(minZ, wall.Position.z);
            }
            int centerX = (minX + maxX) / 2;
            for (int dz = 3; dz <= 8; dz++)
            {
                for (int dx = 0; dx <= 12; dx++)
                {
                    foreach (int sign in new[] { 1, -1 })
                    {
                        IntVec3 cell = new IntVec3(centerX + dx * sign, 0, minZ - dz);
                        if (cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null
                            && !cell.GetTerrain(map).IsWater)
                        {
                            GenSpawn.Spawn(ThingMaker.MakeThing(thingDef), cell, map);
                            PlaceRow(map, cell);
                            return;
                        }
                        if (dx == 0)
                        {
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The rest of the row, spaced two cells apart, east then west of the
        /// anchor - the text promises seven mounds, so the map shows seven.
        /// </summary>
        private void PlaceRow(Map map, IntVec3 anchor)
        {
            if (rowDef == null || rowCount <= 0)
            {
                return;
            }
            PlaceRow(map, anchor, rowDef, rowCount);
        }

        /// <summary>Shared with the save-heal: maps generated before the row existed get it on load.</summary>
        internal static void PlaceRow(Map map, IntVec3 anchor, ThingDef rowDef, int rowCount)
        {
            int placed = 0;
            for (int step = 1; step <= rowCount * 2 && placed < rowCount; step++)
            {
                foreach (int sign in new[] { 1, -1 })
                {
                    if (placed >= rowCount)
                    {
                        break;
                    }
                    IntVec3 cell = anchor + new IntVec3(step * 2 * sign, 0, 0);
                    if (cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null
                        && !cell.GetTerrain(map).IsWater && cell.GetFirstBuilding(map) == null)
                    {
                        GenSpawn.Spawn(ThingMaker.MakeThing(rowDef), cell, map);
                        placed++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The undercell chamber: the vision's closed door, made real. A sealed
    /// stone room at the far end of the bottom floor holding the two biers
    /// and the fourth shard. The door does not open until the rite is
    /// spoken (TSC_MonasteryVowBroken) - the HOUSE is the lock.
    /// </summary>
    public class GenStep_TSC_UndercellChamber : GenStep
    {
        public override int SeedPart => 883412906;

        public override void Generate(Map map, GenStepParams parms)
        {
            Thing wayUp = TSC_KeepCellar.FindWayUp(map);
            IntVec3 anchor = wayUp?.Position ?? map.Center;
            IntVec3 center = IntVec3.Invalid;
            float best = -1f;
            foreach (IntVec3 cell in map.AllCells)
            {
                if (cell.DistanceToEdge(map) < 6 || !cell.Standable(map))
                {
                    continue;
                }
                float dist = cell.DistanceTo(anchor);
                if (dist > best)
                {
                    best = dist;
                    center = cell;
                }
            }
            if (!center.IsValid)
            {
                Log.Warning("[The Shattered Crown] Undercell generated with nowhere to put the chamber.");
                return;
            }
            CellRect room = CellRect.CenteredOn(center, 7, 7).ClipInsideMap(map);
            foreach (IntVec3 cell in room)
            {
                GenStep_TSC_CellarLevel.CarveCell(map, cell);
            }
            // The door faces the way in: the edge cell nearest the anchor.
            IntVec3 doorCell = IntVec3.Invalid;
            float doorBest = float.MaxValue;
            foreach (IntVec3 cell in room.EdgeCells)
            {
                bool corner = (cell.x == room.minX || cell.x == room.maxX)
                    && (cell.z == room.minZ || cell.z == room.maxZ);
                if (corner)
                {
                    continue;
                }
                float dist = cell.DistanceTo(anchor);
                if (dist < doorBest)
                {
                    doorBest = dist;
                    doorCell = cell;
                }
            }
            ThingDef granite = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksGranite");
            foreach (IntVec3 cell in room.EdgeCells)
            {
                if (cell == doorCell)
                {
                    continue;
                }
                if (cell.GetEdifice(map) == null)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, granite), cell, map);
                }
            }
            ThingDef doorDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_UndercellDoor");
            if (doorDef != null && doorCell.IsValid)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(doorDef), doorCell, map);
            }
            ThingDef shard = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard_Undercell");
            if (shard != null)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(shard), room.CenterCell, map);
            }
            ThingDef biers = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_MonSpot_TwoBiers");
            if (biers != null)
            {
                IntVec3 bierCell = room.CenterCell + IntVec3.North;
                if (room.ContractedBy(1).Contains(bierCell))
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(biers), bierCell, map);
                }
            }
            foreach (IntVec3 cell in room)
            {
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
            }
        }
    }

    /// <summary>
    /// The closed door. It has no lock to pick and no hinges to force: the
    /// vow is what holds it, and it opens the moment the truth of the house
    /// has been said out loud in the chapel - and not one moment before.
    /// </summary>
    public class Building_TSC_UndercellDoor : Building
    {
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }
            if (selPawn == null || !selPawn.IsColonistPlayerControlled)
            {
                yield break;
            }
            if (!DialogueStateManager.Current.IsSet("TSC_MonasteryVowBroken"))
            {
                yield return new FloatMenuOption(
                    "Open the door (it does not move: the house is still holding it)", null);
                yield break;
            }
            FloatMenuOption open = new FloatMenuOption("Open the door", delegate
            {
                JobDef job = DefDatabase<JobDef>.GetNamedSilentFail("TSC_OpenUndercellDoor");
                if (job != null)
                {
                    selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(job, this), JobTag.Misc);
                }
            });
            yield return FloatMenuUtility.DecoratePrioritizedTask(open, selPawn, this);
        }

        public override string GetInspectString()
        {
            string state = DialogueStateManager.Current.IsSet("TSC_MonasteryVowBroken")
                ? "It stands ajar by a finger's width, waiting."
                : "It is shut the way a mouth is shut.";
            return base.GetInspectString().NullOrEmpty() ? state : base.GetInspectString() + "\n" + state;
        }
    }

    public class JobDriver_TSC_OpenUndercellDoor : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil wait = Toils_General.Wait(90);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;
            Toil open = ToilMaker.MakeToil("TSC_OpenUndercellDoor");
            open.initAction = delegate
            {
                Thing door = job.GetTarget(TargetIndex.A).Thing;
                if (door == null || door.Destroyed)
                {
                    return;
                }
                Map onMap = door.Map;
                IntVec3 at = door.Position;
                door.Destroy(DestroyMode.Vanish);
                FilthMaker.TryMakeFilth(at, onMap, ThingDefOf.Filth_RubbleBuilding);
                Messages.Message("The door swings open without a sound, into the dark where the house kept its heart.",
                    new TargetInfo(at, onMap), MessageTypeDefOf.NeutralEvent, historical: false);
            };
            open.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return open;
        }
    }
}
