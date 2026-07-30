using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
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
                Messages.Message("The drain is choked solid. There is no way down here.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            // NEVER send anyone down a one-way trip into a map with no way
            // up. If the cellar generated without its stair, the grate is
            // simply choked and the company walks to the gate instead.
            Thing stairUp = null;
            foreach (Thing thing in cellar.listerThings.AllThings)
            {
                if (thing is PocketMapExit)
                {
                    stairUp = thing;
                    break;
                }
            }
            IntVec3 arrival = TSC_KeepCellar.FarSide(cellar);
            if (stairUp == null || !arrival.IsValid)
            {
                Messages.Message("The drain is choked solid a little way in. There is no way through here.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
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
                Log.Warning("[The Shattered Crown] Keep generated with nowhere to put the cellar stair.");
                return;
            }
            // Somewhere the garrison is NOT. This step runs after the guards
            // are posted (order 492 against their 483), so it can see exactly
            // where they are standing - and the first version did not look,
            // which put the stair in a barracks and surfaced the company in
            // the middle of eleven Brand. The whole promise of a back way is
            // arriving behind the defence, not inside it.
            List<IntVec3> hostiles = new List<IntVec3>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!pawn.Dead && !pawn.Downed && pawn.HostileTo(Faction.OfPlayer))
                {
                    hostiles.Add(pawn.Position);
                }
            }
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
            GenSpawn.Spawn(ThingMaker.MakeThing(stairDef), indoors[indoors.Count * 2 / 3], map);
        }

        /// <summary>Distance from a cell to the closest posted guard.</summary>
        private static float NearestHostile(IntVec3 cell, List<IntVec3> hostiles)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < hostiles.Count; i++)
            {
                nearest = Mathf.Min(nearest, cell.DistanceTo(hostiles[i]));
            }
            return nearest;
        }

        /// <summary>Standable ground inside the keep's own masonry, roofed or not.</summary>
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
            // Contracted hard: the bounds include the curtain wall, and a
            // stair in the courtyard is a stair the garrison is standing on.
            CellRect keep = CellRect.FromLimits(minX, minZ, maxX, maxZ).ContractedBy(14);
            foreach (IntVec3 cell in keep)
            {
                if (cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null)
                {
                    cells.Add(cell);
                }
            }
            return cells;
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
            Map cellar = context.interactor?.MapHeld;
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
                if (!pawn.Dead && !pawn.Downed && pawn.RaceProps.Humanlike
                    && pawn.HostileTo(Faction.OfPlayer))
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
