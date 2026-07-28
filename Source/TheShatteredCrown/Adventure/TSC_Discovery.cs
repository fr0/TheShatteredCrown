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
        /// The best "inside" cell: enclosed (a real room, not the outdoors),
        /// standable, reachable from the map edge so the prize is never
        /// walled off, and as deep into the structure as possible.
        /// </summary>
        private static IntVec3 FindInteriorCell(Map map)
        {
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
            IntVec3 post = map.Center;
            if (!post.Walkable(map))
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
            List<Pawn> guards = new List<Pawn>();
            for (int i = 0; i < n; i++)
            {
                // Default roster keeps the old shape: every third one an archer.
                PawnKindDef kind = roster != null
                    ? roster[i % roster.Count]
                    : (archer != null && i % 3 == 2 ? archer : brigand);
                Pawn guard = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind, holders, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(post, map, 8);
                GenSpawn.Spawn(guard, cell, map);
                guards.Add(guard);
            }
            if (guards.Count > 0 && holders != null)
            {
                LordMaker.MakeNewLord(holders, new LordJob_DefendPoint(post), map, guards);
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
        public override int SeedPart => 668142935;

        public override void Generate(Map map, GenStepParams parms)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager");
            if (kind == null)
            {
                return;
            }
            Pawn captive = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, null, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true));
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
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(den, map, 6);
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

        public override void MapComponentTick()
        {
            if (joined || captive == null || captive.Dead || !captive.Spawned
                || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.Position.InHorDistOf(captive.Position, 3f))
                {
                    joined = true;
                    captive.SetFaction(Faction.OfPlayer);
                    Find.LetterStack.ReceiveLetter(
                        $"{captive.LabelShortCap} freed",
                        $"{captive.LabelShortCap} was not going to walk out of this place alone, and knows it. "
                        + "They throw in with the party: no rate, no charter, just owed.",
                        LetterDefOf.PositiveEvent, captive);
                    return;
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref captive, "captive");
            Scribe_Values.Look(ref joined, "joined");
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
