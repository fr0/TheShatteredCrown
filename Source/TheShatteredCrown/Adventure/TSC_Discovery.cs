using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// Adventure Mode's exploration layer: as the party travels, the world
    /// offers itself. Each new tile a player caravan enters rolls a chance
    /// to sight a wilderness location one or two tiles off the route - a
    /// cave, a lair, a fallen tower, a shrine - announced by letter and
    /// placed on the world map to investigate or ignore. Woodcraft counts:
    /// the roll is biased by the party's best Survival or Perception.
    /// Ignored discoveries fade off the map after a couple of weeks.
    /// </summary>
    public class TSC_DiscoveryManager : WorldComponent
    {
        private const int CheckIntervalTicks = 60;
        private const float BaseChancePerTile = 0.05f;
        private const float ChancePerProficiencyPoint = 0.008f;
        private const float MaxChance = 0.25f;
        /// <summary>Un-entered discoveries standing at once; no new sightings above this.</summary>
        private const int MaxStanding = 3;
        private const int TimeoutDays = 14;

        private Dictionary<int, int> lastCaravanTile = new Dictionary<int, int>();

        public TSC_DiscoveryManager(World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0
                || !TSC_AdventureModeGate.Active)
            {
                return;
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled)
                {
                    continue;
                }
                int tile = caravan.Tile;
                if (lastCaravanTile.TryGetValue(caravan.ID, out int last) && last == tile)
                {
                    continue;
                }
                bool moved = lastCaravanTile.ContainsKey(caravan.ID);
                lastCaravanTile[caravan.ID] = tile;
                // The first observation of a caravan is not a traveled tile.
                if (!moved)
                {
                    continue;
                }
                if (StandingDiscoveries() >= MaxStanding || !Rand.Chance(DiscoveryChance(caravan)))
                {
                    continue;
                }
                TryDiscoverNear(caravan);
            }
        }

        private static float DiscoveryChance(Caravan caravan)
        {
            int best = 0;
            TSC_ProficiencyDef survival = DefDatabase<TSC_ProficiencyDef>.GetNamedSilentFail("TSC_Prof_Survival");
            TSC_ProficiencyDef perception = DefDatabase<TSC_ProficiencyDef>.GetNamedSilentFail("TSC_Prof_Perception");
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                if (!pawn.IsFreeColonist)
                {
                    continue;
                }
                if (survival != null)
                {
                    best = Mathf.Max(best, TSC_ProgressionManager.Current.EffectiveProficiency(pawn, survival));
                }
                if (perception != null)
                {
                    best = Mathf.Max(best, TSC_ProgressionManager.Current.EffectiveProficiency(pawn, perception));
                }
            }
            return Mathf.Min(BaseChancePerTile + ChancePerProficiencyPoint * best, MaxChance);
        }

        private static int StandingDiscoveries()
        {
            int count = 0;
            List<Site> sites = Find.WorldObjects.Sites;
            for (int i = 0; i < sites.Count; i++)
            {
                for (int p = 0; p < sites[i].parts.Count; p++)
                {
                    if (sites[i].parts[p].def?.GetModExtension<TSC_DiscoverySiteExtension>() != null)
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        private void TryDiscoverNear(Caravan caravan)
        {
            List<SitePartDef> pool = new List<SitePartDef>();
            List<float> weights = new List<float>();
            foreach (SitePartDef def in DefDatabase<SitePartDef>.AllDefsListForReading)
            {
                TSC_DiscoverySiteExtension ext = def.GetModExtension<TSC_DiscoverySiteExtension>();
                if (ext != null)
                {
                    pool.Add(def);
                    weights.Add(ext.weight);
                }
            }
            if (pool.Count == 0)
            {
                return;
            }
            if (!TileFinder.TryFindPassableTileWithTraversalDistance(caravan.Tile, 1, 2, out PlanetTile tile,
                t => !Find.WorldObjects.AnyWorldObjectAt(t)))
            {
                return;
            }
            SitePartDef part = pool.RandomElementByWeight(d => weights[pool.IndexOf(d)]);
            float? points = part.wantsThreatPoints ? (float?)TSC_ContractManager.PartyScaledPoints() : null;
            Site site = SiteMaker.MakeSite(part, tile, null, ifHostileThenMustRemainHostile: true, points);
            if (site == null)
            {
                return;
            }
            site.GetComponent<TimeoutComp>()?.StartTimeout(TimeoutDays * GenDate.TicksPerDay);
            Find.WorldObjects.Add(site);
            Find.LetterStack.ReceiveLetter(
                $"Discovered: {part.label}",
                $"Riding the wilds, the party sights something off the route: {part.label}.\n\n{part.description}\n\nIt is marked on the map. Untouched places do not stay found: the mark fades in about two weeks.",
                LetterDefOf.NeutralEvent, site);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref lastCaravanTile, "lastCaravanTile", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && lastCaravanTile == null)
            {
                lastCaravanTile = new Dictionary<int, int>();
            }
        }
    }

    /// <summary>
    /// Marks a SitePartDef as a discoverable wilderness location and sets its
    /// weight in the sighting roll. The extension IS the registration - add
    /// it to any site part and the discovery manager offers it.
    /// </summary>
    public class TSC_DiscoverySiteExtension : DefModExtension
    {
        public float weight = 1f;
    }

    /// <summary>
    /// Wild tenants for discovered caves and lairs: beasts spawned at
    /// generation, denned near the map's deep point. Lairs post PERMANENT
    /// man-hunters (the den defends itself); caves leave their fauna wild.
    /// </summary>
    public class GenStep_TSC_WildBeasts : GenStep
    {
        public List<PawnKindDef> kinds = new List<PawnKindDef>();
        public IntRange count = new IntRange(1, 2);
        public bool defendDen;

        public override int SeedPart => 442918573;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (kinds.Count == 0)
            {
                return;
            }
            IntVec3 den = map.Center;
            if (!den.Walkable(map))
            {
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, 60f, useCenter: false))
                {
                    if (candidate.InBounds(map) && candidate.Walkable(map))
                    {
                        den = candidate;
                        break;
                    }
                }
            }
            int n = count.RandomInRange;
            for (int i = 0; i < n; i++)
            {
                Pawn beast = PawnGenerator.GeneratePawn(kinds.RandomElement(), null);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(den, map, 10);
                GenSpawn.Spawn(beast, cell, map);
                if (defendDen)
                {
                    beast.mindState.mentalStateHandler.TryStartMentalState(
                        MentalStateDefOf.ManhunterPermanent, "the den defends itself", forced: true);
                }
            }
        }
    }

    /// <summary>
    /// Crownless guards for discovered sites: openly hostile from spawn
    /// (unlike the crypt's factionless parley crew), posted on the deep
    /// chamber under a defend lord.
    /// </summary>
    /// <summary>
    /// Threat points for a site, read the way VANILLA reads them.
    ///
    /// SitePartParams carries BOTH `threatPoints` and `points`, and they are
    /// not always both filled: `wantsThreatPoints` populates one, the site's
    /// own generation populates the other. Reading only `threatPoints` (as
    /// this mod used to) hands the layout worker a null on sites that filled
    /// `points` instead, and a layout given no points spawns no defenders -
    /// a garrison quietly becoming an empty building.
    /// </summary>
    public static class TSC_SiteThreat
    {
        public static float? PointsFor(Map map, GenStepParams parms)
        {
            float best = 0f;
            SitePartParams sitePart = parms.sitePart?.parms;
            if (sitePart != null)
            {
                best = Mathf.Max(sitePart.threatPoints, sitePart.points);
            }
            if (best <= 0f && map?.Parent is Site site)
            {
                best = site.ActualThreatPoints;
            }
            return best > 0f ? (float?)best : null;
        }
    }

    /// <summary>
    /// Places a thing INSIDE the generated structure rather than scattered
    /// near the map centre. Ruin layouts choose their own footprint, so a
    /// centre-anchored scatter regularly dropped the guild strongbox in open
    /// desert a screen away from the ruin it was supposed to be buried in.
    /// This walks the walls the layout actually built and puts the prize in
    /// the deepest enclosed cell it can find.
    /// </summary>
    public class GenStep_TSC_PlaceInStructure : GenStep
    {
        public ThingDef thingDef;
        public int stackCount = 1;

        public override int SeedPart => 616219043;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (thingDef == null)
            {
                return;
            }
            IntVec3 cell = FindInteriorCell(map);
            if (!cell.IsValid)
            {
                // No structure generated (or none with an interior): fall back
                // to somewhere standable rather than dropping nothing.
                cell = CellFinder.RandomNotEdgeCell(20, map);
            }
            Thing thing = ThingMaker.MakeThing(thingDef, GenStuff.DefaultStuffFor(thingDef));
            if (thingDef.category == ThingCategory.Building)
            {
                // Buildings must be SPAWNED on a clear footprint - GenPlace
                // treats them as haulables and can refuse or shove them.
                foreach (IntVec3 clear in GenAdj.OccupiedRect(cell, Rot4.North, thingDef.size))
                {
                    if (!clear.InBounds(map))
                    {
                        continue;
                    }
                    List<Thing> here = clear.GetThingList(map);
                    for (int i = here.Count - 1; i >= 0; i--)
                    {
                        if (here[i].def.category == ThingCategory.Building && here[i].def.destroyable)
                        {
                            here[i].Destroy();
                        }
                    }
                }
                GenSpawn.Spawn(thing, cell, map);
                return;
            }
            thing.stackCount = Mathf.Max(1, stackCount);
            GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
        }

        /// <summary>
        /// Rooms are derived from the REGION grid, and the region updater is
        /// commonly disabled while a map generates - so GetRoom returns null
        /// for cells inside walls that plainly exist, and every "is this
        /// indoors?" test silently answers no. That is why occupants landed
        /// inside the ruin on some maps and in the open sand on others: pure
        /// luck about whether something earlier had triggered a rebuild.
        /// Force one before any room query.
        /// </summary>
        internal static void EnsureRooms(Map map)
        {
            if (map?.regionAndRoomUpdater == null)
            {
                return;
            }
            if (!map.regionAndRoomUpdater.Enabled)
            {
                map.regionAndRoomUpdater.Enabled = true;
            }
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
        }

        /// <summary>
        /// Every cell inside the structure: enclosed rooms, standable, and
        /// reachable from the map edge. Used to post occupants IN the ruin
        /// rather than in the yard around it - a holdfast whose crew stands
        /// outside in the rain is a camp, not a holdfast.
        /// </summary>
        internal static List<IntVec3> InteriorCells(Map map)
        {
            EnsureRooms(map);
            List<IntVec3> cells = new List<IntVec3>();
            TraverseParms walk = TraverseParms.For(TraverseMode.PassDoors);
            foreach (IntVec3 cell in map.AllCells)
            {
                if (!cell.Standable(map))
                {
                    continue;
                }
                Room room = cell.GetRoom(map);
                if (room == null || room.PsychologicallyOutdoors || room.CellCount < 4)
                {
                    continue;
                }
                if (map.reachability.CanReachMapEdge(cell, walk))
                {
                    cells.Add(cell);
                }
            }
            return cells;
        }

        /// <summary>
        /// The best "inside" cell: enclosed (a real room, not the outdoors),
        /// standable, reachable from the map edge so the prize is never
        /// walled off, and as deep into the structure as possible.
        /// </summary>
        internal static IntVec3 FindInteriorCell(Map map)
        {
            EnsureRooms(map);
            IntVec3 best = IntVec3.Invalid;
            float bestScore = -1f;
            TraverseParms walk = TraverseParms.For(TraverseMode.PassDoors);
            foreach (IntVec3 cell in map.AllCells)
            {
                if (!cell.Standable(map) || cell.Fogged(map))
                {
                    continue;
                }
                Room room = cell.GetRoom(map);
                if (room == null || room.PsychologicallyOutdoors || room.CellCount < 4)
                {
                    continue;
                }
                if (!map.reachability.CanReachMapEdge(cell, walk))
                {
                    continue;
                }
                // Prefer roofed, enclosed, and far from the edge: the back
                // room of the ruin rather than its doorway.
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

    public class GenStep_TSC_BanditGuards : GenStep
    {
        public IntRange count = new IntRange(3, 4);
        public IntRange scaledClamp = new IntRange(2, 7);

        /// <summary>
        /// Who holds the place. Left empty this posts the crownless brigands
        /// and archers it always did; set it and the same "guards around a
        /// point" behaviour serves a nest, a den, or anything else that needs
        /// occupants scaled to the party.
        /// </summary>
        public List<PawnKindDef> kinds;

        /// <summary>"bandits" (default), "insects", or "wild" for no faction.</summary>
        public string faction = "bandits";

        /// <summary>
        /// "SW", "NE", "NW", or "SE": confine this crew to the end of the
        /// structure nearest that map corner. For sites where two hostile
        /// groups share one building (the warren). Unset, the crew spreads
        /// through the whole interior as it always did.
        ///
        /// This exists because two groups scattered through the SAME rooms
        /// start their war at map generation, and the player crosses the map
        /// to find one side already routed. Partitioned to opposite ends,
        /// with a defend radius that keeps each side home, the stand-off the
        /// contract describes actually holds until the party disturbs it.
        /// </summary>
        public string holdEnd;

        /// <summary>How far a partitioned crew will pursue from its post.</summary>
        private const float HoldDefendRadius = 12f;

        public override int SeedPart => 771604318;

        private Faction ResolveFaction()
        {
            switch (faction)
            {
                case "insects":
                    return Faction.OfInsects;
                case "wild":
                    return null;
                default:
                    return TSC_BanditFactionUtility.Get();
            }
        }

        private IntVec3 Corner(Map map, string end)
        {
            switch (end)
            {
                case "SW": return new IntVec3(0, 0, 0);
                case "SE": return new IntVec3(map.Size.x - 1, 0, 0);
                case "NW": return new IntVec3(0, 0, map.Size.z - 1);
                case "NE": return new IntVec3(map.Size.x - 1, 0, map.Size.z - 1);
                default: return IntVec3.Invalid;
            }
        }

        private static string Opposite(string end)
        {
            switch (end)
            {
                case "SW": return "NE";
                case "NE": return "SW";
                case "NW": return "SE";
                case "SE": return "NW";
                default: return null;
            }
        }

        public override void Generate(Map map, GenStepParams parms)
        {
            Faction holders = ResolveFaction();
            List<PawnKindDef> roster = kinds != null && kinds.Count > 0 ? kinds : null;
            PawnKindDef brigand = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand");
            PawnKindDef archer = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Archer");
            if (roster == null && (holders == null || brigand == null))
            {
                return;
            }
            if (roster != null && holders == null && faction != "wild")
            {
                return;
            }
            // Hold the RUIN, not the yard: the crew posts inside the
            // structure when the layout built one, spread through its rooms,
            // with a couple left outside as lookouts. Sites with no building
            // (open camps) keep the old centre-post behaviour.
            List<IntVec3> interior = GenStep_TSC_PlaceInStructure.InteriorCells(map);
            // A crew given an end keeps to it: its half of the interior,
            // post in the deepest part of that half.
            IntVec3 own = Corner(map, holdEnd);
            IntVec3 far = Corner(map, Opposite(holdEnd));
            if (own.IsValid && far.IsValid && interior.Count > 1)
            {
                List<IntVec3> half = new List<IntVec3>();
                foreach (IntVec3 cell in interior)
                {
                    if (cell.DistanceToSquared(own) < cell.DistanceToSquared(far))
                    {
                        half.Add(cell);
                    }
                }
                if (half.Count > 0)
                {
                    interior = half;
                }
            }
            IntVec3 post = interior.Count > 0 ? interior.RandomElement() : map.Center;
            if (own.IsValid && interior.Count > 0)
            {
                float best = float.MaxValue;
                foreach (IntVec3 cell in interior)
                {
                    float d = cell.DistanceToSquared(own);
                    if (d < best)
                    {
                        best = d;
                        post = cell;
                    }
                }
            }
            if (interior.Count == 0 && !post.Walkable(map))
            {
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, 60f, useCenter: false))
                {
                    if (candidate.InBounds(map) && candidate.Walkable(map))
                    {
                        post = candidate;
                        break;
                    }
                }
            }
            int n = TSC_Threat.Count(map, count, scaledClamp);
            // Ragtag crews stay ragtag - rusty swords, no armor - but once
            // the party is casting spells in enchanted mail, bodies alone
            // stop mattering. The crew's answer is its hexer: the one who
            // found a book. Roughly one per four heads once the party has
            // outgrown the grace levels, two in a big crew for a seasoned
            // party. Explicit rosters (keep garrison etc.) manage their own.
            int hexerCount = 0;
            int shamanCount = 0;
            PawnKindDef hexer = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_BanditHexer");
            PawnKindDef shaman = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_BanditShaman");
            if (roster == null && faction == "bandits")
            {
                float partyLevel = TSC_Threat.AverageLevelAboveGraceAt(map);
                if (partyLevel > 0f)
                {
                    if (hexer != null)
                    {
                        hexerCount = Mathf.Clamp(n / 4, 1, partyLevel >= 2f ? 2 : 1);
                    }
                    // The shaman keeps the crew standing: one per big crew.
                    if (shaman != null && n >= 6)
                    {
                        shamanCount = 1;
                    }
                }
            }
            List<Pawn> guards = new List<Pawn>();
            for (int i = 0; i < n; i++)
            {
                // Default roster keeps the old shape: every third one an
                // archer - with the last slots going to the casters (hexers
                // at the very end, the shaman just before them).
                PawnKindDef kind = roster != null
                    ? roster[i % roster.Count]
                    : (i >= n - hexerCount ? hexer
                        : i >= n - hexerCount - shamanCount ? shaman
                        : (archer != null && i % 3 == 2 ? archer : brigand));
                Pawn guard = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind, holders, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                // Most of the crew inside, spread across the rooms; the first
                // two stand watch outside so the ruin still looks held from
                // the approach.
                IntVec3 cell;
                if (interior.Count > 0 && i >= 2)
                {
                    cell = interior.RandomElement();
                }
                else if (interior.Count > 0)
                {
                    // Sentries: standable ground OUTSIDE the walls, within
                    // sight of them, so the ruin reads as held on approach.
                    cell = IntVec3.Invalid;
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        IntVec3 candidate = CellFinder.RandomClosewalkCellNear(post, map, 14);
                        if (candidate.IsValid && candidate.Standable(map)
                            && candidate.GetRoom(map)?.PsychologicallyOutdoors != false)
                        {
                            cell = candidate;
                            break;
                        }
                    }
                    if (!cell.IsValid)
                    {
                        cell = interior.RandomElement();
                    }
                }
                else
                {
                    cell = CellFinder.RandomClosewalkCellNear(post, map, 8);
                }
                GenSpawn.Spawn(guard, cell, map);
                guards.Add(guard);
            }
            if (guards.Count > 0 && holders != null)
            {
                // The defend radius is the other half of the partition: the
                // half-interior keeps them SPAWNING at their end, the radius
                // keeps them from marching to the other one on first contact.
                LordJob_DefendPoint job = own.IsValid
                    ? new LordJob_DefendPoint(post, null, HoldDefendRadius)
                    : new LordJob_DefendPoint(post);
                LordMaker.MakeNewLord(holders, job, map, guards);
            }
        }
    }

    /// <summary>
    /// The captive at a rescue site: a would-be adventurer the bandits took,
    /// spawned hurt and factionless in the deep chamber. The moment a free
    /// colonist reaches them they throw in with the party - the rescue-route
    /// counterpart of hiring at a guild hall, and the party member you do
    /// not pay for (you fought for them instead).
    /// </summary>
    public class GenStep_TSC_Captive : GenStep
    {
        /// <summary>Set for STORY captives (Bry): the named pawn is held here instead of a generated stranger.</summary>
        public NamedNpcDef npc;

        public override int SeedPart => 668142935;

        public override void Generate(Map map, GenStepParams parms)
        {
            Pawn captive = null;
            if (npc != null)
            {
                captive = DialogueStateManager.Current.GetOrGenerateNamedNpc(npc, null);
                if (captive == null || captive.Dead || captive.Spawned)
                {
                    return; // dead, or already out in the world: no doppelganger
                }
            }
            if (captive == null)
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager");
                if (kind == null)
                {
                    return;
                }
                captive = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind, null, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true));
            }
            // Captives are HELD: the den is the deepest enclosed room the
            // layout built (same scoring as the strongbox), never a patch of
            // open sand the site happens to be centred on.
            IntVec3 den = GenStep_TSC_PlaceInStructure.FindInteriorCell(map);
            if (!den.IsValid)
            {
                den = map.Center;
            }
            if (!den.Walkable(map))
            {
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, 60f, useCenter: false))
                {
                    if (candidate.InBounds(map) && candidate.Walkable(map))
                    {
                        den = candidate;
                        break;
                    }
                }
            }
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(den, map, 6);
            // A quest-critical prisoner must never end up somewhere the
            // party cannot walk to: if the chosen cell cannot reach the map
            // edge (sealed room, blocked by the layout), fall back to a
            // reachable one rather than hiding the objective.
            TraverseParms walk = TraverseParms.For(TraverseMode.PassDoors);
            if (!cell.IsValid || !map.reachability.CanReachMapEdge(cell, walk))
            {
                IntVec3 rescue = IntVec3.Invalid;
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, 60f, useCenter: true))
                {
                    if (candidate.InBounds(map) && candidate.Standable(map)
                        && map.reachability.CanReachMapEdge(candidate, walk))
                    {
                        rescue = candidate;
                        break;
                    }
                }
                Log.Warning($"[The Shattered Crown] Captive cell was unreachable; relocating to {rescue}.");
                if (rescue.IsValid)
                {
                    cell = rescue;
                }
            }
            GenSpawn.Spawn(captive, cell, map);
            HealthUtility.DamageUntilDowned(captive, allowBleedingWounds: false);
            map.GetComponent<MapComponent_TSC_CaptiveRescue>()?.Register(captive);
        }
    }

    public class MapComponent_TSC_CaptiveRescue : MapComponent
    {
        private Pawn captive;
        private bool joined;

        public MapComponent_TSC_CaptiveRescue(Map map) : base(map)
        {
        }

        public void Register(Pawn pawn)
        {
            captive = pawn;
        }

        /// <summary>
        /// Announced once, when the party first sets foot on the map: a
        /// letter whose look-target jumps the camera to the prisoner. A
        /// downed, silent pawn in one room of a generated ruin is a needle
        /// in a haystack - a whole map was searched without finding one -
        /// and "where is the objective" is not the puzzle this contract is
        /// selling.
        /// </summary>
        private bool announced;

        public override void MapComponentTick()
        {
            if (joined || captive == null || captive.Dead
                || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            if (!announced && map.mapPawns.FreeColonistsSpawnedCount > 0)
            {
                announced = true;
                Find.LetterStack.ReceiveLetter(
                    "The prisoner",
                    $"{captive.LabelShortCap} is being held here, alive and in no shape to walk out alone. "
                    + "Reach them and they will throw in with the company.",
                    LetterDefOf.NeutralEvent, captive);
            }
            // Scooped between polls: a pawn loading the caravan can pick the
            // captive up inside one 60-tick window, and an unspawned pawn
            // never passes the proximity check below - the rescue then rode
            // out as a NEUTRAL passenger and the join never fired.
            if (!captive.Spawned)
            {
                Pawn carrier = (captive.ParentHolder as Pawn_CarryTracker)?.pawn;
                if (carrier != null && carrier.Faction == Faction.OfPlayer)
                {
                    JoinParty();
                }
                return;
            }
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.Position.InHorDistOf(captive.Position, 3f))
                {
                    JoinParty();
                    return;
                }
            }
        }

        private void JoinParty()
        {
            joined = true;
            captive.SetFaction(Faction.OfPlayer);
            Find.LetterStack.ReceiveLetter(
                $"{captive.LabelShortCap} freed",
                $"{captive.LabelShortCap} was not going to walk out of this place alone, and knows it. "
                + "They throw in with the party: no rate, no charter, just owed.",
                LetterDefOf.PositiveEvent, captive);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref captive, "captive");
            Scribe_Values.Look(ref joined, "joined");
            Scribe_Values.Look(ref announced, "announced");
        }
    }

    /// <summary>
    /// A surface structure layout with an EXPLICIT footprint: vanilla
    /// GenStep_AncientRuins offers no size control, and a castle should be
    /// castle-sized. Threat points come from the site (scaled to the party
    /// at discovery time), so the garrison grows with the finders.
    /// </summary>
    public class GenStep_TSC_Layout : GenStep
    {
        public LayoutDef layoutDef;
        public int size = 40;

        public override int SeedPart => 559130467;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (layoutDef == null)
            {
                return;
            }
            CellRect rect = CellRect.CenteredOn(map.Center, size, size).ClipInsideMap(map).ContractedBy(1);
            StructureGenParams genParms = new StructureGenParams
            {
                size = new IntVec2(rect.Width, rect.Height),
            };
            LayoutWorker worker = layoutDef.Worker;
            LayoutStructureSketch sketch = worker.GenerateStructureSketch(genParms);
            worker.Spawn(sketch, map, rect.Min, threatPoints: TSC_SiteThreat.PointsFor(map, parms));
        }
    }

    /// <summary>
    /// Places the shrine ring at the map's center: cleared ground, the old
    /// stones, and the altar with its rite (a check spot). Prefab-driven so
    /// the shrine's shape lives in XML with the village buildings.
    /// </summary>
    public class GenStep_TSC_Shrine : GenStep
    {
        public PrefabDef prefab;

        public override int SeedPart => 337158204;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (prefab == null)
            {
                return;
            }
            IntVec3 center = map.Center;
            CellRect clearRect = CellRect.CenteredOn(center, prefab.size.x + 4, prefab.size.z + 4);
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
                        || (thing.def.category == ThingCategory.Building && thing.def.building != null && thing.def.building.isNaturalRock)
                        || thing.def.IsFilth)
                    {
                        thing.Destroy();
                    }
                }
            }
            PrefabUtility.SpawnPrefab(prefab, map, center, Rot4.North);
        }
    }
}
