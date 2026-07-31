using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// Gille's drain, as a place instead of a corridor.
    ///
    /// The old back way carved a walled, roofed passage across the surface
    /// from the map edge to the keep. On a mountain map that read as a
    /// tunnel; on flat ground it was a masonry tail visible from anywhere,
    /// which is the opposite of a secret.
    ///
    /// The shape now is deliberately ONE-WAY, and the whole design falls out
    /// of that. The undercellar has a single portal and it stands inside the
    /// keep, so the cave exit at the bottom of the stair comes up behind the
    /// curtain wall. The party does not enter through it: they enter through
    /// a grate outside the walls that drops them into the far end of the
    /// cellar and does not open again. Down is a decision, not a shortcut -
    /// which is why the grate says so before it takes anyone.
    ///
    /// Doing it this way means no custom portal pairing at all. Vanilla's
    /// entrance-and-exit relationship is left exactly as it is, and the only
    /// unusual thing in the whole feature is a dialogue effect that moves the
    /// party onto a map they did not walk to.
    /// </summary>
    public static class TSC_KeepCellar
    {
        public const string StairDefName = "TSC_KeepCellarStair";

        /// <summary>
        /// Walk up the pocket chain to the map that owns this cellar. Pocket
        /// maps have no world tile of their own, which is the test.
        /// </summary>
        public static Map SurfaceOf(Map map)
        {
            int guard = 8;
            while (map != null && !map.Tile.Valid && guard-- > 0)
            {
                map = (map.Parent as RimWorld.Planet.PocketMapParent)?.sourceMap;
            }
            return map;
        }

        /// <summary>The stair inside the keep: the cellar's one and only portal.</summary>
        public static MapPortal FindStair(Map surface)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(StairDefName);
            if (surface == null || def == null)
            {
                return null;
            }
            foreach (Thing thing in surface.listerThings.ThingsOfDef(def))
            {
                if (thing is MapPortal portal)
                {
                    return portal;
                }
            }
            return null;
        }

        /// <summary>
        /// The cellar, built on demand. Normally a pocket map is generated
        /// the first time somebody walks into the portal; the grate sends
        /// people in from the other end, so it has to ask for the map itself.
        /// MapPortal.GeneratePocketMap is private, hence the reflection - but
        /// going through vanilla's own generation is worth it, because it is
        /// what registers the map with the portal and pairs the cave exit.
        /// </summary>
        public static Map EnsureCellar(MapPortal stair)
        {
            if (stair == null)
            {
                return null;
            }
            if (stair.PocketMap != null)
            {
                return stair.PocketMap;
            }
            AccessTools.Method(typeof(MapPortal), "GeneratePocketMap")?.Invoke(stair, null);
            return stair.PocketMap;
        }

        /// <summary>The floor's way back up, if it has one.</summary>
        public static Thing FindWayUp(Map cellar)
        {
            foreach (Thing thing in cellar.listerThings.AllThings)
            {
                if (thing is PocketMapExit)
                {
                    return thing;
                }
            }
            return null;
        }

        /// <summary>
        /// An exit somebody can actually stand at: nothing built over it,
        /// and at least one standable cell beside it. Vanilla's
        /// PlaceCaveExit runs before the floor is carved, so on a bad rock
        /// roll the exit lands at the map corner inside solid stone.
        /// </summary>
        public static bool WayUpUsable(Map cellar, Thing exit)
        {
            if (exit == null || !exit.Spawned)
            {
                return false;
            }
            foreach (IntVec3 cell in exit.OccupiedRect())
            {
                if (!cell.InBounds(cellar))
                {
                    return false;
                }
                Building edifice = cell.GetEdifice(cellar);
                if (edifice != null && edifice != exit)
                {
                    return false;
                }
            }
            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(exit))
            {
                if (cell.InBounds(cellar) && cell.Standable(cellar))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Cut a fresh way up (or dig out a buried one). PocketMapExit pairs
        /// itself to its portal through PocketMapUtility.currentlyGeneratingPortal
        /// in SpawnSetup, so that hook is borrowed for the respawn - which is
        /// also why this works both during map generation (the hook is already
        /// set) and as a save repair (we set it ourselves).
        /// </summary>
        public static Thing CutWayUp(Map cellar, MapPortal entrance, Thing buried)
        {
            IntVec3 cell = ExitSpot(cellar);
            if (!cell.IsValid)
            {
                return null;
            }
            if (buried != null && buried.Destroyed)
            {
                buried = null;
            }
            // The portal's declared exit, not vanilla's: PlaceCaveExit
            // hardcodes CaveExit and never reads portal.exitDef, so the
            // def swap (rope line -> stone stair) happens here. DeSpawn,
            // not Destroy: exits are destroyable=false, and a despawned
            // unreferenced thing simply never reaches the save.
            ThingDef wantDef = entrance?.def?.portal?.exitDef ?? ThingDefOf.CaveExit;
            if (buried != null && buried.def != wantDef)
            {
                if (buried.Spawned)
                {
                    buried.DeSpawn();
                }
                buried = null;
            }
            MapPortal remembered = PocketMapUtility.currentlyGeneratingPortal;
            PocketMapUtility.currentlyGeneratingPortal = entrance ?? remembered;
            Thing exit;
            try
            {
                if (buried != null)
                {
                    buried.DeSpawn();
                    exit = GenSpawn.Spawn(buried, cell, cellar);
                }
                else
                {
                    exit = GenSpawn.Spawn(ThingMaker.MakeThing(wantDef), cell, cellar);
                }
            }
            finally
            {
                PocketMapUtility.currentlyGeneratingPortal = remembered;
            }
            Log.Message("[The Shattered Crown] Cellar way up was missing or buried; a cave exit was cut at " + cell + ".");
            return exit;
        }

        /// <summary>A 3x3 standable, unbuilt clearing, as central as the floor offers.</summary>
        private static IntVec3 ExitSpot(Map cellar)
        {
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MinValue;
            foreach (IntVec3 cell in cellar.AllCells)
            {
                if (cell.DistanceToEdge(cellar) < 4)
                {
                    continue;
                }
                bool clear = true;
                foreach (IntVec3 part in GenAdj.OccupiedRect(cell, Rot4.North, new IntVec2(3, 3)))
                {
                    if (!part.InBounds(cellar) || !part.Standable(cellar) || part.GetEdifice(cellar) != null)
                    {
                        clear = false;
                        break;
                    }
                }
                if (!clear)
                {
                    continue;
                }
                float score = -cell.DistanceTo(cellar.Center);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell;
                }
            }
            return best;
        }

        /// <summary>
        /// Where the grate drops people: as far from the way up as the floor
        /// allows. Arriving next to the exit would make the cellar a corridor
        /// with extra steps.
        /// </summary>
        public static IntVec3 FarSide(Map cellar)
        {
            if (cellar == null)
            {
                return IntVec3.Invalid;
            }
            IntVec3 exit = IntVec3.Invalid;
            foreach (Thing thing in cellar.listerThings.AllThings)
            {
                if (thing is PocketMapExit way)
                {
                    exit = way.Position;
                    break;
                }
            }
            IntVec3 best = IntVec3.Invalid;
            float bestScore = -1f;
            foreach (IntVec3 cell in cellar.AllCells)
            {
                if (!cell.Standable(cellar) || cell.GetEdifice(cellar) != null)
                {
                    continue;
                }
                float score = exit.IsValid ? cell.DistanceTo(exit) : cell.DistanceToEdge(cellar);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// The grate. The proximity comp opens the scene once, the first time
    /// somebody walks up to it - which is the right way to MEET a thing, and
    /// the wrong way to be the only way to use it. A company that looked
    /// down the hole, thought better of a one-way trip and walked back to
    /// the gate must be able to change its mind, so the grate also answers a
    /// right-click for as long as it stands.
    /// </summary>
    public class Building_TSC_DrainGrate : Building
    {
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }
            DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_KeepDrain");
            if (def == null || selPawn == null || !selPawn.IsColonistPlayerControlled)
            {
                yield break;
            }
            yield return new FloatMenuOption("Look into the drain", delegate
            {
                Find.WindowStack.Add(new Dialog_Conversation(def, selPawn, selPawn));
            });
        }
    }

    /// <summary>
    /// DSL effect descend(): the grate takes the company. Everything the
    /// player owns and can see on this map goes down - colonists, their
    /// animals, whatever they are carrying - because leaving the pack mule
    /// on the wrong side of a one-way trip is not a decision anybody meant
    /// to make.
    /// </summary>
    public class DialogueEffect_TSC_Descend : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Map surface = context.interactor?.MapHeld;
            MapPortal stair = TSC_KeepCellar.FindStair(surface);
            if (stair == null)
            {
                // Keeps generated before the stair existed carry no way up,
                // and a persistent site's map is never rebuilt - so cut one
                // now rather than tell the player their drain is blocked
                // forever. Same chooser the genstep uses, same rules.
                ThingDef stairDef = DefDatabase<ThingDef>.GetNamedSilentFail(TSC_KeepCellar.StairDefName);
                GenStep_TSC_KeepCellarStair.Place(surface, stairDef, 14);
                stair = TSC_KeepCellar.FindStair(surface);
            }
            Map cellar = TSC_KeepCellar.EnsureCellar(stair);
            if (cellar == null)
            {
                // The message is deliberately diegetic; the log carries the
                // actual reason, because "sometimes choked" is undebuggable
                // from the message alone.
                Log.Warning("[The Shattered Crown] Drain descent failed: "
                    + (stair == null ? "no stair could be placed in the keep."
                        : "the stair exists but the pocket map did not generate."));
                Messages.Message("The drain is choked solid. There is no way down here.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            // NEVER send anyone down a one-way trip into a map with no way
            // up. Vanilla's PlaceCaveExit can lose the exit on a bad rock
            // roll (it runs before the floor is carved), so a missing or
            // buried way up is REPAIRED here rather than reported.
            Thing stairUp = TSC_KeepCellar.FindWayUp(cellar);
            if (!TSC_KeepCellar.WayUpUsable(cellar, stairUp))
            {
                stairUp = TSC_KeepCellar.CutWayUp(cellar, stair, stairUp);
            }
            IntVec3 arrival = TSC_KeepCellar.FarSide(cellar);
            if (stairUp == null || !arrival.IsValid)
            {
                Log.Warning("[The Shattered Crown] Drain descent failed: cellar generated but "
                    + (stairUp == null ? "no way up could be placed." : "no standable arrival cell was found."));
                Messages.Message("The drain is choked solid a little way in. There is no way through here.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            // If the far side cannot walk to the stair (a sealed-off layout
            // roll), arrive beside the stair instead: a short cellar beats
            // a tomb.
            if (!cellar.reachability.CanReach(arrival, stairUp.Position, PathEndMode.Touch,
                TraverseParms.For(TraverseMode.PassDoors)))
            {
                IntVec3 near = CellFinder.StandableCellNear(stairUp.Position, cellar, 8f);
                if (near.IsValid)
                {
                    arrival = near;
                }
            }
            List<Pawn> going = new List<Pawn>();
            foreach (Pawn pawn in surface.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Faction == Faction.OfPlayer && !pawn.Dead)
                {
                    going.Add(pawn);
                }
            }
            Pawn first = null;
            foreach (Pawn pawn in going)
            {
                IntVec3 cell = CellFinder.StandableCellNear(arrival, cellar, 8f);
                pawn.DeSpawn();
                GenSpawn.Spawn(pawn, cell.IsValid ? cell : arrival, cellar);
                // Pawns crossing maps: rebuild the render tree or they draw
                // as a null-key exception (same fix as the lure below).
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                first = first ?? pawn;
            }
            if (first != null)
            {
                CameraJumper.TryJump(first);
            }
            // A letter, not a message: it carries a look target. The way up is
            // deliberately at the far end of an unlit cellar, which is the
            // difference between crossing a floor and peeking into one - but
            // "somewhere in the dark" is not a direction, and a one-way trip
            // has no room for the player wandering. Clicking the letter shows
            // them where the stair is; walking there is still their problem.
            Find.LetterStack.ReceiveLetter(
                "The drain",
                "The company drops into standing water under the keep, and the grate above does not open from this side.\n\n"
                + "The old kingdom's cellars run on past the lamplight. Somewhere at the far end of them a stair goes up, "
                + "and it comes out inside the curtain wall.",
                LetterDefOf.NeutralEvent,
                new LookTargets(stairUp));
        }
    }

    /// <summary>
    /// Places the stair inside the keep: the cellar's only portal, and the
    /// party's way back up into the walls.
    /// </summary>
    public class GenStep_TSC_KeepCellarStair : GenStep
    {
        public ThingDef stairDef;
        /// <summary>How much room the company wants when it comes up the stair.</summary>
        public int SafeRadius = 14;

        public override int SeedPart => 442087611;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (stairDef == null || !DialogueStateManager.Current.IsSet("TSC_BrandBackWay"))
            {
                return;
            }
            Place(map, stairDef, SafeRadius);
        }

        /// <summary>
        /// Choose a spot and build the stair. Shared with the grate, which
        /// calls this to REPAIR a keep that was generated before the stair
        /// existed - or by a build where placement failed. A map already in a
        /// save can never be regenerated (the keep is a persistent site), so
        /// without this the feature is permanently dead on that campaign.
        /// </summary>
        public static void Place(Map map, ThingDef stairDef, int safeRadius)
        {
            if (map == null || stairDef == null)
            {
                return;
            }
            GenStep_TSC_PlaceInStructure.EnsureRooms(map);
            // This step runs after the guards are posted (order 492 against
            // their 483), so it can see exactly where they are standing.
            List<IntVec3> hostiles = new List<IntVec3>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!pawn.Dead && !pawn.Downed && pawn.HostileTo(Faction.OfPlayer))
                {
                    hostiles.Add(pawn.Position);
                }
            }
            // First choice, and a GUARANTEE when it lands: a proper back
            // room - enclosed, doored, small enough to be a store rather
            // than a hall, and containing not one member of the garrison.
            IntVec3 backRoom = BackRoomSpot(map, stairDef, hostiles);
            if (backRoom.IsValid)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(stairDef), backRoom, map);
                return;
            }
            List<IntVec3> indoors = new List<IntVec3>();
            foreach (IntVec3 cell in map.AllCells)
            {
                if (!cell.Standable(map) || cell.GetEdifice(map) != null)
                {
                    continue;
                }
                Room room = cell.GetRoom(map);
                if (room != null && !room.PsychologicallyOutdoors && room.CellCount >= 12)
                {
                    indoors.Add(cell);
                }
            }
            if (indoors.Count == 0)
            {
                // A ruined castle does not reliably produce a roofed, enclosed
                // room: damaged walls and pruned area leave plenty of the
                // layout open to the sky, and every open room counts as
                // outdoors. Requiring one meant the stair silently did not
                // exist and the grate reported itself choked - a whole
                // feature lost to a strict query.
                //
                // Anywhere well inside the walls will do. The stair is the
                // way in; it does not have to be a nice room.
                indoors = WithinWalls(map);
            }
            if (indoors.Count == 0)
            {
                // Truly nothing inside the masonry (tiny keep, or the walls
                // themselves are gone). The stair still has to exist - a
                // missing stair is a permanently choked drain on this save -
                // so take any standable ground away from the map edge.
                foreach (IntVec3 cell in map.AllCells)
                {
                    if (cell.DistanceToEdge(map) >= 12 && cell.Standable(map)
                        && cell.GetEdifice(map) == null)
                    {
                        indoors.Add(cell);
                    }
                }
            }
            if (indoors.Count == 0)
            {
                Log.Warning("[The Shattered Crown] Keep generated with nowhere to put the cellar stair.");
                return;
            }
            // Somewhere the garrison is NOT - the first version did not look,
            // which put the stair in a barracks and surfaced the company in
            // the middle of eleven Brand. The whole promise of a back way is
            // arriving behind the defence, not inside it.
            if (hostiles.Count > 0)
            {
                List<IntVec3> quiet = new List<IntVec3>();
                foreach (IntVec3 cell in indoors)
                {
                    if (NearestHostile(cell, hostiles) >= safeRadius)
                    {
                        quiet.Add(cell);
                    }
                }
                // If the keep is packed wall to wall, take the emptiest corner
                // of it rather than giving up on the criterion entirely.
                if (quiet.Count == 0)
                {
                    indoors.Sort((a, b) => NearestHostile(b, hostiles).CompareTo(NearestHostile(a, hostiles)));
                    quiet = indoors.GetRange(0, Mathf.Min(12, indoors.Count));
                }
                indoors = quiet;
            }
            // Middling depth among what is left, not the deepest point: the
            // deepest is where the Baron and the hoard are staged, and coming
            // up in his lap would make the drain a cheat rather than a way
            // past the gate.
            indoors.Sort((a, b) => a.DistanceToEdge(map).CompareTo(b.DistanceToEdge(map)));
            Thing stair = GenSpawn.Spawn(ThingMaker.MakeThing(stairDef), indoors[indoors.Count * 2 / 3], map);
            // No usable back room stood anywhere in the keep, so build one:
            // the guarantee is walls and a door, not a hopeful distance.
            EncloseStair(map, stair, hostiles);
        }

        /// <summary>
        /// A store-not-a-hall to surface in: enclosed, reachable through a
        /// door, small, and containing no member of the garrison. Among the
        /// candidates, the quietest (farthest from any posted guard) wins,
        /// with a nudge toward smaller rooms. Returns the stair spot inside
        /// it, or Invalid when the keep rolled no such room. Internal: the
        /// stair-heal component reuses it to relocate old-save stairs.
        /// </summary>
        internal static IntVec3 BackRoomSpot(Map map, ThingDef stairDef, List<IntVec3> hostiles)
        {
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MinValue;
            foreach (Room room in map.regionGrid.AllRooms)
            {
                if (room.PsychologicallyOutdoors || room.IsHuge || room.TouchesMapEdge
                    || room.CellCount < 16 || room.CellCount > 140)
                {
                    continue;
                }
                bool garrisoned = false;
                foreach (IntVec3 cell in room.Cells)
                {
                    List<Thing> things = cell.GetThingList(map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (things[i] is Pawn occupant && !occupant.Dead
                            && occupant.HostileTo(Faction.OfPlayer))
                        {
                            garrisoned = true;
                            break;
                        }
                    }
                    if (garrisoned)
                    {
                        break;
                    }
                }
                if (garrisoned || !HasDoor(room, map))
                {
                    continue;
                }
                IntVec3 spot = SpotInRoom(room, map, stairDef);
                if (!spot.IsValid)
                {
                    continue;
                }
                float score = NearestHostile(spot, hostiles) - room.CellCount * 0.03f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = spot;
                }
            }
            return best;
        }

        /// <summary>A room the company can leave through a door, not by mining.</summary>
        private static bool HasDoor(Room room, Map map)
        {
            foreach (IntVec3 cell in room.BorderCells)
            {
                if (cell.InBounds(map) && cell.GetDoor(map) != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A clear footprint for the stair, wholly inside the room.</summary>
        private static IntVec3 SpotInRoom(Room room, Map map, ThingDef stairDef)
        {
            foreach (IntVec3 cell in room.Cells)
            {
                bool fits = true;
                foreach (IntVec3 part in GenAdj.OccupiedRect(cell, Rot4.North, stairDef.size))
                {
                    if (!part.InBounds(map) || !part.Standable(map)
                        || part.GetEdifice(map) != null || part.GetRoom(map) != room)
                    {
                        fits = false;
                        break;
                    }
                }
                if (fits)
                {
                    return cell;
                }
            }
            return IntVec3.Invalid;
        }

        /// <summary>
        /// The built guarantee: when no suitable room exists, wall the stair
        /// off into one - granite walls, a wooden door, a roof. The Brand
        /// walled off its own cellar entrance years ago; the map just
        /// finally shows it.
        /// </summary>
        private static void EncloseStair(Map map, Thing stair, List<IntVec3> hostiles)
        {
            if (stair == null || !stair.Spawned)
            {
                return;
            }
            CellRect roomRect = stair.OccupiedRect().ExpandedBy(2).ClipInsideMap(map);
            ThingDef granite = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksGranite") ?? ThingDefOf.WoodLog;
            // Door: a non-corner edge cell whose inside and outside are both
            // standable, as far from the garrison as the perimeter offers.
            IntVec3 doorCell = IntVec3.Invalid;
            float doorScore = float.MinValue;
            foreach (IntVec3 cell in roomRect.EdgeCells)
            {
                bool corner = (cell.x == roomRect.minX || cell.x == roomRect.maxX)
                    && (cell.z == roomRect.minZ || cell.z == roomRect.maxZ);
                if (corner || cell.GetEdifice(map) != null)
                {
                    continue;
                }
                IntVec3 inward = new IntVec3(
                    Mathf.Clamp(cell.x, roomRect.minX + 1, roomRect.maxX - 1), 0,
                    Mathf.Clamp(cell.z, roomRect.minZ + 1, roomRect.maxZ - 1));
                IntVec3 outward = cell + (cell - inward);
                if (!outward.InBounds(map) || !outward.Standable(map) || !inward.Standable(map))
                {
                    continue;
                }
                float score = NearestHostile(cell, hostiles);
                if (score > doorScore)
                {
                    doorScore = score;
                    doorCell = cell;
                }
            }
            foreach (IntVec3 cell in roomRect.EdgeCells)
            {
                if (cell == doorCell || cell.GetEdifice(map) != null)
                {
                    continue;
                }
                ClearForBuild(map, cell);
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, granite), cell, map);
            }
            if (doorCell.IsValid)
            {
                ClearForBuild(map, doorCell);
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.WoodLog), doorCell, map);
            }
            foreach (IntVec3 cell in roomRect)
            {
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            }
        }

        private static void ClearForBuild(Map map, IntVec3 cell)
        {
            foreach (Thing thing in new List<Thing>(cell.GetThingList(map)))
            {
                if (thing.def.category == ThingCategory.Plant || thing.def.category == ThingCategory.Item
                    || thing.def.category == ThingCategory.Filth)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        /// <summary>Distance from a cell to the closest posted guard.</summary>
        internal static float NearestHostile(IntVec3 cell, List<IntVec3> hostiles)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < hostiles.Count; i++)
            {
                nearest = Mathf.Min(nearest, cell.DistanceTo(hostiles[i]));
            }
            return nearest;
        }

        /// <summary>
        /// Standable ground inside the keep's own masonry, roofed or not.
        /// Prefers deep inside (the bounds include the curtain wall, and a
        /// stair in the courtyard is a stair the garrison is standing on),
        /// but relaxes rather than fails: a hard 14-cell contraction was
        /// EMPTY on small keeps, which killed the stair and reported the
        /// drain choked - the same strict-query bug the roofed-room
        /// requirement had, one layer down.
        /// </summary>
        private static List<IntVec3> WithinWalls(Map map)
        {
            List<IntVec3> cells = new List<IntVec3>();
            List<Thing> walls = map.listerThings.ThingsOfDef(ThingDefOf.Wall);
            if (walls.Count == 0)
            {
                return cells;
            }
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minZ = int.MaxValue;
            int maxZ = int.MinValue;
            foreach (Thing wall in walls)
            {
                IntVec3 pos = wall.Position;
                minX = UnityEngine.Mathf.Min(minX, pos.x);
                maxX = UnityEngine.Mathf.Max(maxX, pos.x);
                minZ = UnityEngine.Mathf.Min(minZ, pos.z);
                maxZ = UnityEngine.Mathf.Max(maxZ, pos.z);
            }
            CellRect bounds = CellRect.FromLimits(minX, minZ, maxX, maxZ);
            foreach (int contraction in new[] { 14, 10, 6, 3 })
            {
                CellRect keep = bounds.ContractedBy(contraction);
                foreach (IntVec3 cell in keep)
                {
                    if (cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null)
                    {
                        cells.Add(cell);
                    }
                }
                if (cells.Count > 0)
                {
                    return cells;
                }
            }
            return cells;
        }
    }

    /// <summary>
    /// Old-save heal: a keep map generated before the back-room guarantee
    /// carries its stair wherever the old distance-only rules put it - seen
    /// live standing in the Baron's own vault. A saved map never
    /// regenerates, so the placement is baked; this moves the stair to a
    /// proper back room on the first tick the keep map runs. Idempotent: a
    /// stair already in an ungarrisoned room is left alone.
    /// </summary>
    public class MapComponent_TSC_KeepStairHeal : MapComponent
    {
        private bool done;

        public MapComponent_TSC_KeepStairHeal(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (done || Find.TickManager.TicksGame % 250 != 0)
            {
                return;
            }
            done = true;
            if (!(map.Parent is RimWorld.Planet.Site site))
            {
                return;
            }
            bool keep = false;
            for (int i = 0; i < site.parts.Count; i++)
            {
                if (site.parts[i].def?.defName == "TSC_IronBrandKeep")
                {
                    keep = true;
                    break;
                }
            }
            if (!keep)
            {
                return;
            }
            MapPortal stair = TSC_KeepCellar.FindStair(map);
            if (stair == null)
            {
                return; // placed on demand by the grate, with the new rules
            }
            Room room = stair.Position.GetRoom(map);
            bool exposed = room == null || room.PsychologicallyOutdoors;
            if (!exposed)
            {
                foreach (IntVec3 cell in room.Cells)
                {
                    List<Thing> things = cell.GetThingList(map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (things[i] is Pawn occupant && !occupant.Dead
                            && occupant.HostileTo(Faction.OfPlayer))
                        {
                            exposed = true;
                            break;
                        }
                    }
                    if (exposed)
                    {
                        break;
                    }
                }
            }
            if (!exposed)
            {
                return;
            }
            List<IntVec3> hostiles = new List<IntVec3>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!pawn.Dead && !pawn.Downed && pawn.HostileTo(Faction.OfPlayer))
                {
                    hostiles.Add(pawn.Position);
                }
            }
            IntVec3 spot = GenStep_TSC_KeepCellarStair.BackRoomSpot(map, stair.def, hostiles);
            if (!spot.IsValid || spot.GetRoom(map) == room)
            {
                return;
            }
            stair.DeSpawn();
            GenSpawn.Spawn(stair, spot, map);
            Log.Message("[The Shattered Crown] Cellar stair stood in a garrisoned room (old-save placement); moved to a back room at " + spot + ".");
        }
    }

    /// <summary>
    /// The stack of crates at the foot of the stair, and the idea somebody
    /// has when they see it.
    ///
    /// The drain drops the company inside the walls, which is the whole point
    /// and also the whole problem: coming up in a keep that is fully manned
    /// is a coin-flip decided at map generation. This is the answer the
    /// player can reach for. Push the stack over, let the noise go up the
    /// shaft, and fight whoever comes down HERE - in a stone corridor, on
    /// ground of the company's choosing, a few at a time - and walk up into a
    /// keep that is short exactly that many defenders.
    ///
    /// Re-openable by right-click as well as by walking past: a plan declined
    /// once should still be there when the room upstairs turns out to be full.
    /// </summary>
    public class Building_TSC_CellarCrates : Building
    {
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }
            DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_CellarCrates");
            if (def == null || selPawn == null || !selPawn.IsColonistPlayerControlled)
            {
                yield break;
            }
            yield return new FloatMenuOption("Look at the crates", delegate
            {
                Find.WindowStack.Add(new Dialog_Conversation(def, selPawn, selPawn));
            });
            // The push is a separate order from the scene that plans it, so
            // the company takes positions FIRST and the noise starts on the
            // player's word, not when the dialogue closes.
            if (!DialogueStateManager.Current.IsSet("TSC_KeepCratesPlanned")
                || DialogueStateManager.Current.IsSet("TSC_KeepCratesTipped"))
            {
                yield break;
            }
            string label = DialogueStateManager.Current.IsSet("TSC_KeepCratesRigged")
                ? "Pull the line: bring the stack down"
                : "Push the stack over";
            if (!selPawn.CanReach(this, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + " (no path)", null);
                yield break;
            }
            FloatMenuOption push = new FloatMenuOption(label, delegate
            {
                JobDef tip = DefDatabase<JobDef>.GetNamedSilentFail("TSC_TipCrates");
                if (tip != null)
                {
                    selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(tip, this), JobTag.Misc);
                }
            });
            yield return FloatMenuUtility.DecoratePrioritizedTask(push, selPawn, this);
        }
    }

    /// <summary>
    /// The push itself, as an ordered job rather than a dialogue effect: the
    /// scene at the crates makes the plan, the company takes its positions,
    /// and THEN somebody walks over and gives the stack the shove. The
    /// Thievery-rigged version pulls a bigger share of the garrison down.
    /// </summary>
    public class JobDriver_TSC_TipCrates : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil heave = Toils_General.Wait(120);
            heave.WithProgressBarToilDelay(TargetIndex.A);
            yield return heave;
            Toil tip = ToilMaker.MakeToil("TSC_TipCrates");
            tip.initAction = delegate
            {
                // Two pawns ordered at once: the first shove wins.
                if (DialogueStateManager.Current.IsSet("TSC_KeepCratesTipped"))
                {
                    return;
                }
                DialogueStateManager.Current.Set("TSC_KeepCratesTipped");
                int percent = DialogueStateManager.Current.IsSet("TSC_KeepCratesRigged") ? 60 : 40;
                DialogueEffect_TSC_Lure.LureGarrison(pawn.MapHeld, percent);
            };
            tip.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return tip;
        }
    }

    /// <summary>
    /// DSL effect lure(percent): the noise goes up the stair and part of the
    /// garrison comes down to see about it.
    ///
    /// The lured guards are MOVED onto the cellar floor rather than asked to
    /// walk down, because the two maps have no pathing between them - a
    /// pocket map is only reachable through its portal. Moving them is them
    /// coming down. What matters to the player is true either way: the fight
    /// happens in a corridor instead of a barracks, and the keep above is
    /// short exactly that many swords when the company climbs the stair.
    /// </summary>
    public class DialogueEffect_TSC_Lure : DialogueEffect
    {
        public int percent = 40;

        public override void Apply(DialogueContext context)
        {
            LureGarrison(context.interactor?.MapHeld, percent);
        }

        public static void LureGarrison(Map cellar, int percent)
        {
            Map surface = TSC_KeepCellar.SurfaceOf(cellar);
            if (cellar == null || surface == null || surface == cellar)
            {
                return;
            }
            IntVec3 stair = StairCell(cellar);
            if (!stair.IsValid)
            {
                return;
            }
            List<Pawn> garrison = new List<Pawn>();
            foreach (Pawn pawn in surface.mapPawns.AllPawnsSpawned)
            {
                // The Baron never takes the bait: he is posted ON the hoard,
                // does not investigate cellar noises personally, and his
                // parley scene (baron_parley.agd) needs him standing on his
                // gold when the company reaches the hall.
                if (!pawn.Dead && !pawn.Downed && pawn.RaceProps.Humanlike
                    && pawn.HostileTo(Faction.OfPlayer)
                    && pawn.kindDef?.defName != "TSC_IronBrandBaron")
                {
                    garrison.Add(pawn);
                }
            }
            if (garrison.Count == 0)
            {
                Messages.Message("The noise rolls up the shaft, and nothing answers it. There is nobody left up there to come.",
                    MessageTypeDefOf.NeutralEvent, historical: false);
                return;
            }
            // Whoever is nearest the stairhead hears it first - which is also
            // the group the company would otherwise have surfaced into.
            IntVec3 stairhead = TSC_KeepCellar.FindStair(surface)?.Position ?? surface.Center;
            garrison.Sort((a, b) => a.Position.DistanceTo(stairhead).CompareTo(b.Position.DistanceTo(stairhead)));
            int coming = Mathf.Clamp(Mathf.RoundToInt(garrison.Count * percent / 100f), 1, garrison.Count);

            List<Pawn> lured = new List<Pawn>();
            for (int i = 0; i < coming; i++)
            {
                Pawn pawn = garrison[i];
                // Leaving the garrison is leaving its lord: DeSpawn alone
                // keeps the membership, and a pawn cannot join the assault
                // lord below while the defend-point lord above still owns it.
                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                IntVec3 cell = CellFinder.StandableCellNear(stair, cellar, 8f);
                pawn.DeSpawn();
                GenSpawn.Spawn(pawn, cell.IsValid ? cell : stair, cellar);
                // Persistent pawns crossing maps: rebuild the render tree or
                // they arrive as the "Node is null" error rather than a guard.
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                lured.Add(pawn);
            }
            Faction owner = lured.Count > 0 ? lured[0].Faction : null;
            if (owner != null)
            {
                LordMaker.MakeNewLord(owner, new LordJob_AssaultColony(owner,
                    canKidnap: false, canTimeoutOrFlee: true), cellar, lured);
            }
            int left = garrison.Count - lured.Count;
            Find.LetterStack.ReceiveLetter(
                "They took the bait",
                "The stack goes over and the noise climbs the shaft like it was built to. Boots on stone overhead, "
                + "an argument about whose job it is to look, and then " + lured.Count + " of the Brand come down into "
                + "the dark to find out what fell over.\n\nThey arrive strung out, in a corridor, with no idea the "
                + "company is already standing in it. And when this is finished there are " + left + " left above, "
                + "which is a different keep entirely from the one that was up there a minute ago.",
                LetterDefOf.ThreatBig,
                new LookTargets(lured[0]));
        }

        /// <summary>The foot of the stair: where anyone coming down arrives.</summary>
        private static IntVec3 StairCell(Map cellar)
        {
            foreach (Thing thing in cellar.listerThings.AllThings)
            {
                if (thing is PocketMapExit exit)
                {
                    return exit.Position;
                }
            }
            return IntVec3.Invalid;
        }
    }

    /// <summary>Puts the crates within sight of the way up, where the idea is any use.</summary>
    public class GenStep_TSC_CellarCrates : GenStep
    {
        public ThingDef crateDef;

        public override int SeedPart => 553108742;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (crateDef == null)
            {
                return;
            }
            IntVec3 stair = IntVec3.Invalid;
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing is PocketMapExit exit)
                {
                    stair = exit.Position;
                    break;
                }
            }
            if (!stair.IsValid)
            {
                return;
            }
            for (int attempt = 0; attempt < 120; attempt++)
            {
                IntVec3 candidate = CellFinder.RandomClosewalkCellNear(stair, map, 7);
                if (!candidate.IsValid || candidate.DistanceTo(stair) < 3f)
                {
                    continue;
                }
                bool clear = true;
                foreach (IntVec3 cell in GenAdj.OccupiedRect(candidate, Rot4.North, crateDef.size))
                {
                    if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetEdifice(map) != null)
                    {
                        clear = false;
                        break;
                    }
                }
                if (clear)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(crateDef), candidate, map);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The Brand's overflow store. A bandit company with a hoard upstairs
    /// keeps the bulky, awkward and stolen-from-the-wrong-people things
    /// somewhere out of sight, and this is where.
    ///
    /// Deliberately worth less than the vault: the hoard is the prize and the
    /// shard is in it. This is what makes the drain route pay for itself, so
    /// a company that took the one-way trip is not just arriving early and
    /// poor. Placed a good way from the stair, so it is found by crossing the
    /// floor rather than by standing at the exit.
    /// </summary>
    public class GenStep_TSC_KeepCellarCache : GenStep
    {
        /// <summary>
        /// The box the store is kept in. A sealed crate that stocks itself
        /// (CompProperties_LootSpawn) and answers a proficiency check to
        /// open, so the placer neither fills it nor unlocks it.
        /// </summary>
        public ThingDef chestDef;
        public int chests = 2;
        public IntRange silverStacks = new IntRange(1, 2);
        public IntRange silverPerStack = new IntRange(60, 140);

        public override int SeedPart => 771903254;

        public override void Generate(Map map, GenStepParams parms)
        {
            GenStep_TSC_PlaceInStructure.EnsureRooms(map);
            for (int i = 0; chestDef != null && i < chests; i++)
            {
                IntVec3 cell = Spot(map);
                if (cell.IsValid)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(chestDef, GenStuff.DefaultStuffFor(chestDef)), cell, map);
                }
            }
            ThingDef silver = DefDatabase<ThingDef>.GetNamedSilentFail("Silver");
            int stacks = silverStacks.RandomInRange;
            for (int i = 0; silver != null && i < stacks; i++)
            {
                IntVec3 cell = Spot(map);
                if (!cell.IsValid)
                {
                    continue;
                }
                Thing coin = ThingMaker.MakeThing(silver);
                coin.stackCount = silverPerStack.RandomInRange;
                GenPlace.TryPlaceThing(coin, cell, map, ThingPlaceMode.Near);
            }
        }

        /// <summary>A standable floor cell in a room big enough to have been used as a store.</summary>
        private static IntVec3 Spot(Map map)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                IntVec3 candidate = CellFinder.RandomNotEdgeCell(6, map);
                if (!candidate.Standable(map) || candidate.GetEdifice(map) != null)
                {
                    continue;
                }
                Room room = candidate.GetRoom(map);
                if (room != null && room.CellCount >= 12)
                {
                    return candidate;
                }
            }
            return IntVec3.Invalid;
        }
    }

    /// <summary>
    /// The grate on the keep's surface map: a stone throat in open ground
    /// outside the curtain, placed only when the party has LEARNED of it
    /// (TSC_BrandBackWay, from Madoc's campfire scene). Walking up to it
    /// opens the conversation that offers the descent.
    ///
    /// Madoc keeps his word here as he always did: if he was left at his
    /// fire rather than recruited, he is waiting at the grate, since his
    /// campfire site died with its quest and the campaign should not lose a
    /// companion to that.
    /// </summary>
    public class GenStep_TSC_KeepDrain : GenStep
    {
        public ThingDef grateDef;
        /// <summary>How far clear of the keep's outermost masonry the grate sits.</summary>
        public int standoff = 18;

        public override int SeedPart => 660154823;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (grateDef == null || !DialogueStateManager.Current.IsSet("TSC_BrandBackWay"))
            {
                return;
            }
            CellRect walls = MasonryBounds(map);
            IntVec3 mouth = FindMouth(map, walls);
            if (!mouth.IsValid)
            {
                Log.Warning("[The Shattered Crown] No open ground for the keep's drain grate; the keep generated without it.");
                return;
            }
            foreach (IntVec3 cell in GenAdj.OccupiedRect(mouth, Rot4.North, grateDef.size))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                foreach (Thing thing in new List<Thing>(cell.GetThingList(map)))
                {
                    if (thing.def.category == ThingCategory.Plant || thing.def.category == ThingCategory.Item
                        || thing.def.building != null)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }
            GenSpawn.Spawn(ThingMaker.MakeThing(grateDef), mouth, map);
            SeatMadoc(map, mouth);
        }

        /// <summary>Everything the keep and its wall occupy, so the grate lands outside all of it.</summary>
        private static CellRect MasonryBounds(Map map)
        {
            List<Thing> walls = map.listerThings.ThingsOfDef(ThingDefOf.Wall);
            if (walls.Count == 0)
            {
                return CellRect.Empty;
            }
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minZ = int.MaxValue;
            int maxZ = int.MinValue;
            foreach (Thing wall in walls)
            {
                IntVec3 pos = wall.Position;
                minX = UnityEngine.Mathf.Min(minX, pos.x);
                maxX = UnityEngine.Mathf.Max(maxX, pos.x);
                minZ = UnityEngine.Mathf.Min(minZ, pos.z);
                maxZ = UnityEngine.Mathf.Max(maxZ, pos.z);
            }
            return CellRect.FromLimits(minX, minZ, maxX, maxZ);
        }

        /// <summary>
        /// East of the keep, clear of the wall by `standoff`: the side the
        /// party has always been told to look at, and far enough out that the
        /// grate is not in bowshot of the parapet.
        /// </summary>
        private IntVec3 FindMouth(Map map, CellRect walls)
        {
            int z = walls.Width > 0 ? walls.CenterCell.z : map.Center.z;
            int startX = walls.Width > 0 ? walls.maxX + standoff : map.Center.x + standoff;
            for (int drift = 0; drift <= 24; drift += 3)
            {
                foreach (int sign in new[] { 1, -1 })
                {
                    IntVec3 candidate = new IntVec3(startX, 0, z + drift * sign);
                    if (Suitable(map, candidate))
                    {
                        return candidate;
                    }
                    if (drift == 0)
                    {
                        break;
                    }
                }
            }
            for (int i = 0; i < 200; i++)
            {
                IntVec3 candidate = CellFinder.RandomNotEdgeCell(12, map);
                if (Suitable(map, candidate) && (walls.Width <= 0 || !walls.ExpandedBy(6).Contains(candidate)))
                {
                    return candidate;
                }
            }
            return IntVec3.Invalid;
        }

        private bool Suitable(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map))
            {
                return false;
            }
            foreach (IntVec3 part in GenAdj.OccupiedRect(cell, Rot4.North, grateDef.size))
            {
                if (!part.InBounds(map) || !part.Standable(map) || part.GetEdifice(map) != null
                    || part.GetTerrain(map).IsWater || part.Roofed(map))
                {
                    return false;
                }
            }
            return true;
        }

        private static void SeatMadoc(Map map, IntVec3 mouth)
        {
            NamedNpcDef madocDef = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Madoc");
            if (madocDef == null)
            {
                return;
            }
            Pawn madoc = DialogueStateManager.Current.GetOrGenerateNamedNpc(madocDef, GenStep_TSC_Village.VillagerFaction());
            if (madoc == null || madoc.Dead || madoc.Spawned || madoc.Faction == Faction.OfPlayer)
            {
                return;
            }
            IntVec3 seat = CellFinder.StandableCellNear(mouth, map, 5f);
            GenSpawn.Spawn(madoc, seat.IsValid ? seat : mouth, map);
            if (madoc.Faction != null && madoc.GetLord() == null)
            {
                LordMaker.MakeNewLord(madoc.Faction, new LordJob_DefendPoint(mouth), map,
                    new List<Pawn> { madoc });
            }
        }
    }
}
