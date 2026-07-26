using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// The sanguophage crypt under the old survey gallery: Act 1's finale.
    /// Surface site reuses GenStep_TSC_BarrowSurface (gallery mouth portal);
    /// this genstep builds the underground half - the dressed-stone crypt,
    /// the sixty-year-old BRICKWORK sealing the survey tunnel (breached
    /// already if Old Wick walked in ahead of the party), Old Wick's empty
    /// coffin, and the warm door at the end of a single-file passage - the
    /// burial chamber behind it holds the sleeper and the shard.
    /// The crypt spawns QUIET on every route: the fight is the sleeper
    /// waking (with its risen) when the shard is lifted, not pre-spawned
    /// tenants - gen-time shamblers used to beat the sleeping elder awake
    /// before the party cleared the tunnel.
    /// Routes (see village_oldwick.agd): trust = brickwork intact; journal
    /// (TSC_WickConfessed + TSC_WickAtCrypt) = brickwork open, Old Wick
    /// waits inside by his coffin.
    /// </summary>
    public class GenStep_TSC_CryptInterior : GenStep
    {
        public LayoutDef layoutDef;
        public int size = 29;

        public override int SeedPart => 771290463;

        public override void Generate(Map map, GenStepParams parms)
        {
            bool wickInside = DialogueStateManager.Current.IsSet("TSC_WickAtCrypt");
            CellRect rect = CellRect.CenteredOn(map.Center, size, size).ClipInsideMap(map).ContractedBy(1);
            foreach (IntVec3 cell in rect)
            {
                CarveCell(map, cell);
            }
            if (layoutDef != null)
            {
                StructureGenParams genParms = new StructureGenParams
                {
                    size = new IntVec2(rect.Width, rect.Height),
                };
                LayoutWorker worker = layoutDef.Worker;
                LayoutStructureSketch sketch = worker.GenerateStructureSketch(genParms);
                // The crypt is QUIET until the shard is lifted: the fight is
                // the sleeper and its risen (MapComponent_TSC_ElderSleeper),
                // not pre-spawned tenants. Gen-time shamblers used to scatter
                // around the coffin - within reach of the burial chamber -
                // and beat the sleeping elder awake before the party cleared
                // the tunnel. No threat points, and the layout's own dormant
                // insects are cleared for the same reason.
                worker.Spawn(sketch, map, rect.Min, threatPoints: null);
                ClearDormantInsects(map);
            }
            foreach (IntVec3 cell in rect)
            {
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
            }

            IntVec3 breach = TunnelToBrickwork(map, rect, wickInside);
            IntVec3 coffinCell = FarthestStandable(map, rect, breach);
            SpawnWicksCoffin(map, coffinCell);
            IntVec3 doorCell = PlaceWarmDoor(map, rect, breach, coffinCell);
            map.GetComponent<MapComponent_TSC_WarmDoor>()?.SetDoorPos(doorCell);
            // The finale: Old Wick appears when the party reaches his coffin
            // (both routes; see MapComponent_TSC_CryptFinale).
            map.GetComponent<MapComponent_TSC_CryptFinale>()?.SetCoffinPos(coffinCell);

            // The guarantee: rope to coffin must be WALKABLE (doors allowed,
            // no mining). Layout quirks - pruned rooms, a wall seam behind
            // the breach, an unlucky tunnel line - get a rough passage carved
            // straight through rather than a softlocked finale.
            EnsurePath(map, MapGenerator.PlayerStartSpot, breach);
            EnsurePath(map, breach, coffinCell);
        }

        /// <summary>
        /// If b cannot be walked to from a (doors count, mining does not),
        /// carve a 2-wide L-shaped passage between them. Crude on purpose:
        /// it reads as one more collapse in a hill full of them, and it only
        /// ever runs when generation produced a sealed route.
        /// </summary>
        private static void EnsurePath(Map map, IntVec3 a, IntVec3 b)
        {
            // Touch, not OnCell: the coffin endpoint IS an impassable
            // sarcophagus - standing beside it is arrival.
            if (!a.IsValid || !b.IsValid
                || map.reachability.CanReach(a, b, PathEndMode.Touch,
                    TraverseParms.For(TraverseMode.PassDoors)))
            {
                return;
            }
            IntVec3 cursor = a;
            int guard = map.Size.x + map.Size.z;
            while (cursor.x != b.x && guard-- > 0)
            {
                cursor.x += cursor.x < b.x ? 1 : -1;
                if (cursor != b)
                {
                    CarveCell(map, cursor);
                    CarveCell(map, cursor + IntVec3.North);
                }
            }
            while (cursor.z != b.z && guard-- > 0)
            {
                cursor.z += cursor.z < b.z ? 1 : -1;
                if (cursor != b)
                {
                    // The endpoint itself is never carved: it can be the
                    // coffin or the door, and the passage only needs to
                    // arrive beside them.
                    CarveCell(map, cursor);
                    CarveCell(map, cursor + IntVec3.East);
                }
            }
            Log.Warning("[The Shattered Crown] Crypt generation left the route sealed; carved an emergency passage.");
        }

        /// <summary>
        /// Bore the survey tunnel from the cave exit to the crypt's outer
        /// wall, then rebuild the last course as the crew's brickwork:
        /// sandstone blocks against the crypt's marble, sixty years old.
        /// Old Wick's route leaves the center course down - he went in.
        /// Returns the breach point (where tunnel meets wall).
        /// </summary>
        private static IntVec3 TunnelToBrickwork(Map map, CellRect cryptRect, bool breached)
        {
            ThingDef exitDef = DefDatabase<ThingDef>.GetNamedSilentFail("CaveExit");
            Thing exit = null;
            if (exitDef != null)
            {
                foreach (Thing thing in map.listerThings.ThingsOfDef(exitDef))
                {
                    exit = thing;
                    break;
                }
            }
            if (exit == null)
            {
                IntVec3 spawnCell = new IntVec3(cryptRect.minX - 6, 0, cryptRect.CenterCell.z);
                if (!spawnCell.InBounds(map))
                {
                    spawnCell = cryptRect.CenterCell;
                }
                foreach (IntVec3 cell in GenAdj.OccupiedRect(spawnCell, Rot4.North, new IntVec2(3, 3)).ExpandedBy(1))
                {
                    CarveCell(map, cell);
                }
                if (exitDef != null)
                {
                    exit = GenSpawn.Spawn(ThingMaker.MakeThing(exitDef), spawnCell, map);
                }
            }
            IntVec3 from = exit != null ? exit.Position : map.Center;
            MapGenerator.PlayerStartSpot = from;

            IntVec3 breachPoint = cryptRect.CenterCell;
            CellRect inner = cryptRect.ContractedBy(2);
            bool horizontal = true;
            foreach (IntVec3 point in GenSight.PointsOnLineOfSight(from, cryptRect.CenterCell))
            {
                if (inner.Contains(point))
                {
                    break;
                }
                if (cryptRect.Contains(point))
                {
                    breachPoint = point;
                    break;
                }
                horizontal = Mathf.Abs(cryptRect.CenterCell.x - from.x) >= Mathf.Abs(cryptRect.CenterCell.z - from.z);
                foreach (IntVec3 cell in CellRect.CenteredOn(point, 3, 3))
                {
                    CarveCell(map, cell);
                }
            }
            // The brickwork: a 3-course line across the breach, perpendicular
            // to the tunnel. The CENTER course is down on EVERY route - sixty
            // years and the hill's damp did it on the trust route, Old Wick
            // did it on his. "Bricked shut" with no way in but mining made
            // the finale a quarrying job; a door-sized fall keeps the fiction
            // and the path.
            ThingDef brickStuff = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksSandstone") ?? ThingDefOf.BlocksGranite;
            for (int i = -1; i <= 1; i++)
            {
                IntVec3 cell = horizontal
                    ? new IntVec3(breachPoint.x, 0, breachPoint.z + i)
                    : new IntVec3(breachPoint.x + i, 0, breachPoint.z);
                if (!cell.InBounds(map))
                {
                    continue;
                }
                CarveCell(map, cell);
                if (i == 0)
                {
                    continue; // the fallen course: the way in
                }
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, brickStuff), cell, map);
            }
            FilthMaker.TryMakeFilth(breachPoint, map, ThingDefOf.Filth_RubbleRock);
            if (breached)
            {
                // Old Wick's route: he cleared more than the weather did.
                FilthMaker.TryMakeFilth(breachPoint, map, ThingDefOf.Filth_RubbleRock);
            }
            // Punch the gap THROUGH: the layout's own outer wall can stand a
            // cell behind the brickwork, turning the fallen course into a
            // niche. Carve along the tunnel direction until the first open
            // interior floor, so the gap is a doorway and not a decoration.
            IntVec3 inward = horizontal
                ? new IntVec3(cryptRect.CenterCell.x >= breachPoint.x ? 1 : -1, 0, 0)
                : new IntVec3(0, 0, cryptRect.CenterCell.z >= breachPoint.z ? 1 : -1);
            IntVec3 probe = breachPoint;
            for (int step = 0; step < 8; step++)
            {
                probe += inward;
                if (!probe.InBounds(map))
                {
                    break;
                }
                if (probe.Standable(map) && cryptRect.ContractedBy(2).Contains(probe))
                {
                    break; // open interior reached
                }
                CarveCell(map, probe);
            }
            return breachPoint;
        }

        /// <summary>
        /// Removes the layout's dormant insect clusters and the wake-up signal
        /// actions that drive them. The signals are what leaves "could not
        /// resolve reference to Lord_NN" in the log once their cluster is
        /// dead, so they go with the hives rather than outliving them.
        /// </summary>
        private static void ClearDormantInsects(Map map)
        {
            Faction insects = Faction.OfInsects;
            if (insects != null)
            {
                List<Pawn> spawned = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
                for (int i = 0; i < spawned.Count; i++)
                {
                    if (spawned[i].Faction == insects)
                    {
                        spawned[i].Destroy();
                    }
                }
            }
            ThingDef signalDef = DefDatabase<ThingDef>.GetNamedSilentFail("SignalAction_DormancyWakeUp");
            if (signalDef == null)
            {
                return;
            }
            List<Thing> signals = new List<Thing>(map.listerThings.ThingsOfDef(signalDef));
            for (int i = 0; i < signals.Count; i++)
            {
                if (signals[i].Spawned)
                {
                    signals[i].Destroy();
                }
            }
        }

        private static IntVec3 FarthestStandable(Map map, CellRect rect, IntVec3 from)
        {
            IntVec3 best = rect.CenterCell;
            float bestDist = -1f;
            foreach (IntVec3 cell in rect.ContractedBy(2))
            {
                if (!cell.Standable(map))
                {
                    continue;
                }
                float dist = cell.DistanceTo(from);
                if (dist > bestDist)
                {
                    bestDist = dist;
                    best = cell;
                }
            }
            return best;
        }

        /// <summary>Old Wick's own coffin - a marble sarcophagus, sixty years empty. The shard is NOT here; it lies behind the warm door.</summary>
        private static void SpawnWicksCoffin(Map map, IntVec3 cell)
        {
            ThingDef sarcophagusDef = DefDatabase<ThingDef>.GetNamedSilentFail("Sarcophagus");
            ThingDef marble = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksMarble");
            if (sarcophagusDef == null)
            {
                return;
            }
            foreach (IntVec3 c in GenAdj.OccupiedRect(cell, Rot4.North, sarcophagusDef.size))
            {
                CarveCell(map, c);
            }
            GenSpawn.Spawn(ThingMaker.MakeThing(sarcophagusDef, marble), cell, map);
        }

        /// <summary>
        /// The warm door, set into the rock beyond the crypt's far edge on the
        /// line from the breach through Wick's coffin, and the burial chamber
        /// behind it: the elder's sarcophagus, the elder still in it, and the
        /// crown shard that was buried with it. Returns the door's cell.
        /// </summary>
        private static IntVec3 PlaceWarmDoor(Map map, CellRect rect, IntVec3 breach, IntVec3 coffinCell)
        {
            ThingDef doorDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_WarmDoor");
            if (doorDef == null)
            {
                return coffinCell;
            }
            IntVec3 dir = coffinCell - breach;
            IntVec3 step = Mathf.Abs(dir.x) >= Mathf.Abs(dir.z)
                ? new IntVec3(dir.x >= 0 ? 1 : -1, 0, 0)
                : new IntVec3(0, 0, dir.z >= 0 ? 1 : -1);
            IntVec3 doorCell = coffinCell + step * 5;
            // A ONE-CELL threshold: the rock either side of the door has to
            // stay solid, or the party simply walks around the door and the
            // whole encounter is skipped. Carve the approach as a single-file
            // passage from the crypt's edge up to the door, nothing wider.
            for (int i = 1; i <= 5; i++)
            {
                CarveCell(map, coffinCell + step * i);
            }
            CarveCell(map, doorCell);
            CarveBurialChamber(map, doorCell, step);
            GenSpawn.Spawn(ThingMaker.MakeThing(doorDef), doorCell, map);
            return doorCell;
        }

        /// <summary>
        /// Behind the door: a BUILT marble room, not an assumption about
        /// solid rock - the cavern mutator can open this whole area, which
        /// once left the warm door standing alone in a cave. Walls go up
        /// around the chamber and along the approach corridor, the elder
        /// sleeps INSIDE a working sarcophagus (a cryptosleep casket in
        /// marble), and the shard rests at its head. Lifting the shard wakes
        /// it; so does opening the box (see MapComponent_TSC_ElderSleeper).
        /// </summary>
        private static void CarveBurialChamber(Map map, IntVec3 doorCell, IntVec3 step)
        {
            ThingDef marble = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksMarble");
            IntVec3 perp = step.x != 0 ? new IntVec3(0, 0, 1) : new IntVec3(1, 0, 0);

            // The door is framed whatever surrounds it, and the corridor from
            // Wick's coffin to the door gets flanking walls where the rock
            // does not already provide them.
            void WallIfOpen(IntVec3 cell)
            {
                if (!cell.InBounds(map))
                {
                    return;
                }
                if (cell.GetEdifice(map) == null)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, marble ?? ThingDefOf.BlocksGranite), cell, map);
                }
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
            }

            for (int i = -4; i <= 0; i++)
            {
                IntVec3 corridorCell = doorCell + step * i;
                if (i != 0)
                {
                    CarveCell(map, corridorCell);
                }
                WallIfOpen(corridorCell + perp);
                WallIfOpen(corridorCell - perp);
            }

            // The chamber: an 11x11 marble-walled room whose only opening is
            // the cell in line with the door.
            IntVec3 chamberCenter = doorCell + step * 6;
            CellRect chamber = CellRect.CenteredOn(chamberCenter, 11, 11).ClipInsideMap(map);
            if (!chamberCenter.InBounds(map))
            {
                chamberCenter = chamber.CenterCell;
            }
            IntVec3 gate = doorCell + step;
            foreach (IntVec3 cell in chamber)
            {
                bool edge = cell.x == chamber.minX || cell.x == chamber.maxX
                    || cell.z == chamber.minZ || cell.z == chamber.maxZ;
                CarveCell(map, cell);
                if (edge && cell != gate)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, marble ?? ThingDefOf.BlocksGranite), cell, map);
                }
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
            }

            // The site persists while its quest runs, so the map can be built
            // twice. A shard already lifted is not buried here again, and
            // nothing is left to wake over it: the chamber regenerates open
            // and empty.
            if (DialogueStateManager.Current.IsSet(TSC_ShardTracker.RushFlag))
            {
                return;
            }

            ThingDef casketDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_ElderSarcophagus");
            Building_CryptosleepCasket casket = null;
            if (casketDef != null)
            {
                foreach (IntVec3 c in GenAdj.OccupiedRect(chamberCenter, Rot4.North, casketDef.size))
                {
                    CarveCell(map, c);
                }
                ThingDef slate = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksSlate");
                casket = (Building_CryptosleepCasket)GenSpawn.Spawn(
                    ThingMaker.MakeThing(casketDef, slate ?? ThingDefOf.BlocksGranite),
                    chamberCenter, map, Rot4.North);
            }
            Pawn elder = GenerateElder();
            if (elder != null && casket != null)
            {
                if (!casket.TryAcceptThing(elder))
                {
                    // The box refused (should not happen): lay the sleeper
                    // beside it rather than lose the boss.
                    IntVec3 cell = CellFinder.StandableCellNear(chamberCenter, map, 3f);
                    GenSpawn.Spawn(elder, cell.IsValid ? cell : chamberCenter, map);
                }
            }
            Thing shard = null;
            ThingDef shardDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard");
            if (shardDef != null)
            {
                // At the head of the sarcophagus, where an offering sits.
                IntVec3 shardCell = chamberCenter + new IntVec3(0, 0, 2);
                if (!shardCell.InBounds(map) || !shardCell.Standable(map))
                {
                    shardCell = CellFinder.StandableCellNear(chamberCenter, map, 3f);
                }
                if (shardCell.IsValid)
                {
                    shard = GenSpawn.Spawn(ThingMaker.MakeThing(shardDef), shardCell, map);
                }
            }
            map.GetComponent<MapComponent_TSC_ElderSleeper>()?.Register(elder, shard, casket);
        }

        /// <summary>The one who made Old Wick, generated armed and UNSPAWNED: it goes into the sarcophagus, not onto the floor.</summary>
        private static Pawn GenerateElder()
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_ElderSanguophage");
            if (kind == null)
            {
                return null;
            }
            Pawn elder = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, null, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                fixedBiologicalAge: 45f, fixedChronologicalAge: 1200f));
            EquipEmberBow(elder);
            return elder;
        }

        /// <summary>
        /// The ember bow, in its hands where the player can see it before the
        /// fight and take it after. Replaces whatever the kind rolled, so the
        /// drop is the reward rather than a random shortbow.
        /// </summary>
        private static void EquipEmberBow(Pawn elder)
        {
            ThingDef bowDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_Weapon_EmberBow");
            if (elder?.equipment == null || bowDef == null)
            {
                return;
            }
            elder.equipment.DestroyAllEquipment();
            ThingWithComps bow = (ThingWithComps)ThingMaker.MakeThing(bowDef);
            bow.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Outsider);
            elder.equipment.AddEquipment(bow);
        }

        private static void CarveCell(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map))
            {
                return;
            }
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i].def.category == ThingCategory.Building && things[i].def.destroyable)
                {
                    things[i].Destroy();
                }
            }
            if (!cell.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy))
            {
                TerrainDef rough = DefDatabase<TerrainDef>.GetNamedSilentFail("Granite_Rough")
                    ?? TerrainDef.Named("FlagstoneGranite");
                map.terrainGrid.SetTerrain(cell, rough);
            }
            map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
        }
    }

    /// <summary>
    /// The looting party at the gallery mouth (crypt SURFACE map): a bandit
    /// captain and crew, picks and shovels and war gear, spawned FACTIONLESS
    /// so the parley can happen before any blood. The parley resolves them:
    /// parley_hostile flips the crew to TSC_Faction_Bandits with an assault
    /// lord; parley_flee (the Arcana 10 scare) panics them off the map.
    /// One encounter per save (TSC_CryptBanditsMet skips the respawn when
    /// the persistent site regenerates).
    /// </summary>
    public class GenStep_TSC_CryptBandits : GenStep
    {
        public IntRange count = new IntRange(3, 5);
        // Difficulty clamp: always a real crew, never an army.
        public IntRange scaledClamp = new IntRange(2, 7);

        public override int SeedPart => 662184937;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (DialogueStateManager.Current.IsSet(MapComponent_TSC_CryptParley.MetFlag))
            {
                return;
            }
            ThingDef entranceDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CryptEntrance");
            Thing entrance = null;
            if (entranceDef != null)
            {
                List<Thing> entrances = map.listerThings.ThingsOfDef(entranceDef);
                entrance = entrances.Count > 0 ? entrances[0] : null;
            }
            IntVec3 anchor = entrance != null
                ? CellFinder.StandableCellNear(entrance.Position, map, 10f)
                : map.Center;
            if (!anchor.IsValid)
            {
                anchor = map.Center;
            }
            MapComponent_TSC_CryptParley parley = map.GetComponent<MapComponent_TSC_CryptParley>();
            PawnKindDef leaderKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_CryptLeader");
            PawnKindDef brigand = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand");
            PawnKindDef archer = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Archer");
            if (leaderKind == null || brigand == null)
            {
                return;
            }
            Pawn leader = SpawnBandit(map, leaderKind, anchor);
            parley?.Register(leader, isLeader: true);
            float threatScale = Find.Storyteller?.difficulty?.threatScale ?? 1f;
            int n = Mathf.Clamp(Mathf.RoundToInt(count.RandomInRange * threatScale),
                scaledClamp.min, scaledClamp.max);
            for (int i = 0; i < n; i++)
            {
                PawnKindDef kind = archer != null && i % 3 == 2 ? archer : brigand;
                parley?.Register(SpawnBandit(map, kind, anchor), isLeader: false);
            }
        }

        private static Pawn SpawnBandit(Map map, PawnKindDef kind, IntVec3 anchor)
        {
            // Factionless until the parley resolves: nobody's turrets or
            // trigger fingers start this fight early.
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, null, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(anchor, map, 5);
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
        }
    }

    /// <summary>
    /// Tracks the looting party and forces the parley: when a colonist gets
    /// close to the captain, the conversation opens (short words, then steel
    /// or a very good Arcana lie). Holds the group roster for the two
    /// dialogue effects.
    /// </summary>
    public class MapComponent_TSC_CryptParley : MapComponent
    {
        public const string MetFlag = "TSC_CryptBanditsMet";
        private const float ParleyRadius = 9f;
        /// <summary>How far a bandit may drift from the spot it was posted before being sent back.</summary>
        private const float PostRadius = 3f;
        private List<Pawn> group = new List<Pawn>();
        private List<IntVec3> posts = new List<IntVec3>();
        private Pawn leader;
        private bool opened;

        public MapComponent_TSC_CryptParley(Map map) : base(map)
        {
        }

        public void Register(Pawn pawn, bool isLeader)
        {
            if (pawn == null)
            {
                return;
            }
            group.Add(pawn);
            posts.Add(pawn.Position);
            if (isLeader)
            {
                leader = pawn;
            }
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 30 != 0)
            {
                return;
            }
            if (opened)
            {
                HealUnflippedCrew();
                return;
            }
            if (leader == null || !leader.Spawned || leader.Dead)
            {
                return;
            }
            HoldThePost();
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.Position.InHorDistOf(leader.Position, ParleyRadius)
                    && GenSight.LineOfSight(p.Position, leader.Position, map))
                {
                    opened = true;
                    DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_CryptBandits");
                    if (def != null)
                    {
                        CameraJumper.TryJump(leader);
                        Find.WindowStack.Add(new Dialog_Conversation(def, leader, p));
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// The crew is here to get INTO the hole, not to tour the hill: they
        /// stand around the gallery mouth arguing about how, until somebody
        /// walks up. Factionless pawns have no lord and no duty, so left alone
        /// they idle-wander off across the map and the party never finds them.
        /// Each is posted at the cell it spawned on and sent back if it drifts.
        /// </summary>
        private void HoldThePost()
        {
            for (int i = 0; i < group.Count; i++)
            {
                Pawn pawn = group[i];
                if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed
                    || pawn.InMentalState || i >= posts.Count)
                {
                    continue;
                }
                IntVec3 post = posts[i];
                if (!pawn.Position.InHorDistOf(post, PostRadius) && post.Standable(map))
                {
                    if (pawn.CurJobDef != JobDefOf.Goto)
                    {
                        pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, post),
                            JobCondition.InterruptForced);
                    }
                    continue;
                }
                // Standing around talking it over: the vanilla idle-chat job
                // keeps them put and facing each other.
                if (pawn.CurJobDef != JobDefOf.StandAndBeSociallyActive)
                {
                    pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.StandAndBeSociallyActive),
                        JobCondition.InterruptForced);
                }
            }
        }

        /// <summary>
        /// Save-repair: a parley that resolved hostile on a world with NO
        /// bandit faction left the crew factionless and idle (the "wrong
        /// answer" that answered nothing). If the parley is over and crew
        /// still stand around unfactioned and calm, flip them properly now -
        /// TurnHostile works these days because the faction utility creates
        /// what it cannot find. Fled crews are in PanicFlee and are skipped.
        /// </summary>
        private bool healChecked;

        private void HealUnflippedCrew()
        {
            if (healChecked)
            {
                return;
            }
            healChecked = true;
            foreach (Pawn pawn in group)
            {
                if (pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed
                    && pawn.Faction == null && !pawn.InMentalState)
                {
                    TurnHostile();
                    return;
                }
            }
        }

        /// <summary>parley_hostile: words are done. The crew turns bandit and comes on.</summary>
        public void TurnHostile()
        {
            DialogueStateManager.Current.Set(MetFlag);
            Faction bandits = TSC_BanditFactionUtility.Get();
            List<Pawn> fighters = new List<Pawn>();
            foreach (Pawn pawn in group)
            {
                if (pawn != null && pawn.Spawned && !pawn.Dead && bandits != null)
                {
                    pawn.SetFaction(bandits);
                    fighters.Add(pawn);
                }
            }
            if (fighters.Count > 0 && bandits != null)
            {
                LordMaker.MakeNewLord(bandits, new LordJob_AssaultColony(bandits,
                    canKidnap: false, canTimeoutOrFlee: true), map, fighters);
            }
            Messages.Message("The looting party attacks!", leader ?? (Thing)null,
                MessageTypeDefOf.ThreatBig, historical: false);
        }

        /// <summary>parley_flee: the Arcana scare lands. They want no part of the hill.</summary>
        public void ScareOff()
        {
            DialogueStateManager.Current.Set(MetFlag);
            foreach (Pawn pawn in group)
            {
                if (pawn != null && pawn.Spawned && !pawn.Dead)
                {
                    pawn.mindState.mentalStateHandler.TryStartMentalState(
                        MentalStateDefOf.PanicFlee, "the hill's bad magic", forced: true);
                }
            }
            Messages.Message("The looting party wants no part of this place. They flee.",
                leader ?? (Thing)null, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref group, "group", LookMode.Reference);
            Scribe_Collections.Look(ref posts, "posts", LookMode.Value);
            Scribe_References.Look(ref leader, "leader");
            Scribe_Values.Look(ref opened, "opened");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                group = group ?? new List<Pawn>();
                posts = posts ?? new List<IntVec3>();
                // group and posts are index-paired; a null-purge would desync
                // them, so only trim once both are known good.
                while (posts.Count < group.Count)
                {
                    posts.Add(IntVec3.Invalid);
                }
                for (int i = group.Count - 1; i >= 0; i--)
                {
                    if (group[i] == null)
                    {
                        group.RemoveAt(i);
                        posts.RemoveAt(i);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The finale: when the party reaches the coffin (and the shard beside
    /// it), Old Wick steps out of the dark - on BOTH routes - armed, and
    /// bares everything (Dialogues/crypt_finale.agd). The dialogue ends in
    /// the choice: let him walk (npc_leave) or make him pay
    /// (npc_hostile(TSC_Hediff_ElderBlood) - centuries make a hard fight).
    /// One-shot per save (TSC_WickFinaleDone); TSC_WickAtCrypt is set here
    /// too, so the trust route also empties his chair in the village.
    /// </summary>
    public class MapComponent_TSC_CryptFinale : MapComponent
    {
        public const string DoneFlag = "TSC_WickFinaleDone";
        private IntVec3 coffinPos = IntVec3.Invalid;
        private bool done;

        public MapComponent_TSC_CryptFinale(Map map) : base(map)
        {
        }

        public void SetCoffinPos(IntVec3 pos)
        {
            coffinPos = pos;
        }

        public override void MapComponentTick()
        {
            if (done || !coffinPos.IsValid || Find.TickManager.TicksGame % 30 != 0)
            {
                return;
            }
            if (DialogueStateManager.Current.IsSet(DoneFlag))
            {
                done = true;
                return;
            }
            Pawn witness = null;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.Position.InHorDistOf(coffinPos, 6f))
                {
                    witness = p;
                    break;
                }
            }
            if (witness == null)
            {
                return;
            }
            done = true;
            NamedNpcDef wickDef = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_OldWick");
            Pawn cached = wickDef != null ? DialogueStateManager.Current.GetNamedNpcIfExists(wickDef) : null;
            if (wickDef == null || (cached != null && cached.Dead))
            {
                // He died before his hour: no finale, the shard is just loot.
                DialogueStateManager.Current.Set(DoneFlag);
                return;
            }
            DialogueStateManager.Current.Set(DoneFlag);
            DialogueStateManager.Current.Set("TSC_WickAtCrypt");
            Pawn wick = DialogueStateManager.Current.GetOrGenerateNamedNpc(wickDef, GenStep_TSC_Village.VillagerFaction());
            if (wick == null || wick.Dead)
            {
                return;
            }
            if (wick.Spawned && wick.Map != map)
            {
                wick.DeSpawn();
            }
            if (!wick.Spawned)
            {
                if (wick.IsWorldPawn())
                {
                    Find.WorldPawns.RemovePawn(wick);
                }
                // Out of the dark on the door side, if the door was placed.
                IntVec3 from = map.GetComponent<MapComponent_TSC_WarmDoor>()?.DoorPos ?? IntVec3.Invalid;
                IntVec3 near = from.IsValid ? from : coffinPos;
                IntVec3 cell = CellFinder.StandableCellNear(near, map, 6f);
                if (!cell.IsValid)
                {
                    cell = CellFinder.StandableCellNear(coffinPos, map, 8f);
                }
                if (!cell.IsValid)
                {
                    return;
                }
                GenSpawn.Spawn(wick, cell, map);
                if (wick.Faction != null)
                {
                    LordMaker.MakeNewLord(wick.Faction, new LordJob_DefendPoint(cell), map, Gen.YieldSingle(wick));
                }
            }
            // "Come armed, if it helps. I would." He did.
            if (wick.equipment?.Primary == null)
            {
                ThingDef swordDef = DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_LongSword");
                if (swordDef != null)
                {
                    ThingWithComps sword = (ThingWithComps)ThingMaker.MakeThing(swordDef, ThingDefOf.Plasteel);
                    sword.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Outsider);
                    wick.equipment?.AddEquipment(sword);
                }
            }
            DialogueDef finale = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_WickFinale");
            if (finale != null)
            {
                CameraJumper.TryJump(wick);
                Find.WindowStack.Add(new Dialog_Conversation(finale, wick, witness));
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref coffinPos, "coffinPos", IntVec3.Invalid);
            Scribe_Values.Look(ref done, "done");
        }
    }

    /// <summary>
    /// DSL effect npc_leave(): the npc walks off the map for good - lord
    /// dissolved, exit lord in its place. "Let him walk."
    /// </summary>
    public class DialogueEffect_TSC_NpcLeave : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Pawn npc = context.npc;
            if (npc == null || !npc.Spawned || npc.Faction == null)
            {
                return;
            }
            npc.GetLord()?.Notify_PawnLost(npc, PawnLostCondition.ForcedToJoinOtherLord);
            LordMaker.MakeNewLord(npc.Faction,
                new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: false, canDefendSelf: true),
                npc.Map, Gen.YieldSingle(npc));
        }
    }

    /// <summary>
    /// DSL effect npc_hostile(HediffDefName?): the npc turns on the party -
    /// bandit faction, assault lord, and an optional hediff so an ancient
    /// thing fights like one. "Make him pay."
    /// </summary>
    public class DialogueEffect_TSC_NpcHostile : DialogueEffect
    {
        public HediffDef hediff;

        public override void Apply(DialogueContext context)
        {
            Pawn npc = context.npc;
            if (npc == null || !npc.Spawned || npc.Dead)
            {
                return;
            }
            Faction hostiles = TSC_BanditFactionUtility.Get();
            if (hostiles == null)
            {
                return;
            }
            npc.GetLord()?.Notify_PawnLost(npc, PawnLostCondition.ForcedToJoinOtherLord);
            npc.SetFaction(hostiles);
            if (hediff != null)
            {
                npc.health.AddHediff(hediff);
            }
            LordMaker.MakeNewLord(hostiles, new LordJob_AssaultColony(hostiles,
                canKidnap: false, canTimeoutOrFlee: false), npc.Map, Gen.YieldSingle(npc));
        }
    }

    /// <summary>
    /// Shamblers do not eat their own side. Vanilla shambler targeting
    /// (MutantUtility.CheckShamblerHostility) does not fully respect faction
    /// - the crypt's risen kept mauling the elder they rose to serve.
    /// Same non-null faction = not prey, which also cannot break vanilla:
    /// entity-faction shamblers only share a faction with other entities,
    /// which were never valid targets anyway.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(MutantUtility), nameof(MutantUtility.CheckShamblerHostility))]
    public static class Patch_ShamblersSpareTheirOwn
    {
        public static void Postfix(Pawn __0, Pawn __1, ref bool __result)
        {
            if (__result && __0?.Faction != null && __0.Faction == __1?.Faction)
            {
                __result = false;
            }
        }
    }

    /// <summary>DSL effect parley_hostile: the npc's parley group attacks.</summary>
    public class DialogueEffect_TSC_ParleyHostile : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            context.npc?.MapHeld?.GetComponent<MapComponent_TSC_CryptParley>()?.TurnHostile();
        }
    }

    /// <summary>DSL effect parley_flee: the npc's parley group panics off the map.</summary>
    public class DialogueEffect_TSC_ParleyFlee : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            context.npc?.MapHeld?.GetComponent<MapComponent_TSC_CryptParley>()?.ScareOff();
        }
    }

    /// <summary>
    /// The elder sanguophage behind the warm door, sealed INSIDE its black
    /// sarcophagus (a cryptosleep casket). Two things wake it: the shard
    /// leaving the ground - lifted into a pawn's hands is a grave robbed -
    /// or the box being opened or broken, since a pawn that suddenly exists
    /// outside the casket got there because somebody disturbed it. Combat
    /// is not optional either way: the shard is what it was buried to keep.
    /// No health baselines, no forced lie-down jobs: a pawn inside a casket
    /// cannot idle-wander, be pummeled by strays, or misreport its health.
    /// </summary>
    public class MapComponent_TSC_ElderSleeper : MapComponent
    {
        public const string WokeFlag = "TSC_ElderWoke";
        private Pawn elder;
        private Thing shard;
        private Building_CryptosleepCasket casket;
        private bool awake;

        public MapComponent_TSC_ElderSleeper(Map map) : base(map)
        {
        }

        public void Register(Pawn sleeper, Thing buriedShard, Building_CryptosleepCasket box)
        {
            elder = sleeper;
            shard = buriedShard;
            casket = box;
            if (sleeper != null && buriedShard == null)
            {
                Log.Warning("[The Shattered Crown] Elder sleeper registered with NO shard: the wake-on-theft trigger is disabled for this map.");
            }
        }

        public override void MapComponentTick()
        {
            if (awake || elder == null || elder.Dead
                || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            // A null reference is NOT theft: it means no shard was ever
            // registered (misgeneration). Theft is a shard that existed and
            // is no longer lying where it was left.
            bool shardTaken = shard != null && (!shard.Spawned || shard.Destroyed);
            // Spawned means OUT OF THE BOX: the casket was opened by hand or
            // broken open. Either way, somebody chose this.
            bool disturbed = elder.Spawned;
            if (shardTaken || disturbed)
            {
                Wake();
            }
        }

        private void Wake()
        {
            awake = true;
            DialogueStateManager.Current.Set(WokeFlag);
            if (elder.Dead)
            {
                return;
            }
            if (!elder.Spawned && casket != null && !casket.Destroyed)
            {
                // The lid moves. EjectContents drops the sleeper beside the box.
                casket.EjectContents();
            }
            if (!elder.Spawned)
            {
                return;
            }
            elder.mindState.canFleeIndividual = false;
            elder.jobs?.StopAll();
            // Kindled blood, not elder blood: Wick's inheritance is a
            // swordsman's package and does nothing for an archer.
            HediffDef kindledBlood = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_KindledBlood");
            if (kindledBlood != null && !elder.health.hediffSet.HasHediff(kindledBlood))
            {
                elder.health.AddHediff(kindledBlood);
            }
            Faction hostiles = TSC_BanditFactionUtility.Get();
            if (hostiles != null)
            {
                elder.SetFaction(hostiles);
                List<Pawn> fighters = new List<Pawn> { elder };
                fighters.AddRange(RaiseTheDead(hostiles));
                LordMaker.MakeNewLord(hostiles, new LordJob_AssaultColony(hostiles,
                    canKidnap: false, canTimeoutOrFlee: false), map, fighters);
            }
            CameraJumper.TryJump(elder);
            Find.LetterStack.ReceiveLetter(
                "The sleeper wakes",
                "The shard leaves the stone, and the thing it was buried with opens its eyes.\n\n"
                + "The crypt's dead are getting up with it, and it is all "
                + "between you and the way out.",
                LetterDefOf.ThreatBig, elder);
        }

        /// <summary>
        /// The crypt's own dead come up with their maker: shamblers where
        /// Anomaly provides them, plain risen otherwise, so the fight is a
        /// fight and not a duel. Scaled by difficulty and clamped so it is
        /// never nothing and never a wall.
        /// </summary>
        private List<Pawn> RaiseTheDead(Faction hostiles)
        {
            List<Pawn> risen = new List<Pawn>();
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand")
                ?? PawnKindDefOf.Villager;
            float threatScale = Find.Storyteller?.difficulty?.threatScale ?? 1f;
            // A handful, not a horde: the elder IS the fight - the risen are
            // there to stop the party from simply kiting an archer around a
            // marble room. Difficulty widens the handful, capped at six.
            int count = Mathf.Clamp(Mathf.RoundToInt(Rand.RangeInclusive(2, 3) * threatScale), 2, 6);
            for (int i = 0; i < count; i++)
            {
                // The risen serve their raiser: SAME faction as the elder,
                // never Faction.OfEntities - the entity faction is hostile to
                // everything, and the dead were eating their own boss.
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind, hostiles, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false);
                if (ModsConfig.AnomalyActive)
                {
                    request.ForcedMutant = MutantDefOf.Shambler;
                }
                Pawn dead = PawnGenerator.GeneratePawn(request);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(elder.Position, map, 7);
                GenSpawn.Spawn(dead, cell, map);
                // The generator can hand shambler mutants to the entity
                // faction regardless of the request; the risen serve the
                // Crownless, full stop.
                if (dead.Faction != hostiles)
                {
                    dead.SetFaction(hostiles);
                }
                // Shamblers keep their own mutant AI (it targets by faction
                // hostility, so they come for the party, not the elder); the
                // plain risen join the elder's assault lord.
                if (!ModsConfig.AnomalyActive)
                {
                    risen.Add(dead);
                }
            }
            return risen;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref elder, "elder");
            Scribe_References.Look(ref shard, "shard");
            Scribe_References.Look(ref casket, "casket");
            Scribe_Values.Look(ref awake, "awake");
        }
    }

    /// <summary>
    /// The epilogue: once the shard is in hand and the crypt has gone quiet,
    /// Serra and Oswin come down the rope - they felt the act turn too. Serra
    /// points the party at Oswin; Oswin has the next lead (the bard). Their
    /// dialogue entries gate on TSC_ShardRushSeen, so if the party leaves
    /// before talking, the same conversations wait at the village camp.
    /// </summary>
    public class MapComponent_TSC_ShardEpilogue : MapComponent
    {
        public const string ArrivedFlag = "TSC_ShardEpilogueArrived";
        private bool done;

        public MapComponent_TSC_ShardEpilogue(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (done || Find.TickManager.TicksGame % 60 != 0
                || map.generatorDef?.defName != "TSC_CryptUnderground")
            {
                return;
            }
            if (DialogueStateManager.Current.IsSet(ArrivedFlag))
            {
                done = true;
                return;
            }
            if (!DialogueStateManager.Current.IsSet(TSC_ShardTracker.RushFlag)
                || map.mapPawns.FreeColonistsSpawnedCount == 0
                || GenHostility.AnyHostileActiveThreatToPlayer(map))
            {
                return;
            }
            done = true;
            DialogueStateManager.Current.Set(ArrivedFlag);
            IntVec3 rope = map.Center;
            ThingDef exitDef = DefDatabase<ThingDef>.GetNamedSilentFail("CaveExit");
            if (exitDef != null)
            {
                foreach (Thing thing in map.listerThings.ThingsOfDef(exitDef))
                {
                    rope = thing.Position;
                    break;
                }
            }
            bool any = false;
            foreach (string defName in new[] { "TSC_Npc_Serra", "TSC_Npc_Oswin" })
            {
                NamedNpcDef npcDef = DefDatabase<NamedNpcDef>.GetNamedSilentFail(defName);
                if (npcDef == null)
                {
                    continue;
                }
                Pawn cached = DialogueStateManager.Current.GetNamedNpcIfExists(npcDef);
                if (cached != null && cached.Dead)
                {
                    continue; // the pass took them; no epilogue for the dead
                }
                Pawn pawn = DialogueStateManager.Current.GetOrGenerateNamedNpc(npcDef, GenStep_TSC_Village.VillagerFaction());
                if (pawn == null || pawn.Dead || pawn.Faction == Faction.OfPlayer)
                {
                    continue;
                }
                if (pawn.Spawned && pawn.Map != map)
                {
                    pawn.DeSpawn();
                }
                if (!pawn.Spawned)
                {
                    if (pawn.IsWorldPawn())
                    {
                        Find.WorldPawns.RemovePawn(pawn);
                    }
                    IntVec3 cell = CellFinder.StandableCellNear(rope, map, 5f);
                    if (!cell.IsValid)
                    {
                        continue;
                    }
                    GenSpawn.Spawn(pawn, cell, map);
                    pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                }
                // They arrive as GUESTS, not conscripts: each joins the party
                // through their own scene (Serra's shard_epilogue, Oswin's
                // lead_taken). Until then they hold at the rope.
                if (pawn.Spawned && pawn.Faction != Faction.OfPlayer)
                {
                    if (pawn.GetLord() == null)
                    {
                        LordMaker.MakeNewLord(pawn.Faction,
                            new LordJob_DefendPoint(pawn.Position), map, Gen.YieldSingle(pawn));
                    }
                    any = true;
                }
            }
            if (any)
            {
                // No letter card: Serra's shard_epilogue scene carries the
                // arrival. A quiet top-left line is enough of a pointer.
                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
                Messages.Message("Serra and Oswin have come down the rope.",
                    colonists.Count > 0 ? colonists[0] : (LookTargets)null,
                    MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref done, "done");
        }
    }

    /// <summary>
    /// One-shot per save: the warm door speaks when the party first stands
    /// before it (Dialogues/warm_door.agd, conversation window). The door
    /// itself is an ordinary door and stays put - opening it and walking into
    /// the burial chamber is the player's move to make. Plays on both routes;
    /// gated on the finale so Old Wick always speaks first.
    /// </summary>
    public class MapComponent_TSC_WarmDoor : MapComponent
    {
        private const string HeardFlag = "TSC_WarmDoorHeard";
        private IntVec3 doorPos = IntVec3.Invalid;
        private bool done;

        public MapComponent_TSC_WarmDoor(Map map) : base(map)
        {
        }

        public IntVec3 DoorPos => doorPos;

        public void SetDoorPos(IntVec3 pos)
        {
            doorPos = pos;
        }

        public override void MapComponentTick()
        {
            if (done || !doorPos.IsValid || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            if (DialogueStateManager.Current.IsSet(HeardFlag))
            {
                done = true;
                return;
            }
            // Old Wick gets his scene first: the door is five cells past his
            // coffin, so this only matters if the finale somehow did not fire.
            if (!DialogueStateManager.Current.IsSet(MapComponent_TSC_CryptFinale.DoneFlag))
            {
                return;
            }
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.Position.InHorDistOf(doorPos, 5f))
                {
                    done = true;
                    DialogueStateManager.Current.Set(HeardFlag);
                    DialogueDef door = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_WarmDoor");
                    if (door != null)
                    {
                        CameraJumper.TryJump(new RimWorld.Planet.GlobalTargetInfo(doorPos, map));
                        Find.WindowStack.Add(new Dialog_Conversation(door, p, p));
                    }
                    return;
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref doorPos, "doorPos", IntVec3.Invalid);
            Scribe_Values.Look(ref done, "done");
        }
    }
}
