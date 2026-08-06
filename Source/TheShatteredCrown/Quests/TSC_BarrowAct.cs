using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// The duty of the Honor Guard: hold this chamber, want nothing.
    ///
    /// LordToil_DefendPoint was close but leaks in two places. Its
    /// AllowSatisfyLongNeeds is true, so a guard will break off the duty to
    /// go and eat or sleep - and there is no food and no bed in a sealed
    /// barrow, so the errand takes them out of the room and across the map.
    /// And its wanderRadius defaults to null, which leaves the idle wander
    /// unbounded by anything except the (necessarily large) defend radius.
    ///
    /// These are men who have been standing over a dead king for eight
    /// centuries. They are not hungry. They are not going anywhere.
    /// </summary>
    public class LordToil_TSC_HoldTomb : LordToil_DefendPoint
    {
        public LordToil_TSC_HoldTomb()
            : base(canSatisfyLongNeeds: false)
        {
        }

        public LordToil_TSC_HoldTomb(IntVec3 point, float defendRadius, float wanderRadius)
            : this()
        {
            Data.defendPoint = point;
            Data.defendRadius = defendRadius;
            Data.wanderRadius = wanderRadius;
        }
    }

    /// <summary>
    /// A state graph with one state and no transitions, so there is no path
    /// out of it: no timeout, no flee, no stealing, no exit. Every one of
    /// those was a door in LordJob_AssaultColony, and closing them one at a
    /// time did not work.
    /// </summary>
    public class LordJob_TSC_HoldTomb : LordJob
    {
        private IntVec3 point;
        private float defendRadius = 8f;
        private float wanderRadius = 4f;

        public override bool AddFleeToil => false;

        public LordJob_TSC_HoldTomb()
        {
        }

        public LordJob_TSC_HoldTomb(IntVec3 point, float defendRadius, float wanderRadius)
        {
            this.point = point;
            this.defendRadius = defendRadius;
            this.wanderRadius = wanderRadius;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            graph.AddToil(new LordToil_TSC_HoldTomb(point, defendRadius, wanderRadius));
            return graph;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref point, "tscHoldPoint");
            Scribe_Values.Look(ref defendRadius, "tscHoldDefendRadius", 8f);
            Scribe_Values.Look(ref wanderRadius, "tscHoldWanderRadius", 4f);
        }
    }

    /// <summary>
    /// The barrow's interior, reshaped as a PROCESSION: one long hall from
    /// the stair to the king, the five road panels set in wall niches along
    /// it, and nothing hostile anywhere before the throne room's door. The
    /// old catacombs layout made the last descent a maze with the story
    /// scattered through it; a funeral road walks better. The throne-room
    /// genstep runs after this one and places the chamber at the point
    /// farthest from the stair, which in a hall is exactly the far end.
    /// </summary>
    public class GenStep_TSC_KingsHall : GenStep
    {
        /// <summary>Total corridor width, including the centerline.</summary>
        public int hallWidth = 7;

        /// <summary>The wall pieces, stair to door: the dust line first, then the five roads in order.</summary>
        private static readonly string[] PanelDefNames =
        {
            "TSC_Spot_DustLine", "TSC_Road_Grave", "TSC_Road_Reliquary",
            "TSC_Road_Hoard", "TSC_Road_Quiet", "TSC_Road_Last",
        };

        public override int SeedPart => 862914035;

        public override void Generate(Map map, GenStepParams parms)
        {
            // The hall OWNS its stair. If some earlier genstep already
            // placed an exit, anchor on it; otherwise pick a spot off one
            // edge, and spawn the portal's TSC_StairsUp there ourselves -
            // generation runs with PocketMapUtility.currentlyGeneratingPortal
            // set, so the exit binds to the mouth the vanilla way. Without
            // this, the mouth's later self-heal dropped a SECOND stair at
            // map center because vanilla PlaceCaveExit had spawned a generic
            // CaveExit instead of the def the portal asked for.
            Thing wayUp = TSC_KeepCellar.FindWayUp(map);
            IntVec3 start;
            if (wayUp != null)
            {
                start = wayUp.Position;
            }
            else
            {
                start = new IntVec3(14, 0, map.Size.z / 2);
                Carve(map, CellRect.CenteredOn(start, 5, 5));
                ThingDef stairsDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_StairsUp");
                if (stairsDef != null)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(stairsDef), start, map);
                }
                MapGenerator.PlayerStartSpot = start;
            }
            // Run the hall down whichever compass direction has the most
            // rock in front of it, and stop well short of the edge so the
            // throne genstep has room for its 15x15 chamber.
            IntVec3 dir = LongestRun(map, start);
            // 20 off the far edge, not 13: the throne room (15x15) sits
            // BEYOND the hall's end now, and needs the depth.
            int run = RunLength(map, start, dir) - 20;
            if (run < 20)
            {
                // A stair placed absurdly near the far edge: fall back to
                // whatever direction faces the map's middle.
                dir = (map.Center - start).x * dir.x + (map.Center - start).z * dir.z >= 0
                    ? dir : new IntVec3(-dir.x, 0, -dir.z);
                run = Mathf.Max(20, RunLength(map, start, dir) - 20);
            }
            IntVec3 end = start + new IntVec3(dir.x * run, 0, dir.z * run);
            // Hand the throne genstep its anchor: room center a half-room
            // past the hall's last cell, so the near wall meets the hall
            // and the door opens onto it.
            map.GetComponent<MapComponent_TSC_Barrow>()?.SetThroneAnchor(
                end + new IntVec3(dir.x * 7, 0, dir.z * 7));

            // The landing: breathing room around the stair.
            Carve(map, CellRect.CenteredOn(start, 9, 9));

            int half = hallWidth / 2;
            CellRect hall = CellRect.FromLimits(start, end).ExpandedBy(half);
            // Expand only ACROSS the hall, not along it: FromLimits+Expand
            // widens both axes, so trim the overshoot at the ends.
            hall = dir.x != 0
                ? new CellRect(Mathf.Min(start.x, end.x), hall.minZ, run + 1, hallWidth)
                : new CellRect(hall.minX, Mathf.Min(start.z, end.z), hallWidth, run + 1);
            Carve(map, hall);
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail("FlagstoneGranite");
            if (floor != null)
            {
                foreach (IntVec3 cell in hall)
                {
                    if (cell.GetEdifice(map) == null)
                    {
                        map.terrainGrid.SetTerrain(cell, floor);
                    }
                }
            }

            // The panels, in niches: one wall bay per piece, alternating
            // sides, spaced down the processional order the roads were
            // ridden in. A niche is a one-deep, three-long recess cut past
            // the hall wall, with the stone standing in its middle - close
            // enough to read from the hall, out of the walking line.
            for (int i = 0; i < PanelDefNames.Length; i++)
            {
                ThingDef panel = DefDatabase<ThingDef>.GetNamedSilentFail(PanelDefNames[i]);
                if (panel == null)
                {
                    continue;
                }
                float t = (i + 1f) / (PanelDefNames.Length + 1f);
                IntVec3 along = start + new IntVec3(
                    Mathf.RoundToInt(dir.x * run * t), 0, Mathf.RoundToInt(dir.z * run * t));
                int side = i % 2 == 0 ? 1 : -1;
                IntVec3 across = dir.x != 0 ? new IntVec3(0, 0, side) : new IntVec3(side, 0, 0);
                IntVec3 nicheMid = along + new IntVec3(across.x * (half + 1), 0, across.z * (half + 1));
                CellRect niche = dir.x != 0
                    ? new CellRect(nicheMid.x - 1, nicheMid.z, 3, 1)
                    : new CellRect(nicheMid.x, nicheMid.z - 1, 1, 3);
                Carve(map, niche);
                if (floor != null)
                {
                    foreach (IntVec3 cell in niche)
                    {
                        map.terrainGrid.SetTerrain(cell, floor);
                    }
                }
                if (nicheMid.InBounds(map) && nicheMid.GetEdifice(map) == null)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(panel), nicheMid, map);
                }
            }
        }

        private static void Carve(Map map, CellRect rect)
        {
            foreach (IntVec3 cell in rect.ClipInsideMap(map))
            {
                GenStep_TSC_CellarLevel.CarveCell(map, cell);
            }
        }

        /// <summary>The cardinal direction with the most map in front of the start.</summary>
        private static IntVec3 LongestRun(Map map, IntVec3 start)
        {
            IntVec3 best = IntVec3.North;
            int bestRun = -1;
            foreach (IntVec3 dir in new[] { IntVec3.North, IntVec3.South, IntVec3.East, IntVec3.West })
            {
                int run = RunLength(map, start, dir);
                if (run > bestRun)
                {
                    bestRun = run;
                    best = dir;
                }
            }
            return best;
        }

        private static int RunLength(Map map, IntVec3 start, IntVec3 dir)
        {
            return dir.x > 0 ? map.Size.x - 1 - start.x
                : dir.x < 0 ? start.x
                : dir.z > 0 ? map.Size.z - 1 - start.z
                : start.z;
        }
    }

    /// <summary>
    /// The throne room at the bottom of the Barrow of Kings: a walled chamber
    /// at the deepest point holding the first king's sarcophagus and the
    /// Honor Guard still standing around it.
    ///
    /// The guard spawn FACTIONLESS, which is the whole design: they are not
    /// a monster closet. They challenge whoever comes through the door
    /// (Dialogues/honor_guard.agd), and they only become a fight if the
    /// company fails or refuses to answer. A hostile faction is assigned at
    /// that moment, not at generation.
    /// </summary>
    public class GenStep_TSC_ThroneRoom : GenStep
    {
        public IntRange guards = new IntRange(3, 5);

        public override int SeedPart => 774103928;

        public override void Generate(Map map, GenStepParams parms)
        {
            Thing wayUp = TSC_KeepCellar.FindWayUp(map);
            IntVec3 anchor = wayUp?.Position ?? map.Center;
            // The hall's word beats the guesswork: when the hall genstep has
            // said where it ends, the chamber goes there, at the end of the
            // procession. The farthest-cell scan below is only for maps
            // without a hall (legacy saves, other users of this genstep).
            IntVec3 center = map.GetComponent<MapComponent_TSC_Barrow>()?.ThroneAnchor ?? IntVec3.Invalid;
            if (center.IsValid && center.DistanceToEdge(map) < 9)
            {
                center = IntVec3.Invalid;
            }
            if (!center.IsValid)
            {
                float best = -1f;
                foreach (IntVec3 cell in map.AllCells)
                {
                    if (cell.DistanceToEdge(map) < 9 || !cell.Standable(map))
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
            }
            if (!center.IsValid)
            {
                center = map.Center;
            }
            // 15x15, not 13x13: the eleven lesser tombs are laid in rows
            // along the north and south walls, and they need the depth.
            CellRect room = CellRect.CenteredOn(center, 15, 15).ClipInsideMap(map);
            foreach (IntVec3 cell in room)
            {
                GenStep_TSC_CellarLevel.CarveCell(map, cell);
            }
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail("TileMarble")
                ?? DefDatabase<TerrainDef>.GetNamedSilentFail("FlagstoneGranite");
            if (floor != null)
            {
                foreach (IntVec3 cell in room.ContractedBy(1))
                {
                    if (cell.GetEdifice(map) == null)
                    {
                        map.terrainGrid.SetTerrain(cell, floor);
                    }
                }
            }
            // The door faces the way in, so the challenge happens at a
            // threshold rather than in the middle of an open floor.
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
                if (cell == doorCell || cell.GetEdifice(map) != null)
                {
                    continue;
                }
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, granite), cell, map);
            }
            if (doorCell.IsValid && doorCell.GetEdifice(map) == null)
            {
                // The king's door: sealed until the regalia is carried, and
                // the bearer of the riddle that starts that errand.
                ThingDef doorDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_KingsDoor") ?? ThingDefOf.Door;
                GenSpawn.Spawn(ThingMaker.MakeThing(doorDef, granite), doorCell, map);
                map.GetComponent<MapComponent_TSC_Barrow>()?.SetThroneDoor(doorCell);
            }
            foreach (IntVec3 cell in room)
            {
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
            }

            ThingDef tomb = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_KingsSarcophagus");
            if (tomb != null)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(tomb), room.CenterCell, map);
            }
            CarveApproach(map, room, doorCell, anchor);
            PlaceLesserTombs(map, room, doorCell);
            SpawnGuard(map, room);
            map.GetComponent<MapComponent_TSC_Barrow>()?.SetThroneRoom(room);
        }

        /// <summary>
        /// The eleven who wore it after him, in rows along the north and
        /// south walls of his chamber.
        ///
        /// They are furniture, and they are the argument: eleven boxes,
        /// eleven names, reigns getting shorter as the numbers get higher,
        /// arranged around the man who is about to explain that every one of
        /// these people thought they would be the exception. Found by
        /// defName so the content stays in XML - adding a twelfth is a def,
        /// not a code change.
        /// </summary>
        /// <summary>
        /// Digs from the door back toward the way up until it meets open
        /// floor.
        ///
        /// The chamber is placed at the point FARTHEST from the stairs and
        /// then walled, which says nothing at all about whether the level's
        /// corridors happen to reach it. They frequently do not, and the
        /// result is a sealed room with a door facing solid rock: the party
        /// stands outside the campaign's last chamber with no way in and no
        /// way to know why. Seen in play.
        ///
        /// The path is dug in two straight legs rather than diagonally, so
        /// every cell of it is orthogonally walkable, and it STOPS at the
        /// first open floor it meets instead of ploughing on to the stairs -
        /// the road panels and the rest of the level are already standing by
        /// the time this runs, and carving is destructive.
        /// </summary>
        private static void CarveApproach(Map map, CellRect room, IntVec3 doorCell, IntVec3 anchor)
        {
            if (!doorCell.IsValid || !anchor.IsValid)
            {
                return;
            }
            // Start OUTSIDE the door, not on it. Heading straight for the
            // stairs from the doorway itself would walk back through the
            // chamber whenever the stairs lie beyond it, digging the approach
            // out of the far wall.
            IntVec3 outward = IntVec3.Zero;
            if (doorCell.x == room.minX)
            {
                outward = new IntVec3(-1, 0, 0);
            }
            else if (doorCell.x == room.maxX)
            {
                outward = new IntVec3(1, 0, 0);
            }
            else if (doorCell.z == room.minZ)
            {
                outward = new IntVec3(0, 0, -1);
            }
            else if (doorCell.z == room.maxZ)
            {
                outward = new IntVec3(0, 0, 1);
            }
            IntVec3 cur = doorCell + outward;
            if (!cur.InBounds(map))
            {
                return;
            }
            GenStep_TSC_CellarLevel.CarveCell(map, cur);
            for (int leg = 0; leg < 2; leg++)
            {
                for (int step = 0; step < 250; step++)
                {
                    int dx = leg == 0 ? System.Math.Sign(anchor.x - cur.x) : 0;
                    int dz = leg == 0 ? 0 : System.Math.Sign(anchor.z - cur.z);
                    if (dx == 0 && dz == 0)
                    {
                        break;
                    }
                    IntVec3 next = new IntVec3(cur.x + dx, 0, cur.z + dz);
                    if (!next.InBounds(map) || next.DistanceToEdge(map) < 3)
                    {
                        return;
                    }
                    cur = next;
                    if (room.Contains(cur))
                    {
                        continue; // still alongside the chamber; nothing to dig yet
                    }
                    if (cur.Standable(map) && !room.ExpandedBy(1).Contains(cur))
                    {
                        return; // met the level's own floor; the room is connected
                    }
                    GenStep_TSC_CellarLevel.CarveCell(map, cur);
                }
            }
        }

        private static readonly string[] TombOrder =
        {
            "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh",
            "Eighth", "Ninth", "Tenth", "Eleventh", "Twelfth",
        };

        private void PlaceLesserTombs(Map map, CellRect room, IntVec3 doorCell)
        {
            // Named rather than scanned-and-sorted: ThingDef defNames may not
            // end in a digit (RimWorld rejects them outright), so there is no
            // numeric suffix left to sort on. The reigns are a sequence, and
            // the sequence lives here.
            List<ThingDef> tombs = new List<ThingDef>();
            foreach (string ordinal in TombOrder)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_KingTomb_" + ordinal);
                if (def != null)
                {
                    tombs.Add(def);
                }
            }
            if (tombs.Count == 0)
            {
                return;
            }
            CellRect inner = room.ContractedBy(1);
            List<IntVec3> slots = new List<IntVec3>();
            // Two rows, feet to the wall, one gap cell between neighbours.
            for (int x = inner.minX + 1; x <= inner.maxX - 1; x += 2)
            {
                slots.Add(new IntVec3(x, 0, inner.maxZ - 1));
                slots.Add(new IntVec3(x, 0, inner.minZ));
            }
            int placed = 0;
            for (int i = 0; i < slots.Count && placed < tombs.Count; i++)
            {
                if (Blocked(map, slots[i], tombs[placed], doorCell))
                {
                    continue;
                }
                GenSpawn.Spawn(ThingMaker.MakeThing(tombs[placed]), slots[i], map);
                placed++;
            }
        }

        /// <summary>
        /// Is this footprint unusable? Checked cell by cell BEFORE spawning:
        /// GenSpawn.Spawn wipes whatever is standing in the way without
        /// complaint, and the thing in the way here would be the king.
        /// </summary>
        private static bool Blocked(Map map, IntVec3 cell, ThingDef def, IntVec3 doorCell)
        {
            if (doorCell.IsValid && cell.InHorDistOf(doorCell, 2.5f))
            {
                return true; // never wall up the only way in
            }
            foreach (IntVec3 part in GenAdj.OccupiedRect(cell, Rot4.North, def.size))
            {
                if (!part.InBounds(map) || !part.Standable(map) || part.GetEdifice(map) != null
                    || part.GetFirstBuilding(map) != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The Honor Guard: factionless, so they are neutral until spoken
        /// to. Excellent plate and the best steel the old kingdom made.
        /// </summary>
        private void SpawnGuard(Map map, CellRect room)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_HonorGuard");
            if (kind == null)
            {
                return;
            }
            int count = guards.RandomInRange;
            CellRect ring = room.ContractedBy(3);
            List<IntVec3> posts = new List<IntVec3>(ring.EdgeCells);
            posts.Shuffle();
            List<Pawn> posted = new List<Pawn>();
            for (int i = 0; i < count && i < posts.Count; i++)
            {
                if (!posts[i].Standable(map))
                {
                    continue;
                }
                Pawn guard = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind, null, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true));
                GenSpawn.Spawn(guard, posts[i], map);
                MapComponent_TSC_Barrow.HoldTheLine(guard);
                map.GetComponent<MapComponent_TSC_Barrow>()?.RegisterGuard(guard);
                posted.Add(guard);
            }
            if (posted.Count == 0)
            {
                return;
            }
            // A LORD, from the moment they exist, while they are still
            // factionless and neutral.
            //
            // They had none at all before, and a spawned pawn with no lord
            // and no faction falls through to the plain humanlike think tree,
            // which for somebody with nowhere to be means wander for a while
            // and then walk off the map. That is what was happening: not
            // fleeing, not looting, not the assault lord - the guard simply
            // had no orders and left, at a stroll, by the stairs, without
            // ever having turned hostile. Seen live standing on the steps.
            //
            // A null faction on a lord is fine (Lord guards for it), and a
            // defend duty is what they were described as doing anyway:
            // standing around the sarcophagus, waiting for somebody to come
            // and answer for the king.
            float radius = Mathf.Max(room.Width, room.Height) / 2f + 2f;
            LordMaker.MakeNewLord(null,
                new LordJob_TSC_HoldTomb(room.CenterCell, radius, Mathf.Max(2f, radius - 4f)),
                map, posted);
        }
    }

    /// <summary>
    /// The mound itself, on the barrow's surface map.
    ///
    /// The arrival letter promises "a long green mound on the high ground,
    /// turf grown over stonework older than any kingdom on the maps, and a
    /// throat of dressed rock facing the sunrise" - and the site was
    /// delivering a stair set in bare dirt, because nothing built the thing
    /// the text describes. This builds it.
    ///
    /// RimWorld has no elevation, so a mound is made of what it is made of:
    /// an elongated oval of rich soil under thick turf, a kerb of granite
    /// showing through where the grass has worn away, and the mouth set in
    /// the EAST end so it faces the sunrise the letter mentions.
    /// </summary>
    public class GenStep_TSC_Mound : GenStep
    {
        public ThingDef mouthDef;
        /// <summary>Long axis (east-west) and short axis, in cells.</summary>
        public int length = 34;
        public int width = 18;

        public override int SeedPart => 615238470;

        public override void Generate(Map map, GenStepParams parms)
        {
            IntVec3 center = map.Center;
            TerrainDef turf = DefDatabase<TerrainDef>.GetNamedSilentFail("SoilRich")
                ?? TerrainDefOf.Soil;
            TerrainDef kerb = DefDatabase<TerrainDef>.GetNamedSilentFail("Gravel")
                ?? TerrainDefOf.Gravel;
            ThingDef grass = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Grass");
            ThingDef chunk = DefDatabase<ThingDef>.GetNamedSilentFail("ChunkGranite");

            float halfLong = length / 2f;
            float halfShort = width / 2f;
            List<IntVec3> shell = new List<IntVec3>();
            foreach (IntVec3 cell in CellRect.CenteredOn(center, length + 4, width + 4).ClipInsideMap(map))
            {
                float dx = (cell.x - center.x) / halfLong;
                float dz = (cell.z - center.z) / halfShort;
                float r = dx * dx + dz * dz;
                if (r > 1.25f)
                {
                    continue;
                }
                Sweep(map, cell);
                if (r > 0.92f)
                {
                    // The kerb: old stonework showing through the turf.
                    map.terrainGrid.SetTerrain(cell, kerb);
                    shell.Add(cell);
                    continue;
                }
                map.terrainGrid.SetTerrain(cell, turf);
                // Turf, thick enough to read as green from the world map in.
                if (grass != null && cell.GetPlant(map) == null && Rand.Chance(0.75f))
                {
                    Plant plant = (Plant)ThingMaker.MakeThing(grass);
                    plant.Growth = Rand.Range(0.65f, 1f);
                    GenSpawn.Spawn(plant, cell, map);
                }
            }
            // A scatter of kerbstones where the mound has slumped.
            if (chunk != null)
            {
                shell.Shuffle();
                int stones = Mathf.Min(14, shell.Count);
                for (int i = 0; i < stones; i++)
                {
                    if (shell[i].Standable(map) && shell[i].GetFirstItem(map) == null)
                    {
                        GenSpawn.Spawn(ThingMaker.MakeThing(chunk), shell[i], map);
                    }
                }
            }
            PlaceMouth(map, center);
        }

        /// <summary>
        /// The throat, in the east end, facing the sunrise. Placed here
        /// rather than by the generic in-structure placer, which had no
        /// structure to find and was dropping it wherever.
        /// </summary>
        private void PlaceMouth(Map map, IntVec3 center)
        {
            if (mouthDef == null)
            {
                return;
            }
            for (int inset = 2; inset < length / 2; inset++)
            {
                IntVec3 cell = new IntVec3(center.x + length / 2 - inset, 0, center.z);
                bool clear = true;
                foreach (IntVec3 part in GenAdj.OccupiedRect(cell, Rot4.North, mouthDef.size))
                {
                    if (!part.InBounds(map) || !part.Standable(map) || part.GetEdifice(map) != null)
                    {
                        clear = false;
                        break;
                    }
                }
                if (!clear)
                {
                    continue;
                }
                foreach (IntVec3 part in GenAdj.OccupiedRect(cell, Rot4.North, mouthDef.size))
                {
                    Sweep(map, part);
                    map.terrainGrid.SetTerrain(part, DefDatabase<TerrainDef>.GetNamedSilentFail("FlagstoneGranite")
                        ?? TerrainDefOf.Gravel);
                }
                GenSpawn.Spawn(ThingMaker.MakeThing(mouthDef), cell, map);
                return;
            }
            // Nowhere clean in the east end: put it at the centre rather
            // than lose the way in entirely.
            GenSpawn.Spawn(ThingMaker.MakeThing(mouthDef), center, map);
        }

        private static void Sweep(Map map, IntVec3 cell)
        {
            foreach (Thing thing in new List<Thing>(cell.GetThingList(map)))
            {
                if (thing.def.category == ThingCategory.Plant
                    || thing.def.category == ThingCategory.Item
                    || thing.def.IsFilth
                    || (thing.def.building != null && thing.def.building.isNaturalRock))
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
        }
    }

    /// <summary>
    /// Act 5's stage manager inside the barrow:
    ///  - the crown starts TALKING on the descent, to a companion by name;
    ///  - the Honor Guard challenge fires at the throne room threshold;
    ///  - the first king answers once the guard is settled;
    ///  - the sarcophagus offers the crown, and the choice.
    /// </summary>
    public class MapComponent_TSC_Barrow : MapComponent
    {
        private const int Interval = 60;
        private List<Pawn> guard = new List<Pawn>();
        private IntVec3 throneCenter = IntVec3.Invalid;
        private int throneRadius;
        private bool temptedYet;
        private int enteredTick = -1;

        public MapComponent_TSC_Barrow(Map map) : base(map)
        {
        }

        public void RegisterGuard(Pawn pawn)
        {
            if (pawn != null && !guard.Contains(pawn))
            {
                guard.Add(pawn);
            }
        }

        public void SetThroneRoom(CellRect room)
        {
            throneCenter = room.CenterCell;
            throneRadius = Mathf.Max(room.Width, room.Height) / 2 + 2;
        }

        /// <summary>The unmaking component builds its annex off this.</summary>
        public IntVec3 ThroneCenter => throneCenter;

        /// <summary>
        /// Where the hall says the throne room belongs: just past its far
        /// end. The throne genstep's own heuristic (farthest standable cell
        /// from the stairs) loses to natural cave tips on this generator,
        /// which hung the crypt off a side tunnel instead of the hall's end.
        /// The hall knows where it ends; the guesswork does not.
        /// </summary>
        private IntVec3 throneAnchor = IntVec3.Invalid;

        public IntVec3 ThroneAnchor => throneAnchor;

        public void SetThroneAnchor(IntVec3 anchor)
        {
            throneAnchor = anchor;
        }

        /// <summary>The king's door: the riddle fires when somebody walks up to it.</summary>
        private IntVec3 throneDoor = IntVec3.Invalid;

        public void SetThroneDoor(IntVec3 cell)
        {
            throneDoor = cell;
        }

        /// <summary>
        /// A gold crown over the king's door, drawn in screen space so it
        /// rides above fog-of-war mods the same as the search rings do.
        /// Shown from the moment the door's cell is uncovered until the
        /// king has spoken: the whole act points at this door, so the map
        /// should too.
        /// </summary>
        public override void MapComponentOnGUI()
        {
            if (!throneDoor.IsValid || Find.CurrentMap != map
                || TSC_BarrowTex.DoorMark == null
                || map.fogGrid.IsFogged(throneDoor))
            {
                return;
            }
            DialogueStateManager state = DialogueStateManager.Current;
            if (state == null || state.IsSet("TSC_KingSpoke"))
            {
                return;
            }
            Vector2 screen = GenMapUI.LabelDrawPosFor(throneDoor);
            const float size = 26f;
            Rect rect = new Rect(screen.x - size / 2f, screen.y - size - 34f, size, size);
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.realtimeSinceStartup * 2.2f);
            GUI.color = new Color(1f, 1f, 1f, pulse);
            GUI.DrawTexture(rect, TSC_BarrowTex.DoorMark);
            GUI.color = Color.white;
        }

        /// <summary>
        /// The regalia errand starts HERE, not at arrival: the party reaches
        /// the sealed door, reads the sealers' riddle, and the quest lands
        /// with the answer half-formed. Skipped when the quest already
        /// exists by any other road (the debug grant, old arrival saves).
        /// </summary>
        private void TryDoorRiddle()
        {
            DialogueStateManager state = DialogueStateManager.Current;
            if (!throneDoor.IsValid || state == null
                || state.IsSet("TSC_DoorRiddleSeen") || state.IsSet("TSC_RegaliaNeeded")
                || state.IsSet("TSC_RegaliaGathered") || state.IsSet("TSC_KingSpoke"))
            {
                return;
            }
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.Downed || !colonist.Position.InHorDistOf(throneDoor, 3.9f))
                {
                    continue;
                }
                state.Set("TSC_DoorRiddleSeen");
                DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_KingsDoor");
                if (def != null)
                {
                    CameraJumper.TryJump(new TargetInfo(throneDoor, map));
                    Find.WindowStack.Add(new Dialog_Conversation(def, colonist, colonist));
                }
                return;
            }
        }

        /// <summary>
        /// Living oath-keepers, standing, HERE.
        ///
        /// The map check is not decoration. A guard who has left the map is
        /// still not dead and not downed, so without it one absent guard kept
        /// the tomb sealed forever with nobody on the map for the player to
        /// deal with: the campaign's last door, held shut by somebody who is
        /// not there. Seen live (an honour guard who ended up a world pawn).
        /// </summary>
        public IEnumerable<Pawn> LivingGuard
        {
            get
            {
                foreach (Pawn pawn in guard)
                {
                    if (pawn != null && !pawn.Dead && !pawn.Downed && pawn.Spawned && pawn.Map == map)
                    {
                        yield return pawn;
                    }
                }
            }
        }

        /// <summary>
        /// The lord they get, and the reason it is not an assault lord.
        ///
        /// LordJob_AssaultColony was the obvious choice and it was wrong: its
        /// state graph is full of doors out of the fight, several of them
        /// opened by parameters that default to true. Stealing (attached
        /// whenever high-value goods are on the map, and the party walks in
        /// carrying five pieces of a crown), kidnapping, and a timeout all
        /// end in LordToil_ExitMap, and the guard simply walked out of the
        /// barrow past the party. Closing them one at a time did not hold.
        ///
        /// LordJob_DefendPoint has exactly one toil and no transitions at
        /// all. There is no path out of it. addFleeToil is another parameter
        /// that defaults to true, so it is named here too.
        ///
        /// It also says what they are for: they hold the chamber. Walk out
        /// and they do not chase you across the barrow, because leaving the
        /// king is the one thing they will not do.
        /// </summary>
        private void MakeGuardLord(List<Pawn> fighters, Faction hostile)
        {
            LordMaker.MakeNewLord(hostile,
                new LordJob_TSC_HoldTomb(throneCenter, throneRadius, Mathf.Max(2f, throneRadius - 4f)),
                map, fighters);
        }

        /// <summary>
        /// Rebuilds a guard lord that is not the one above.
        ///
        /// Lords are saved objects: a game that turned the guard hostile
        /// under the old assault lord keeps it, exit toils and all, and no
        /// amount of fixing the constructor reaches it. This notices a guard
        /// under the wrong kind of lord (or none) and puts the whole standing
        /// guard back under a fresh one.
        /// </summary>
        private void EnsureGuardLord()
        {
            List<Pawn> neutral = null;
            List<Pawn> hostile = null;
            foreach (Pawn pawn in LivingGuard)
            {
                // Checked FIRST, and it evicts. A guard on loan to the
                // player belongs to Command, which drives it by hand and
                // nulls its duty every tick; a lord on top of that means the
                // pawn has a lord and no duty, which vanilla logs once per
                // job tick for as long as the loan lasts. Leaving it in a
                // lord we made earlier would keep that going, so it comes
                // out. Command hands it back when the loan ends, and the
                // sweep picks it up again then.
                if (TSC_ShardTracker.Current?.IsCommanded(pawn) ?? false)
                {
                    pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                    continue;
                }
                if (pawn.GetLord()?.LordJob is LordJob_TSC_HoldTomb)
                {
                    continue;
                }
                if (pawn.Faction != null && pawn.Faction.IsPlayer)
                {
                    continue; // somebody else owns this one at the moment
                }
                // Both cases are broken in the same way and get the same
                // answer: a guard under no lord (the original bug, which let
                // them wander out neutral) and a guard under a stale assault
                // lord saved before this was fixed.
                if (pawn.Faction == null)
                {
                    (neutral = neutral ?? new List<Pawn>()).Add(pawn);
                }
                else
                {
                    (hostile = hostile ?? new List<Pawn>()).Add(pawn);
                }
            }
            Regroup(neutral);
            Regroup(hostile);
        }

        /// <summary>
        /// Finishes a "wear it" ending that threw halfway through.
        ///
        /// The effect sets its flags first and hands the crown over second, so
        /// an exception in between leaves a game that has chosen an ending and
        /// received none of it. If the ending is recorded but no crown exists
        /// on this map or on anybody's head, hand it over now and roll the
        /// card that never played.
        /// </summary>
        private bool wearHealed;

        private void HealWearEnding()
        {
            if (wearHealed || !DialogueStateManager.Current.IsSet("TSC_Ending_wear"))
            {
                return;
            }
            wearHealed = true;
            ThingDef crownDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_ShatteredCrown");
            if (crownDef == null || map.listerThings.ThingsOfDef(crownDef).Count > 0)
            {
                return;
            }
            Pawn heir = null;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                List<Apparel> worn = colonist.apparel?.WornApparel;
                for (int i = 0; worn != null && i < worn.Count; i++)
                {
                    if (worn[i].def == crownDef)
                    {
                        return; // somebody is already wearing it; nothing broke
                    }
                }
                if (heir == null && !colonist.Downed)
                {
                    heir = colonist;
                }
            }
            if (heir == null)
            {
                wearHealed = false; // try again when somebody is standing
                return;
            }
            DialogueEffect_TSC_CrownEnding.CrownOnHead(heir, map);
            TSC_TitleCardManager.Show("The Shattered Crown", "A New King");
            TSC_QuestSignals.Send("TSC_Act5_Crown", "TSC_CrownResolved");
        }

        /// <summary>
        /// Saves generated while vanilla PlaceCaveExit was in the interior's
        /// genstep list hold a stray generic CaveExit in some side cave, on
        /// top of the real TSC_StairsUp: two ways up, one of them a lie.
        /// Once the real stair exists, the stray goes.
        /// </summary>
        private bool caveExitHealed;

        private void HealStrayCaveExit()
        {
            if (caveExitHealed)
            {
                return;
            }
            caveExitHealed = true;
            ThingDef stairs = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_StairsUp");
            ThingDef stray = DefDatabase<ThingDef>.GetNamedSilentFail("CaveExit");
            if (stairs == null || stray == null
                || map.listerThings.ThingsOfDef(stairs).Count == 0)
            {
                return;
            }
            List<Thing> strays = new List<Thing>(map.listerThings.ThingsOfDef(stray));
            foreach (Thing thing in strays)
            {
                if (!thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        /// <summary>Puts a set of loose guards back under one defend lord.</summary>
        private void Regroup(List<Pawn> loose)
        {
            if (loose == null || loose.Count == 0)
            {
                return;
            }
            foreach (Pawn pawn in loose)
            {
                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                pawn.jobs?.StopAll();
            }
            MakeGuardLord(loose, loose[0].Faction);
        }

        /// <summary>
        /// They do not rout.
        ///
        /// The lord-level exits are all shut off (no timeout, no flee, no
        /// stealing), but NONE of that governs a pawn deciding on its own
        /// account that it has had enough: a hurt pawn with canFleeIndividual
        /// set panics, sets exitMapAfterTick and runs for the nearest way off
        /// the map, lord or no lord. That is what was actually happening -
        /// the guard poured out of the throne room past the party and up the
        /// stairs, one at a time, as they got hurt.
        ///
        /// An oath to stand over a king's body until somebody answers for him
        /// is not a thing you abandon at 30% health. Applied on spawn, again
        /// when they turn hostile, and enforced on the roster sweep so a save
        /// where they are already halfway to the door recovers: the flight is
        /// cancelled and they go back to it.
        /// </summary>
        public static void HoldTheLine(Pawn pawn)
        {
            if (pawn?.mindState == null || pawn.Dead)
            {
                return;
            }
            pawn.mindState.canFleeIndividual = false;
            pawn.mindState.exitMapAfterTick = -99999;
            if (pawn.InMentalState && pawn.MentalStateDef == MentalStateDefOf.PanicFlee)
            {
                pawn.mindState.mentalStateHandler.CurState?.RecoverFromState();
            }
            if (pawn.CurJobDef == JobDefOf.Flee || pawn.CurJobDef == JobDefOf.FleeAndCower)
            {
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        /// <summary>
        /// Forgets guards who have left, and tells their lord so.
        ///
        /// A pawn that leaves the map without its lord being told stays in
        /// that lord's ownedPawns forever, which vanilla notices once a
        /// second and complains about ("owns a free world pawn"). Whatever
        /// took them off the map - fleeing, being carried off, a mod - the
        /// answer from here is the same: they are no longer part of this.
        /// </summary>
        private void ReconcileGuard()
        {
            for (int i = guard.Count - 1; i >= 0; i--)
            {
                Pawn pawn = guard[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    guard.RemoveAt(i);
                    continue;
                }
                if (pawn.Spawned && pawn.Map == map)
                {
                    HoldTheLine(pawn);
                    continue;
                }
                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ExitedMap);
                guard.RemoveAt(i);
            }
            // Are there oath-keepers on their feet who are not at war with
            // the party? The ending scenes ask, and they used to guess: the
            // entombing described the guard sealing the barrow even when the
            // company had killed every one of them on the way in. Recomputed
            // rather than latched, because both halves can change - they can
            // die after yielding, and the scenes fire much later.
            bool standing = false;
            foreach (Pawn unused in LivingGuard)
            {
                standing = true;
                break;
            }
            if (standing && DialogueStateManager.Current.IsSet("TSC_HonorGuardYielded"))
            {
                DialogueStateManager.Current.Set("TSC_HonorGuardStands");
            }
            else
            {
                DialogueStateManager.Current.Clear("TSC_HonorGuardStands");
            }
        }

        /// <summary>The challenge went badly: the post is defended.</summary>
        public void GuardFight()
        {
            Faction hostile = TSC_BanditFactionUtility.Get();
            List<Pawn> fighters = new List<Pawn>(LivingGuard);
            if (hostile == null || fighters.Count == 0)
            {
                return;
            }
            foreach (Pawn pawn in fighters)
            {
                pawn.SetFaction(hostile);
                HoldTheLine(pawn);
            }
            MakeGuardLord(fighters, hostile);
            Messages.Message("The Honor Guard closes ranks around their king.",
                fighters[0], MessageTypeDefOf.ThreatBig, historical: false);
        }

        /// <summary>The challenge was answered: they stand aside, and stay standing.</summary>
        public void GuardYield()
        {
            foreach (Pawn pawn in LivingGuard)
            {
                pawn.jobs?.StopAll();
            }
            DialogueStateManager.Current.Set("TSC_HonorGuardYielded");
            Messages.Message("The Honor Guard grounds its spears and steps back from the tomb.",
                MessageTypeDefOf.PositiveEvent, historical: false);
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            // EVERY map gets one of these components; only the barrow's
            // interior has a throne room. Without this gate the crown's
            // offer fired on whatever map the party happened to be
            // carrying shards on - seen live, three floors under the
            // monastery, an act early.
            if (!throneCenter.IsValid)
            {
                return;
            }
            ReconcileGuard();
            EnsureGuardLord();
            HealWearEnding();
            HealStrayCaveExit();
            // The fifth ending's gate, recomputed like TSC_HonorGuardStands:
            // dialogue options can only branch on flags, so the gate IS one.
            TSC_Unmaking.MaintainReadyFlag();
            if (map.mapPawns.FreeColonistsSpawnedCount == 0)
            {
                return;
            }
            if (enteredTick < 0)
            {
                enteredTick = Find.TickManager.TicksGame;
                // Heal for saves where the offer already misfired elsewhere:
                // if it was "seen" before the barrow was ever entered, it
                // was seen in the wrong place. Give it back.
                if (!DialogueStateManager.Current.IsSet("TSC_BarrowEntered")
                    && DialogueStateManager.Current.IsSet("TSC_CrownTempted"))
                {
                    DialogueStateManager.Current.Clear("TSC_CrownTempted");
                    Log.Message("[The Shattered Crown] The crown's offer had fired outside the barrow; it is armed again for the descent.");
                }
                DialogueStateManager.Current.Set("TSC_BarrowEntered");
            }
            if (Find.WindowStack.WindowOfType<Dialog_Conversation>() != null)
            {
                return;
            }
            TryDoorRiddle();
            TryTempt();
            TryChallenge();
        }

        /// <summary>
        /// The crown stops showing visions and starts bargaining, by name,
        /// with whoever is carrying it. It waits until the company is a
        /// little way in: the offer lands better underground than at the
        /// door.
        /// </summary>
        private bool TryTempt(bool force = false)
        {
            if (temptedYet || DialogueStateManager.Current.IsSet("TSC_CrownTempted"))
            {
                return false;
            }
            // The hour-after-entering timer was the only trigger, and a party
            // that goes straight down reaches the guard well inside the hour:
            // the crown then made its offer after the confrontation, or in
            // the middle of it, instead of on the way. Approaching the tomb
            // arms it too, so whichever happens first - an hour of picking
            // through the barrow, or walking up on the chamber - the offer
            // comes before the door.
            if (!force && Find.TickManager.TicksGame - enteredTick < 2500 && !NearingTheTomb())
            {
                return false;
            }
            Pawn carrier = null;
            foreach (ThingDef shardDef in TSC_Shards.AllDefs)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn.inventory?.innerContainer != null
                        && pawn.inventory.innerContainer.Contains(shardDef))
                    {
                        carrier = pawn;
                        break;
                    }
                }
                if (carrier != null)
                {
                    break;
                }
            }
            if (carrier == null)
            {
                return false;
            }
            DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_CrownOffer");
            if (def == null)
            {
                return false;
            }
            temptedYet = true;
            DialogueStateManager.Current.Set("TSC_CrownTempted");
            Find.WindowStack.Add(new Dialog_Conversation(def, carrier, carrier));
            return true;
        }

        /// <summary>Anyone close enough to the chamber that the tomb is the next thing they meet.</summary>
        private bool NearingTheTomb()
        {
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (!colonist.Downed && colonist.Position.InHorDistOf(throneCenter, throneRadius + 18f))
                {
                    return true;
                }
            }
            return false;
        }

        private void TryChallenge()
        {
            if (!throneCenter.IsValid
                || DialogueStateManager.Current.IsSet("TSC_HonorGuardMet"))
            {
                return;
            }
            // The guard challenges CLAIMANTS, and only claimants who have
            // actually come THROUGH the king's door: no regalia, no
            // challenge (the sealed door and its riddle do the redirecting),
            // and standing outside the threshold does not count as arriving.
            DialogueStateManager state = DialogueStateManager.Current;
            if (state.IsSet("TSC_RegaliaNeeded") && !state.IsSet("TSC_RegaliaGathered"))
            {
                return;
            }
            bool anyGuard = false;
            foreach (Pawn unused in LivingGuard)
            {
                anyGuard = true;
                break;
            }
            if (!anyGuard)
            {
                DialogueStateManager.Current.Set("TSC_HonorGuardMet");
                DialogueStateManager.Current.Set("TSC_HonorGuardYielded"); // nobody left to object
                return;
            }
            // INSIDE the chamber, not merely near it: the throne radius
            // reaches through the walls, and the challenge belongs to the
            // moment somebody actually crosses the king's threshold.
            Room throne = throneCenter.GetRoom(map);
            Pawn near = null;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (!colonist.Downed && throne != null
                    && colonist.Position.GetRoom(map) == throne)
                {
                    near = colonist;
                    break;
                }
            }
            DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_HonorGuard");
            if (near == null || def == null)
            {
                return;
            }
            // Last guarantee of the order. If the party got here anyway - a
            // sprint down an open corridor, a translocation, a doorway right
            // outside the chamber - the crown speaks first and the guard
            // waits a tick. The offer is the setup for this scene; playing
            // them the other way round makes both of them read wrong.
            if (TryTempt(force: true))
            {
                return;
            }
            DialogueStateManager.Current.Set("TSC_HonorGuardMet");
            Pawn speaker = null;
            foreach (Pawn pawn in LivingGuard)
            {
                speaker = pawn;
                break;
            }
            if (speaker != null)
            {
                CameraJumper.TryJump(speaker);
            }
            Find.WindowStack.Add(new Dialog_Conversation(def, speaker ?? near, near));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref guard, "guard", LookMode.Reference);
            Scribe_Values.Look(ref throneCenter, "throneCenter", IntVec3.Invalid);
            Scribe_Values.Look(ref throneAnchor, "throneAnchor", IntVec3.Invalid);
            Scribe_Values.Look(ref throneRadius, "throneRadius");
            Scribe_Values.Look(ref wearHealed, "wearHealed");
            Scribe_Values.Look(ref caveExitHealed, "caveExitHealed");
            Scribe_Values.Look(ref throneDoor, "throneDoor", IntVec3.Invalid);
            Scribe_Values.Look(ref temptedYet, "temptedYet");
            Scribe_Values.Look(ref enteredTick, "enteredTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && guard == null)
            {
                guard = new List<Pawn>();
            }
        }
    }

    /// <summary>
    /// The last courier, on the barrow's SURFACE map. He arrives when the
    /// party does, because the crown finally stopped putting rivers in his
    /// way. The scene hands over the fifth shard; the shard itself is
    /// placed into the talker's inventory when the scene closes, so it
    /// changes hands by choice rather than by looting a corpse.
    /// </summary>
    public class MapComponent_TSC_Courier : MapComponent
    {
        private const int Interval = 60;
        private bool checkedMap;
        private bool isBarrow;
        private bool sceneOpened;
        private bool shardGiven;
        private Pawn talker;

        public MapComponent_TSC_Courier(Map map) : base(map)
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
                        if (site.parts[i].def?.defName == "TSC_FirstKingBarrow")
                        {
                            isBarrow = true;
                            break;
                        }
                    }
                }
            }
            if (!isBarrow)
            {
                return;
            }
            // The handover happens when the scene closes, not mid-scene:
            // the player should read his last line before the weight of a
            // fifth piece lands in somebody's pack.
            if (sceneOpened && !shardGiven
                && Find.WindowStack.WindowOfType<Dialog_Conversation>() == null
                && DialogueStateManager.Current.IsSet("TSC_MetCourier"))
            {
                shardGiven = true;
                GiveShard();
                return;
            }
            if (sceneOpened || DialogueStateManager.Current.IsSet("TSC_MetCourier"))
            {
                HealDownedCourier();
                return;
            }
            if (Find.WindowStack.WindowOfType<Dialog_Conversation>() != null)
            {
                return;
            }
            ThingDef mouthDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_KingsBarrowMouth");
            Thing mouth = null;
            if (mouthDef != null)
            {
                List<Thing> mouths = map.listerThings.ThingsOfDef(mouthDef);
                if (mouths.Count > 0)
                {
                    mouth = mouths[0];
                }
            }
            IntVec3 anchor = mouth?.Position ?? map.Center;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.Downed || !colonist.Position.InHorDistOf(anchor, 12f))
                {
                    continue;
                }
                DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_LastCourier");
                if (def == null)
                {
                    return;
                }
                // He arrives as a PAWN, walking up the slope, so the scene
                // has his face in it and the player can watch him sit down
                // afterward instead of reading that he did.
                Pawn aled = SpawnCourier(anchor);
                sceneOpened = true;
                talker = colonist;
                if (aled != null)
                {
                    CameraJumper.TryJump(aled);
                }
                Find.WindowStack.Add(new Dialog_Conversation(def, aled ?? colonist, colonist));
                return;
            }
        }

        private bool courierHealed;

        /// <summary>
        /// Saves where the handover already happened but left Aled lying
        /// downed and alive: his plot armor caught the authored death and
        /// turned it into a knockout. Finishes what the scene meant to do.
        /// </summary>
        private void HealDownedCourier()
        {
            if (courierHealed || !DialogueStateManager.Current.IsSet("TSC_MetCourier"))
            {
                return;
            }
            courierHealed = true;
            NamedNpcDef def = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Courier");
            Pawn aled = def != null ? DialogueStateManager.Current.GetNamedNpcIfExists(def) : null;
            if (aled != null && !aled.Dead)
            {
                DialogueEffect_TSC_CourierEnds.Finish(aled);
            }
        }

        /// <summary>
        /// Aled, on the slope below the mouth, facing the way the party
        /// came. Villager faction so nobody shoots him and he stays put
        /// after the handover.
        /// </summary>
        private Pawn SpawnCourier(IntVec3 anchor)
        {
            NamedNpcDef def = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Courier");
            if (def == null)
            {
                return null;
            }
            Pawn aled = DialogueStateManager.Current.GetOrGenerateNamedNpc(def, GenStep_TSC_Village.VillagerFaction());
            if (aled == null || aled.Dead)
            {
                return null;
            }
            // "Eight hundred and eleven years out from a gate that is not
            // there any more" - and the character card should agree with
            // the man: 27 when the chancery sealed his warrant, 811 on the
            // road since. Biological AND chronological: the road did not
            // preserve him, it just refused to let him finish. Safe to set
            // this high - age-driven disease rolls happen at generation
            // (before this) and on birthdays (which he will not live to
            // see), so the number is cosmetic everywhere except the soul.
            if (aled.ageTracker != null && aled.ageTracker.AgeBiologicalYears < 800)
            {
                long ageTicks = (811L + 27L) * GenDate.TicksPerYear;
                aled.ageTracker.AgeBiologicalTicks = ageTicks;
                aled.ageTracker.AgeChronologicalTicks = ageTicks;
            }
            if (aled.Spawned)
            {
                return aled.Map == map ? aled : null;
            }
            IntVec3 cell = CellFinder.StandableCellNear(anchor, map, 8f);
            if (!cell.IsValid)
            {
                cell = anchor;
            }
            GenSpawn.Spawn(aled, cell, map);
            aled.Drawer?.renderer?.SetAllGraphicsDirty();
            if (aled.Faction != null && aled.GetLord() == null)
            {
                LordMaker.MakeNewLord(aled.Faction, new LordJob_DefendPoint(cell), map,
                    new List<Pawn> { aled });
            }
            return aled;
        }

        private void GiveShard()
        {
            ThingDef shardDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard_Road");
            if (shardDef == null || talker == null)
            {
                return;
            }
            Thing shard = ThingMaker.MakeThing(shardDef);
            if (talker.inventory == null || !talker.inventory.innerContainer.TryAdd(shard))
            {
                GenPlace.TryPlaceThing(shard, talker.PositionHeld, map, ThingPlaceMode.Near);
            }
            Find.LetterStack.ReceiveLetter(
                "Five of five",
                "The fifth piece of the crown comes out of a courier's satchel and into the company's hands, "
                + "handed over rather than taken. What is left of Aled of the fifth road is lying on the grass "
                + "behind them, and the satchel beside him has outlasted its owner.\n\n"
                + "Everything the shards have ever wanted is now in one pack, at the mouth of the barrow it "
                + "was made for. The door is right there.",
                LetterDefOf.PositiveEvent, talker);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sceneOpened, "sceneOpened");
            Scribe_Values.Look(ref shardGiven, "shardGiven");
            Scribe_Values.Look(ref courierHealed, "courierHealed");
            Scribe_References.Look(ref talker, "talker");
        }
    }

    /// <summary>
    /// DSL effect courier_ends(): the years arrive.
    ///
    /// The crown kept Aled walking for eight centuries and kept the bill.
    /// He hands his piece over, it lets go of him, and the whole account
    /// falls due at once - so the pawn takes the hediff that explains it,
    /// dies, and the corpse is aged past dessication on the spot. A player
    /// who clicks the husk afterward gets the reason in the health tab
    /// rather than an unusually dry body with no story attached.
    /// </summary>
    public class DialogueEffect_TSC_CourierEnds : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            // Looked up by def rather than taken from the scene's npc slot:
            // this ends a specific character, and it should end HIM whichever
            // side of the conversation he is sitting on.
            NamedNpcDef def = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Courier");
            Pawn aled = def != null ? DialogueStateManager.Current.GetNamedNpcIfExists(def) : null;
            Finish(aled ?? context.npc);
        }

        /// <summary>The handover's aftermath, also reachable as a save repair.</summary>
        public static void Finish(Pawn aled)
        {
            if (aled == null || aled.Dead)
            {
                return;
            }
            HediffDef years = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_YearsCollected");
            if (years != null && !aled.health.hediffSet.HasHediff(years))
            {
                aled.health.AddHediff(years);
            }
            Map map = aled.MapHeld;
            IntVec3 at = aled.PositionHeld;
            aled.GetLord()?.Notify_PawnLost(aled, PawnLostCondition.Killed);
            // Through the plot-armor bypass: he is a protected character, and
            // without this the Kill prefix downs him alive instead - which is
            // exactly what happened in play.
            TSC_PlotArmor.ScriptedKill(aled);
            Corpse corpse = aled.Corpse;
            if (corpse != null)
            {
                // Straight to dessicated: he was eight hundred years dead
                // already, he just had not stopped walking.
                CompRottable rot = corpse.TryGetComp<CompRottable>();
                if (rot != null)
                {
                    rot.RotProgress = 9999999f;
                }
            }
            if (map != null)
            {
                FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 2.5f,
                    new UnityEngine.Color(0.72f, 0.66f, 0.5f));
            }
            Find.LetterStack.ReceiveLetter(
                "Aled of the fifth road",
                "The last courier set his piece down in the company's hands and eight hundred and eleven "
                + "years arrived to collect. What is on the turf outside the barrow is dry and light and "
                + "very old, and it stopped being Aled somewhere around the third breath.\n\n"
                + "He was not being kept alive. He was being kept walking. Nobody ever said alive.",
                LetterDefOf.NeutralEvent,
                corpse != null && corpse.Spawned ? (LookTargets)corpse : null);
        }
    }

    /// <summary>DSL effect guard_fight(): the Honor Guard defends the post.</summary>
    public class DialogueEffect_TSC_GuardFight : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            context.interactor?.MapHeld?.GetComponent<MapComponent_TSC_Barrow>()?.GuardFight();
        }
    }

    /// <summary>DSL effect guard_yield(): the challenge was answered.</summary>
    public class DialogueEffect_TSC_GuardYield : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            context.interactor?.MapHeld?.GetComponent<MapComponent_TSC_Barrow>()?.GuardYield();
        }
    }

    /// <summary>Textures for the barrow's GUI marks, loaded on the main thread.</summary>
    [StaticConstructorOnStartup]
    public static class TSC_BarrowTex
    {
        public static readonly Texture2D DoorMark =
            ContentFinder<Texture2D>.Get("UI/TSC_DoorMark", reportFailure: false);
    }

    /// <summary>
    /// The king's door: sealed until the answer to its riddle is carried.
    /// No bar and no lock to pick - it was not made to be forced, it was
    /// made to be answered. Opens like any door once the regalia is
    /// gathered (or, legacy-safe, once the king has already spoken).
    /// </summary>
    public class Building_TSC_KingsDoor : Building_Door
    {
        public override bool PawnCanOpen(Pawn p)
        {
            DialogueStateManager state = DialogueStateManager.Current;
            if (state != null && !state.IsSet("TSC_RegaliaGathered") && !state.IsSet("TSC_KingSpoke"))
            {
                return false;
            }
            return base.PawnCanOpen(p);
        }
    }

    /// <summary>
    /// The first king's sarcophagus. Opening it is the end of the campaign,
    /// so it is gated on the guard being settled one way or the other: the
    /// king does not receive claimants over the shoulders of his own men.
    /// </summary>
    public class Building_TSC_KingsTomb : Building
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
            MapComponent_TSC_Barrow barrow = Map?.GetComponent<MapComponent_TSC_Barrow>();
            bool guardStanding = false;
            if (barrow != null && !DialogueStateManager.Current.IsSet("TSC_HonorGuardYielded"))
            {
                foreach (Pawn unused in barrow.LivingGuard)
                {
                    guardStanding = true;
                    break;
                }
            }
            if (guardStanding)
            {
                yield return new FloatMenuOption("Open the sarcophagus (the Honor Guard is between you and it)", null);
                yield break;
            }
            // The regalia gate. Possession, not the quest flag: a piece
            // dropped in a river after the quest closed is still not on the
            // stone. TSC_RegaliaNeeded arms the gate at barrow arrival, so
            // saves from before the regalia existed keep the old, open tomb;
            // TSC_KingSpoke disarms it - once the pieces are laid, they are
            // consumed, and the gate must not demand them a second time.
            if (DialogueStateManager.Current.IsSet("TSC_RegaliaNeeded")
                && !DialogueStateManager.Current.IsSet("TSC_KingSpoke"))
            {
                string missing = MissingRegalia();
                if (missing != null)
                {
                    yield return new FloatMenuOption(
                        $"Open the sarcophagus (the king will not wake without {missing})", null);
                    yield break;
                }
            }
            if (DialogueStateManager.Current.IsSet("TSC_CrownClaimed"))
            {
                yield break;
            }
            FloatMenuOption open = new FloatMenuOption("Open the sarcophagus", delegate
            {
                JobDef job = DefDatabase<JobDef>.GetNamedSilentFail("TSC_OpenKingsTomb");
                if (job != null)
                {
                    selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(job, this), JobTag.Misc);
                }
            });
            yield return FloatMenuUtility.DecoratePrioritizedTask(open, selPawn, this);
        }

        /// <summary>The three keepings, in the order the quest names them.</summary>
        public static readonly string[] RegaliaDefNames =
        {
            "TSC_KingsRing", "TSC_KingsAmulet", "TSC_KingsStaff",
        };

        /// <summary>Labels of the pieces the party does not hold, or null when all three are in hand.</summary>
        public static string MissingRegalia()
        {
            List<string> missing = null;
            foreach (string defName in RegaliaDefNames)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def != null && TSC_Possession.Count(def) < 1)
                {
                    (missing = missing ?? new List<string>()).Add(def.label);
                }
            }
            return missing == null ? null : string.Join(", ", missing);
        }

        /// <summary>
        /// The waking: one of each piece leaves the party's hands for the
        /// stone. This is what the fetch was FOR, so they are consumed, not
        /// pocketed - a king woken by his own regalia does not hand it back.
        /// </summary>
        public static void LayRegalia(Pawn opener)
        {
            foreach (string defName in RegaliaDefNames)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    continue;
                }
                Thing piece = FindHeld(def);
                if (piece != null)
                {
                    piece.holdingOwner?.Remove(piece);
                    if (!piece.Destroyed)
                    {
                        piece.Destroy(DestroyMode.Vanish);
                    }
                }
            }
            Messages.Message("The ring, the amulet and the staff are laid on the stone. Something under the lid takes a breath it has been owed for nine hundred years.",
                opener, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        /// <summary>One held instance of the def, anywhere in the party: inventory, equipment, or loose on the opener's map.</summary>
        private static Thing FindHeld(ThingDef def)
        {
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        if (thing.def == def)
                        {
                            return thing;
                        }
                    }
                }
                if (pawn.equipment?.AllEquipmentListForReading != null)
                {
                    foreach (Thing thing in pawn.equipment.AllEquipmentListForReading)
                    {
                        if (thing.def == def)
                        {
                            return thing;
                        }
                    }
                }
            }
            foreach (Map map in Find.Maps)
            {
                List<Thing> loose = map.listerThings.ThingsOfDef(def);
                if (loose.Count > 0)
                {
                    return loose[0];
                }
            }
            return null;
        }
    }

    public class JobDriver_TSC_OpenKingsTomb : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil heave = Toils_General.Wait(240);
            heave.WithProgressBarToilDelay(TargetIndex.A);
            yield return heave;
            Toil open = ToilMaker.MakeToil("TSC_OpenKingsTomb");
            open.initAction = delegate
            {
                if (DialogueStateManager.Current.IsSet("TSC_KingSpoke"))
                {
                    // Refusal-with-retry: until an ending is actually chosen,
                    // lifting the lid resumes the conversation. Also mends
                    // the older soft-lock where closing the window mid-scene
                    // orphaned the campaign's last choice.
                    if (!DialogueStateManager.Current.IsSet("TSC_CrownClaimed"))
                    {
                        DialogueDef again = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_FirstKing");
                        if (again != null)
                        {
                            Find.WindowStack.Add(new Dialog_Conversation(again, pawn, pawn));
                        }
                    }
                    return;
                }
                if (DialogueStateManager.Current.IsSet("TSC_RegaliaNeeded"))
                {
                    Building_TSC_KingsTomb.LayRegalia(pawn);
                }
                DialogueStateManager.Current.Set("TSC_KingSpoke");
                DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_FirstKing");
                if (def != null)
                {
                    Find.WindowStack.Add(new Dialog_Conversation(def, pawn, pawn));
                }
            };
            open.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return open;
        }
    }

    /// <summary>
    /// DSL effect crown_ending(kind): the campaign's last beat. Records the
    /// choice, hands over the crown (or does not), and rolls the card.
    /// </summary>
    public class DialogueEffect_TSC_CrownEnding : DialogueEffect
    {
        /// <summary>wear | scatter | entomb | leave</summary>
        public string kind = "entomb";

        public override void Apply(DialogueContext context)
        {
            Pawn actor = context.interactor;
            Map map = actor?.MapHeld;
            DialogueStateManager.Current.Set("TSC_CrownClaimed");
            DialogueStateManager.Current.Set("TSC_Ending_" + kind);
            // ONLY wearing it produces the crown. Scattering used to hand it
            // over as well, which contradicted its own scene (the pieces
            // leave with five riders) and, now that the whole crown grants
            // wishes, would have given the engine to the player who chose to
            // give it away.
            if (kind == "wear")
            {
                CrownOnHead(actor, map);
            }
            if (kind != "wear")
            {
                // Everything except wearing it consumes the pieces.
                ConsumeShards();
            }
            string small = "The Shattered Crown";
            string big = kind == "wear" ? "A New King"
                : kind == "scatter" ? "Five New Roads"
                : kind == "entomb" ? "The Quiet Grave"
                : kind == "unmake" ? "A Thing That Wanted"
                : "The Road Home";
            if (kind == "unmake")
            {
                Find.LetterStack.ReceiveLetter("The unmaking",
                    "The crown is not broken. Broken was tried, and it waited out broken for eight hundred years. The crown is GONE: unmade in the room it was made in, by the hand it was made for, and the silence where the wanting used to be goes down past the bottom of anything.\n\n"
                    + "The king asked for one thing afterward, and the company did it: the ring, the amulet and the staff came off the stone lid. He was still saying thank you when he stopped.\n\n"
                    + "Somewhere under a city, a white room has gone quiet. Somewhere on five roads, nothing at all is being carried.",
                    LetterDefOf.PositiveEvent);
            }
            TSC_TitleCardManager.Show(small, big);
            TSC_QuestSignals.Send("TSC_Act5_Crown", "TSC_CrownResolved");
        }

        /// <summary>
        /// Puts it on. The crown is apparel and its offer is made to whoever
        /// is wearing it, so the pawn who said yes should be wearing it when
        /// the scene ends rather than carrying it in a pack.
        /// </summary>
        public static void CrownOnHead(Pawn actor, Map map)
        {
            ThingDef crownDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_ShatteredCrown");
            if (crownDef == null || actor == null)
            {
                return;
            }
            Thing crown = ThingMaker.MakeThing(crownDef);
            // crownDef.apparel is checked, not assumed. It was null once (the
            // apparel block had been written into the wrong ThingDef) and
            // Pawn_ApparelTracker.Wear dereferences it without a guard, so the
            // NRE came out of the middle of Apply() and took the rest of the
            // ending with it: no crown, no letter, no title card, no quest
            // resolution. A content mistake should cost the player a hat, not
            // the last scene of the campaign.
            if (crown is Apparel apparel && actor.apparel != null && crownDef.apparel != null)
            {
                // locked: vanilla's own "this does not come off" flag, which
                // hides the drop button and makes the outfit system leave it
                // alone. TSC_CrownLock is the harder guarantee behind it.
                actor.apparel.Wear(apparel, dropReplacedApparel: true, locked: true);
            }
            else if (actor.inventory == null || !actor.inventory.innerContainer.TryAdd(crown))
            {
                GenPlace.TryPlaceThing(crown, actor.PositionHeld, map, ThingPlaceMode.Near);
            }
            Find.LetterStack.ReceiveLetter(
                "The crown, worn",
                $"{actor.LabelShortCap} is wearing the crown of Aldruin, whole, on the head it was not made for.\n\n"
                + "It will give the wearer anything asked of it, at once, as often as asked. It charges for "
                + "what it hands over the only way it knows how, and it takes payment out of the wearer's own "
                + "years, on the spot, in proportion to what was wanted.\n\n"
                + "It does not come off. Nobody has ever got one off a living head, and the twelve in the "
                + "ground here were all buried wearing theirs.",
                LetterDefOf.NeutralEvent, actor);
        }

        private static void ConsumeShards()
        {
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn.inventory?.innerContainer == null)
                {
                    continue;
                }
                List<Thing> held = new List<Thing>(pawn.inventory.innerContainer);
                foreach (Thing thing in held)
                {
                    if (TSC_Shards.IsShard(thing.def))
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }
        }
    }
}
