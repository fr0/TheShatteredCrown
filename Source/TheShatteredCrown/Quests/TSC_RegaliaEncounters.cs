using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The encounters that make the three regalia fetches (TSC_Act5Regalia)
    /// more than three fights:
    ///  - the chantry's grave robbers PARLEY instead of charging (this
    ///    genstep), riding the crypt parley component;
    ///  - the wardens' hall still has working wards, and they charge in the
    ///    same coin the crown does (MapComponent_TSC_HallWard);
    ///  - the road home has riders on it who know what the party is
    ///    carrying (WorldComponent_TSC_RegaliaRoad);
    ///  - and two small dialogue effects: calming a manhunting den, and an
    ///    ambush called down on a caravan mid-conversation.
    /// </summary>
    public class GenStep_TSC_ChantryRobbers : GenStep
    {
        public IntRange count = new IntRange(4, 6);

        public override int SeedPart => 736251908;

        public override void Generate(Map map, GenStepParams parms)
        {
            MapComponent_TSC_CryptParley parley = map.GetComponent<MapComponent_TSC_CryptParley>();
            PawnKindDef brigand = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand");
            PawnKindDef archer = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Archer");
            if (parley == null || brigand == null)
            {
                return;
            }
            parley.Configure("TSC_Dialogue_ChantryRobbers", "TSC_ChantryRobbersMet",
                "The grave robbers take up picks and blades!",
                "The grave robbers want no more nights next to the thing that ticks. They clear out.");
            List<IntVec3> interior = GenStep_TSC_PlaceInStructure.InteriorCells(map);
            IntVec3 post = interior.Count > 0 ? interior.RandomElement() : map.Center;
            int n = count.RandomInRange;
            for (int i = 0; i < n; i++)
            {
                PawnKindDef kind = archer != null && i % 3 == 2 ? archer : brigand;
                // Factionless on purpose: a parley crew belongs to nobody
                // until the words run out (MapComponent_TSC_CryptParley).
                Pawn robber = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind, null, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(post, map, 4);
                if (GenSpawn.Spawn(robber, cell, map) is Pawn spawned)
                {
                    parley.Register(spawned, isLeader: i == 0);
                }
            }
        }
    }

    /// <summary>
    /// One castle, one rect big enough to hold it. Vanilla's ruin scatter
    /// chops the map into ~45-cell regions, SHRINKS each around water and
    /// bad ground (floor 14, minus random contraction), and then attempts
    /// the full layout in every surviving rect - so the TSC_Castle
    /// layout's required 8x8 great hall fails whenever its rect rolled
    /// small: "Layout failed to spawn all required rooms", seen twice at
    /// the wardens' hall. Fill percent does not fix that (it only decides
    /// how MANY undersized rects try). This places a single structure in a
    /// rect searched for at known-good sizes, and only falls back to the
    /// vanilla scatter if the map genuinely has no room anywhere.
    /// </summary>
    public class GenStep_TSC_AncientRuinsLarge : GenStep_AncientRuins
    {
        private static readonly int[] TrySizes = { 42, 38, 34, 30 };

        public override void GenerateRuins(Map map, GenStepParams parms, FloatRange mapFillPercentRange)
        {
            structureSketches.Clear();
            foreach (int size in TrySizes)
            {
                int margin = size / 2 + 6;
                if (map.Size.x < margin * 2 || map.Size.z < margin * 2)
                {
                    continue;
                }
                for (int attempt = 0; attempt < 150; attempt++)
                {
                    IntVec3 center = attempt == 0
                        ? map.Center
                        : new IntVec3(Rand.Range(margin, map.Size.x - margin), 0,
                            Rand.Range(margin, map.Size.z - margin));
                    CellRect rect = CellRect.CenteredOn(center, size, size);
                    if (!IsValidRect(rect, map))
                    {
                        continue;
                    }
                    GenerateAndSpawn(rect, map, parms, LayoutDef);
                    return;
                }
            }
            // A map with no clear 30x30 anywhere: let vanilla scatter try,
            // and accept its error as the least-bad outcome.
            base.GenerateRuins(map, parms, mapFillPercentRange);
        }
    }

    /// <summary>
    /// Places a thing in the DEEPEST room of the map's layout structure -
    /// the room whose center sits farthest from the structure's outer
    /// boundary. Written for the wardens' staff, which the confession says
    /// was kept "in the deepest room they had", and which the generic
    /// interior-cell placement kept dropping in open scrub whenever room
    /// detection failed mid-generation (seen live: the staff in the middle
    /// of the desert). The layout sketch knows its own rooms; asking it
    /// beats inferring.
    /// </summary>
    public class GenStep_TSC_PlaceInDeepestRoom : GenStep
    {
        public ThingDef thingDef;

        public override int SeedPart => 493817265;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (thingDef == null)
            {
                return;
            }
            IntVec3 cell = FindDeepestRoomCell(map);
            if (!cell.IsValid)
            {
                // No sketch on this map: fall through to the same interior
                // placement the other keepings use.
                cell = GenStep_TSC_PlaceInStructure.InteriorCells(map) is List<IntVec3> interior
                    && interior.Count > 0 ? interior.RandomElement() : CellFinder.RandomNotEdgeCell(20, map);
            }
            Thing thing = ThingMaker.MakeThing(thingDef, GenStuff.DefaultStuffFor(thingDef));
            GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
        }

        private static IntVec3 FindDeepestRoomCell(Map map)
        {
            LayoutStructureSketch sketch = null;
            for (int i = 0; i < map.layoutStructureSketches.Count; i++)
            {
                if (map.layoutStructureSketches[i]?.structureLayout?.Rooms != null)
                {
                    sketch = map.layoutStructureSketches[i];
                    break;
                }
            }
            if (sketch == null)
            {
                return IntVec3.Invalid;
            }
            // The structure's own footprint: the envelope of every room rect.
            CellRect envelope = CellRect.Empty;
            bool first = true;
            foreach (LayoutRoom room in sketch.structureLayout.Rooms)
            {
                for (int i = 0; room.rects != null && i < room.rects.Count; i++)
                {
                    envelope = first ? room.rects[i] : envelope.Encapsulate(room.rects[i]);
                    first = false;
                }
            }
            if (first)
            {
                return IntVec3.Invalid;
            }
            // Real rooms only: corridors and connective space carry no room
            // defs, and "the deepest room" landing in a hallway (seen live)
            // is worse than a shallower actual room. Two passes - defed
            // rooms first, anything with walls as the fallback.
            LayoutRoom deepest = null;
            CellRect deepestRect = CellRect.Empty;
            for (int pass = 0; pass < 2 && deepest == null; pass++)
            {
                int best = -1;
                foreach (LayoutRoom room in sketch.structureLayout.Rooms)
                {
                    if (room.rects == null || room.rects.Count == 0)
                    {
                        continue;
                    }
                    if (pass == 0 && (room.defs == null || room.defs.Count == 0 || room.Area < 25))
                    {
                        continue;
                    }
                    CellRect largest = room.rects[0];
                    for (int i = 1; i < room.rects.Count; i++)
                    {
                        if (room.rects[i].Area > largest.Area)
                        {
                            largest = room.rects[i];
                        }
                    }
                    IntVec3 center = largest.CenterCell;
                    int depth = Mathf.Min(
                        Mathf.Min(center.x - envelope.minX, envelope.maxX - center.x),
                        Mathf.Min(center.z - envelope.minZ, envelope.maxZ - center.z));
                    // Depth first; a bigger room breaks ties, so a closet
                    // never beats the armory at equal remove from the walls.
                    if (depth > best || (depth == best && deepest != null && largest.Area > deepestRect.Area))
                    {
                        best = depth;
                        deepest = room;
                        deepestRect = largest;
                    }
                }
            }
            if (deepest == null)
            {
                return IntVec3.Invalid;
            }
            IntVec3 target = deepestRect.ContractedBy(1).CenterCell;
            if (!target.InBounds(map))
            {
                return IntVec3.Invalid;
            }
            if (!target.Standable(map))
            {
                foreach (IntVec3 near in GenRadial.RadialCellsAround(target, 4f, useCenter: false))
                {
                    if (near.InBounds(map) && near.Standable(map))
                    {
                        return near;
                    }
                }
            }
            return target;
        }
    }

    /// <summary>
    /// The priests' sanctum: a small sealed cell inside the chantry with
    /// the amulet at its middle, shut with the order's own seal - the
    /// TSC_SealedDoor check spot, which answers skill and rite, not picks.
    /// It is the answer to a fair question: if the robbers spent six days
    /// in the building, why is the amulet still here? Because the thing
    /// keeping it is not a lock, and their six days went into trying to
    /// tunnel UNDER it. Runs after the robbers spawn and keeps two cells
    /// clear of every pawn, so the walls can never brick anyone in.
    /// </summary>
    public class GenStep_TSC_ChantrySanctum : GenStep
    {
        public override int SeedPart => 918254733;

        public override void Generate(Map map, GenStepParams parms)
        {
            ThingDef amulet = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_KingsAmulet");
            ThingDef sealDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_SealedDoor");
            ThingDef granite = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksGranite");
            if (amulet == null)
            {
                return;
            }
            List<IntVec3> interior = GenStep_TSC_PlaceInStructure.InteriorCells(map);
            HashSet<IntVec3> inside = new HashSet<IntVec3>(interior);
            IntVec3 center = IntVec3.Invalid;
            IntVec3 gap = IntVec3.Invalid;
            interior.Shuffle();
            foreach (IntVec3 candidate in interior)
            {
                if (!FitsSanctum(map, candidate, inside, out gap))
                {
                    continue;
                }
                center = candidate;
                break;
            }
            if (!center.IsValid || sealDef == null)
            {
                // No room for a cell: the amulet lies loose, as it used to.
                // A cramped layout must never cost the quest its objective.
                GenSpawn.Spawn(ThingMaker.MakeThing(amulet), interior.Count > 0 ? interior[0] : map.Center, map);
                return;
            }
            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(new TargetInfo(center, map)))
            {
                if (!cell.InBounds(map) || cell.GetEdifice(map) != null)
                {
                    continue;
                }
                if (cell == gap)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(sealDef), cell, map);
                }
                else
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, granite), cell, map);
                }
            }
            GenSpawn.Spawn(ThingMaker.MakeThing(amulet), center, map);
        }

        /// <summary>A 3x3 that fits: all neighbors inside the structure, and nobody standing close enough to be walled in.</summary>
        private static bool FitsSanctum(Map map, IntVec3 center, HashSet<IntVec3> inside, out IntVec3 gap)
        {
            gap = IntVec3.Invalid;
            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(new TargetInfo(center, map)))
            {
                if (!cell.InBounds(map) || !inside.Contains(cell) || cell.GetEdifice(map) != null)
                {
                    return false;
                }
            }
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Position.InHorDistOf(center, 2.9f))
                {
                    return false;
                }
            }
            // The seal goes on the side with open floor beyond it, so the
            // door is walkable up to from the chantry proper.
            foreach (IntVec3 dir in GenAdj.CardinalDirections)
            {
                IntVec3 doorCell = center + dir;
                IntVec3 beyond = center + dir + dir;
                if (inside.Contains(beyond) && beyond.Standable(map))
                {
                    gap = doorCell;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// The wardens' hall keeps its own law. Two duties, both site-gated:
    ///
    /// THE TELL - the insects fill the hall but nothing nests on the vault
    /// approach, said once out loud so the player knows the clean corridor
    /// means something (and Search has a reason to look down it).
    ///
    /// THE TOLL - the shatterers warded the staff's room, and the ward
    /// charges whoever takes the staff out six months of life, unless the
    /// dead warden's confession taught them the pass (TSC_WardKeystone).
    /// Deliberately the same currency as the crown and the errand: by the
    /// end of this act the player should know exactly what magic costs in
    /// this world, because they have paid it in small change twice.
    /// </summary>
    public class MapComponent_TSC_HallWard : MapComponent
    {
        private const int Interval = 60;
        private const int TollMonths = 6;
        private bool checkedMap;
        private bool isHall;
        private bool toldTell;

        public MapComponent_TSC_HallWard(Map map) : base(map)
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
                if (map.Parent is Site site)
                {
                    for (int i = 0; i < site.parts.Count; i++)
                    {
                        if (site.parts[i].def?.defName == "TSC_RegaliaHall")
                        {
                            isHall = true;
                            break;
                        }
                    }
                }
            }
            if (!isHall || map.mapPawns.FreeColonistsSpawnedCount == 0)
            {
                return;
            }
            if (!toldTell)
            {
                toldTell = true;
                Messages.Message("The hall's tenants have webbed and chewed every room but one: the corridor to the deepest chamber is untouched. Nothing will nest there. It is worth wondering why.",
                    MessageTypeDefOf.NeutralEvent, historical: false);
            }
            DialogueStateManager state = DialogueStateManager.Current;
            if (state == null || state.IsSet("TSC_WardCharged") || state.IsSet("TSC_WardPassed"))
            {
                return;
            }
            Pawn holder = StaffHolder();
            if (holder == null)
            {
                return;
            }
            if (state.IsSet("TSC_WardKeystone"))
            {
                state.Set("TSC_WardPassed");
                Messages.Message($"The ward reads the dead warden's word in {holder.LabelShortCap}'s mouth and stands aside. The staff leaves its room for the first time in eight hundred years, paid for in full by somebody else.",
                    holder, MessageTypeDefOf.PositiveEvent, historical: false);
                return;
            }
            state.Set("TSC_WardCharged");
            holder.ageTracker.AgeBiologicalTicks += GenDate.TicksPerYear / 12L * TollMonths;
            Find.LetterStack.ReceiveLetter("The ward settles up",
                $"The wardens' script on the vault floor flares once as the staff crosses it, and {holder.LabelShortCap} is {TollMonths} months older between one step and the next.\n\nThe men who broke the crown learned the magic from the thing they broke. Somewhere in this hall, their last warden wrote down the incantation that would have waived the fee.",
                LetterDefOf.NegativeEvent, holder);
        }

        private Pawn StaffHolder()
        {
            ThingDef staff = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_KingsStaff");
            if (staff == null)
            {
                return null;
            }
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.carryTracker?.CarriedThing?.def == staff)
                {
                    return pawn;
                }
                if (pawn.inventory?.innerContainer != null
                    && pawn.inventory.innerContainer.Contains(staff))
                {
                    return pawn;
                }
            }
            return null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref toldTell, "wardToldTell");
        }
    }

    /// <summary>
    /// DSL calm_beasts(): every manhunting animal on the interactor's map
    /// goes back to being an animal. The bride's tower den, bought off with
    /// a carcass laid downwind instead of a fight.
    /// </summary>
    public class DialogueEffect_TSC_CalmBeasts : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Map map = context.interactor?.MapHeld;
            if (map == null)
            {
                return;
            }
            int calmed = 0;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.RaceProps?.Animal != true || !pawn.InMentalState)
                {
                    continue;
                }
                if (pawn.MentalStateDef == MentalStateDefOf.Manhunter
                    || pawn.MentalStateDef == MentalStateDefOf.ManhunterPermanent)
                {
                    pawn.mindState.mentalStateHandler.CurState?.RecoverFromState();
                    calmed++;
                }
            }
            if (calmed > 0)
            {
                Messages.Message("The den takes the offering. The beasts drift off the stones and back to being animals, and the tower is anybody's.",
                    MessageTypeDefOf.PositiveEvent, historical: false);
            }
        }
    }

    /// <summary>
    /// DSL ambush_caravan(): the riders make good on the threat, right now.
    /// A real vanilla caravan ambush - the period filter recasts the
    /// attackers medieval like any other road event.
    /// </summary>
    public class DialogueEffect_TSC_CaravanAmbush : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Caravan caravan = context.interactor?.GetCaravan();
            IncidentDef ambush = DefDatabase<IncidentDef>.GetNamedSilentFail("Ambush");
            if (caravan == null || ambush == null)
            {
                return;
            }
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(ambush.category, caravan);
            parms.forced = true;
            // Caravan targets price their threats off wealth-on-wheels,
            // which sent two riders against a seven-strong party (seen
            // live). The dialogue promised a fight worth dodging a toll
            // over: price it off the PARTY, the same difficulty-x-level
            // scale as every other mod threat, with vanilla's own roll
            // kept as the floor.
            int heads = 0;
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                if (pawn.IsFreeColonist)
                {
                    heads++;
                }
            }
            float partyPoints = 55f * Mathf.Max(3, heads)
                * TSC_Threat.PartyScale * TSC_Threat.DifficultyScale;
            parms.points = Mathf.Max(parms.points, Mathf.Min(partyPoints, 2200f));
            if (!ambush.Worker.TryExecute(parms))
            {
                // Nobody in riding distance after all: the threat was wind.
                Messages.Message("Hooves in the dark, going away. Whatever they wanted, they have thought better of collecting it tonight.",
                    MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }
    }

    /// <summary>
    /// Word gets out. The first time the party night-rests on the road with
    /// two or more pieces of the regalia, riders find the fire: a remnant of
    /// the Baron's people who know exactly what is being carried and want to
    /// sell the king's waking back to the company. Once per save; talked
    /// through, paid off, or answered with steel (a real ambush).
    /// </summary>
    public class WorldComponent_TSC_RegaliaRoad : WorldComponent
    {
        private const int CheckInterval = 2500; // hourly
        private const string MetFlag = "TSC_RegaliaRoadMet";

        private static readonly string[] PieceDefNames =
        {
            "TSC_KingsRing", "TSC_KingsAmulet", "TSC_KingsStaff",
        };

        public WorldComponent_TSC_RegaliaRoad(World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (Find.TickManager.TicksGame % CheckInterval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            DialogueStateManager state = DialogueStateManager.Current;
            if (state == null || state.IsSet(MetFlag) || state.IsSet("TSC_KingSpoke"))
            {
                return;
            }
            if (Find.WindowStack?.WindowOfType<Dialog_Conversation>() != null)
            {
                return;
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled || !caravan.NightResting)
                {
                    continue;
                }
                if (PiecesHeld(caravan) < 2)
                {
                    continue;
                }
                // On the ROAD, not on the doorstep: a camp pitched on the
                // tile of the keeping they just emptied is still part of the
                // job. The riders work the roads between - toll men do not
                // knock on doors.
                if (AtKeepingSite(caravan))
                {
                    continue;
                }
                Pawn talker = null;
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    if (pawn.IsFreeColonist && !pawn.Dead && !pawn.Downed)
                    {
                        talker = pawn;
                        break;
                    }
                }
                DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_RegaliaRoad");
                if (talker == null || def == null)
                {
                    return;
                }
                state.Set(MetFlag);
                Find.WindowStack.Add(new Dialog_Conversation(def, talker, talker));
                return;
            }
        }

        /// <summary>Camped on one of the three keepings (or any site at all): not yet "on the road".</summary>
        private static bool AtKeepingSite(Caravan caravan)
        {
            foreach (WorldObject worldObject in Find.WorldObjects.ObjectsAt(caravan.Tile))
            {
                if (worldObject is Site site && site.parts != null)
                {
                    for (int i = 0; i < site.parts.Count; i++)
                    {
                        string defName = site.parts[i].def?.defName;
                        if (defName == "TSC_RegaliaTower" || defName == "TSC_RegaliaChantry"
                            || defName == "TSC_RegaliaHall")
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static int PiecesHeld(Caravan caravan)
        {
            int held = 0;
            foreach (string defName in PieceDefNames)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    continue;
                }
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    if (pawn.inventory?.innerContainer != null
                        && pawn.inventory.innerContainer.Contains(def))
                    {
                        held++;
                        break;
                    }
                }
            }
            return held;
        }
    }
}
