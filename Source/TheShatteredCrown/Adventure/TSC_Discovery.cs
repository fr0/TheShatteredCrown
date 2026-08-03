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
        // Halved (0.05/0.008/0.25 originally): discoveries were arriving
        // faster than a party could clear them, which turned sightings from
        // an event into a backlog.
        private const float BaseChancePerTile = 0.025f;
        private const float ChancePerProficiencyPoint = 0.004f;
        private const float MaxChance = 0.125f;
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
            TryDiscoverNear(caravan.Tile);
        }

        /// <summary>
        /// Reveal a discovery near any world tile. Public because sightings
        /// are no longer only made from the saddle: a dead adventurer's
        /// journal names the place they were headed next, and that call
        /// comes from a check spot on a site map.
        /// </summary>
        public bool TryDiscoverNear(PlanetTile fromTile)
        {
            return TryDiscoverNear(fromTile, 1, 2, null, null);
        }

        /// <summary>
        /// The parameterised form: how far off the marked country lies, and
        /// what the letter says. The map case reads a survey rather than
        /// sighting something from the saddle, so it wants both a longer
        /// reach and its own words; the letter format takes {0} = the
        /// place's label and {1} = its description.
        /// </summary>
        public bool TryDiscoverNear(PlanetTile fromTile, int minDist, int maxDist,
            string letterLabel, string letterTextFormat)
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
                return false;
            }
            if (!TileFinder.TryFindPassableTileWithTraversalDistance(fromTile, minDist, maxDist, out PlanetTile tile,
                t => !Find.WorldObjects.AnyWorldObjectAt(t)))
            {
                return false;
            }
            SitePartDef part = pool.RandomElementByWeight(d => weights[pool.IndexOf(d)]);
            float? points = part.wantsThreatPoints ? (float?)TSC_ContractManager.PartyScaledPoints() : null;
            Site site = SiteMaker.MakeSite(part, tile, null, ifHostileThenMustRemainHostile: true, points);
            if (site == null)
            {
                return false;
            }
            site.GetComponent<TimeoutComp>()?.StartTimeout(TimeoutDays * GenDate.TicksPerDay);
            Find.WorldObjects.Add(site);
            Find.LetterStack.ReceiveLetter(
                letterLabel ?? $"Discovered: {part.label}",
                letterTextFormat != null
                    ? string.Format(letterTextFormat, part.label, part.description)
                    : $"Riding the wilds, the party sights something off the route: {part.label}.\n\n{part.description}\n\nIt is marked on the map. Untouched places do not stay untouched for long: the mark fades in about two weeks.",
                LetterDefOf.NeutralEvent, site);
            return true;
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
        /// <summary>One kind rolled for the whole den: a menu, not a zoo. Duplicates weight the roll.</summary>
        public bool pickOneKind;

        public override int SeedPart => 442918573;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (kinds.Count == 0)
            {
                return;
            }
            List<PawnKindDef> denKinds = pickOneKind
                ? new List<PawnKindDef> { kinds.RandomElement() }
                : kinds;
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
                Pawn beast = PawnGenerator.GeneratePawn(denKinds.RandomElement(), null);
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

    /// <summary>
    /// Somebody came here before the party, and it went badly for them.
    ///
    /// A dessicated corpse in road gear, their weapon fallen beside them,
    /// a pouch of what they were carrying: silver, sometimes medicine or a
    /// guild coin, rarely the class book they trained from. Discovered
    /// sites were tenants-plus-check-spots; this is the middle layer, loot
    /// with a reason to exist - and an implicit warning about the tenants.
    ///
    /// Chance-gated per site so a fallen adventurer stays a find, not
    /// furniture. The corpse is aged hard so nobody reads it as a fresh
    /// kill the party gets blamed for.
    /// </summary>
    public class GenStep_TSC_FallenAdventurer : GenStep
    {
        public float chance = 0.4f;
        public IntRange silver = new IntRange(25, 75);
        public float medicineChance = 0.5f;
        public IntRange medicine = new IntRange(2, 5);
        public float coinChance = 0.15f;
        /// <summary>The big find: the book they trained from. Kept rare on purpose.</summary>
        public float bookChance = 0.05f;

        /// <summary>The other body: what got them, lying nearby. Both halves of the story.</summary>
        public List<PawnKindDef> beastKinds;
        public float beastChance = 0.4f;

        /// <summary>Their journal, a check spot: read it and it names the place they were headed next.</summary>
        public float journalChance;

        /// <summary>A sealed guild dispatch they never delivered. Any factor pays to receive it.</summary>
        public float courierChance;

        public override int SeedPart => 918273645;

        /// <summary>Spawn a pawn dead and long dead: gear drops, corpse aged to bone.</summary>
        public static Corpse SpawnAgedCorpse(Map map, IntVec3 cell, PawnKindDef kind, Faction faction)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(pawn, cell, map);
            pawn.Kill(null); // equipment drops beside the body
            Corpse corpse = pawn.Corpse;
            corpse?.TryGetComp<CompRottable>()?.RotImmediately(RotStage.Dessicated);
            return corpse;
        }

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!Rand.Chance(chance))
            {
                return;
            }
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand");
            Faction faction = TSC_BanditFactionUtility.Get();
            if (kind == null || faction == null)
            {
                return;
            }
            // Partway between the edge and the middle: they almost made it out.
            IntVec3 cell = IntVec3.Invalid;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                IntVec3 candidate = map.Center
                    + IntVec3Utility.RandomHorizontalOffset(Rand.Range(12f, 28f));
                if (candidate.InBounds(map) && candidate.Standable(map))
                {
                    cell = candidate;
                    break;
                }
            }
            if (!cell.IsValid)
            {
                return;
            }
            // Aged to bone: this happened long before the party arrived,
            // and no faction reads it as the party's work.
            SpawnAgedCorpse(map, cell, kind, faction);
            foreach (IntVec3 stain in GenAdj.CellsAdjacent8Way(new TargetInfo(cell, map)))
            {
                if (stain.InBounds(map) && Rand.Chance(0.3f))
                {
                    FilthMaker.TryMakeFilth(stain, map, ThingDefOf.Filth_Blood);
                }
            }
            SpawnBeside(map, cell, ThingDefOf.Silver, silver.RandomInRange);
            if (Rand.Chance(medicineChance))
            {
                SpawnBeside(map, cell, ThingDefOf.MedicineHerbal, medicine.RandomInRange);
            }
            if (Rand.Chance(coinChance))
            {
                SpawnBeside(map, cell, DefDatabase<ThingDef>.GetNamedSilentFail("TSC_GuildCoin"), 1);
            }
            if (Rand.Chance(bookChance))
            {
                ThingCategoryDef books = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("TSC_ClassBooks");
                List<ThingDef> pool = books != null
                    ? new List<ThingDef>(books.DescendantThingDefs)
                    : new List<ThingDef>();
                if (pool.Count > 0)
                {
                    SpawnBeside(map, cell, pool.RandomElement(), 1);
                }
            }
            if (beastKinds != null && beastKinds.Count > 0 && Rand.Chance(beastChance))
            {
                // What got them, a stone's throw away. It did not walk far either.
                IntVec3 beastCell = cell + IntVec3Utility.RandomHorizontalOffset(Rand.Range(6f, 13f));
                if (beastCell.InBounds(map) && beastCell.Standable(map))
                {
                    SpawnAgedCorpse(map, beastCell, beastKinds.RandomElement(), null);
                }
            }
            if (Rand.Chance(journalChance))
            {
                // A check spot is a building: GenSpawn on a clear cell, not GenPlace.
                ThingDef journal = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_TrailJournal");
                if (journal != null)
                {
                    foreach (IntVec3 spot in GenAdj.CellsAdjacent8Way(new TargetInfo(cell, map)))
                    {
                        if (spot.InBounds(map) && spot.Standable(map) && spot.GetFirstBuilding(map) == null)
                        {
                            GenSpawn.Spawn(journal, spot, map);
                            break;
                        }
                    }
                }
            }
            if (Rand.Chance(courierChance))
            {
                SpawnBeside(map, cell, DefDatabase<ThingDef>.GetNamedSilentFail("TSC_SealedDispatch"), 1);
            }
        }

        private static void SpawnBeside(Map map, IntVec3 center, ThingDef def, int count)
        {
            if (def == null || count <= 0)
            {
                return;
            }
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            GenPlace.TryPlaceThing(thing, center, map, ThingPlaceMode.Near);
        }
    }

    /// <summary>
    /// Somebody wintered here and moved on - or meant to.
    ///
    /// A cold campfire, a bedroll or two gone stiff with weather, dropped
    /// provisions. Pure scene-setting: the site was somewhere on a road
    /// people actually travel, and the party is not the first to think the
    /// spot defensible.
    /// </summary>
    public class GenStep_TSC_ColdCamp : GenStep
    {
        public float chance = 0.3f;
        public IntRange bedrolls = new IntRange(1, 2);
        public IntRange food = new IntRange(8, 20);

        public override int SeedPart => 662114809;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!Rand.Chance(chance))
            {
                return;
            }
            IntVec3 center = IntVec3.Invalid;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                IntVec3 candidate = map.Center + IntVec3Utility.RandomHorizontalOffset(Rand.Range(15f, 32f));
                if (candidate.InBounds(map) && candidate.Standable(map)
                    && candidate.GetRoom(map)?.PsychologicallyOutdoors != false)
                {
                    center = candidate;
                    break;
                }
            }
            if (!center.IsValid)
            {
                return;
            }
            ThingDef campfire = DefDatabase<ThingDef>.GetNamedSilentFail("Campfire");
            if (campfire != null)
            {
                Thing fire = GenSpawn.Spawn(campfire, center, map);
                fire.TryGetComp<CompRefuelable>()?.ConsumeFuel(99999f); // burned out long ago
                FilthMaker.TryMakeFilth(center, map, ThingDefOf.Filth_Ash);
            }
            int rolls = bedrolls.RandomInRange;
            ThingDef bedroll = DefDatabase<ThingDef>.GetNamedSilentFail("Bedroll");
            for (int i = 0; i < rolls && bedroll != null; i++)
            {
                IntVec3 cell = center + GenAdj.AdjacentCellsAndInside[Rand.Range(0, 8)] * 2;
                if (cell.InBounds(map) && cell.Standable(map) && cell.GetFirstBuilding(map) == null)
                {
                    Thing roll = GenSpawn.Spawn(bedroll, cell, map, Rot4.Random);
                    roll.HitPoints = Mathf.Max(1, (int)(roll.MaxHitPoints * Rand.Range(0.25f, 0.6f)));
                }
            }
            Thing rations = ThingMaker.MakeThing(ThingDefOf.Pemmican);
            rations.stackCount = food.RandomInRange;
            GenPlace.TryPlaceThing(rations, center, map, ThingPlaceMode.Near);
        }
    }

    /// <summary>
    /// An old battlefield: two crews met here long before the party, and
    /// neither won anything. A dozen dessicated dead, rusted weapon litter,
    /// and - for the company that searches every corpse instead of riding
    /// past - one genuinely good piece among the wreckage.
    /// </summary>
    public class GenStep_TSC_Battlefield : GenStep
    {
        public IntRange corpses = new IntRange(8, 14);
        public IntRange weaponLitter = new IntRange(4, 8);
        public float radius = 16f;

        public override int SeedPart => 337221954;

        public override void Generate(Map map, GenStepParams parms)
        {
            PawnKindDef brigand = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand");
            PawnKindDef archer = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Archer");
            Faction faction = TSC_BanditFactionUtility.Get();
            if (brigand == null || faction == null)
            {
                return;
            }
            IntVec3 center = map.Center;
            int count = corpses.RandomInRange;
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = center + IntVec3Utility.RandomHorizontalOffset(Rand.Range(2f, radius));
                if (!cell.InBounds(map) || !cell.Standable(map))
                {
                    continue;
                }
                PawnKindDef kind = archer != null && Rand.Chance(0.35f) ? archer : brigand;
                GenStep_TSC_FallenAdventurer.SpawnAgedCorpse(map, cell, kind, faction);
            }
            // Rusted litter: the weapons nobody thought worth carrying away.
            List<ThingDef> weapons = new List<ThingDef>(TSC_MedievalGear.Weapons);
            int litter = weaponLitter.RandomInRange;
            for (int i = 0; i < litter && weapons.Count > 0; i++)
            {
                IntVec3 cell = center + IntVec3Utility.RandomHorizontalOffset(Rand.Range(2f, radius));
                if (!cell.InBounds(map) || !cell.Standable(map))
                {
                    continue;
                }
                ThingDef litterDef = weapons.RandomElement();
                Thing rusted = ThingMaker.MakeThing(litterDef, GenStuff.DefaultStuffFor(litterDef));
                rusted.HitPoints = Mathf.Max(1, (int)(rusted.MaxHitPoints * Rand.Range(0.1f, 0.35f)));
                GenPlace.TryPlaceThing(rusted, cell, map, ThingPlaceMode.Near);
            }
            // The prize: one piece worth the grave-walking.
            if (weapons.Count > 0)
            {
                ThingDef prizeDef = weapons.RandomElement();
                Thing prize = ThingMaker.MakeThing(prizeDef, GenStuff.DefaultStuffFor(prizeDef));
                prize.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Masterwork,
                    ArtGenerationContext.Outsider);
                IntVec3 cell = center + IntVec3Utility.RandomHorizontalOffset(Rand.Range(1f, radius * 0.5f));
                GenPlace.TryPlaceThing(prize, cell.InBounds(map) ? cell : center, map, ThingPlaceMode.Near);
            }
        }
    }

    /// <summary>
    /// Another company got here first, and they are ALIVE.
    ///
    /// Neutral riders working the same find, wired to the parley component
    /// the crypt looters and the tribute collectors already use: walk up
    /// and words happen. Persuade them off the claim, pay them off, or
    /// start something - the dialogue decides, the component executes.
    /// </summary>
    public class GenStep_TSC_RivalCompany : GenStep
    {
        public float chance = 0.25f;
        public IntRange count = new IntRange(2, 4);

        public override int SeedPart => 774431226;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!Rand.Chance(chance))
            {
                return;
            }
            PawnKindDef brigand = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand");
            PawnKindDef archer = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Archer");
            Faction faction = TSC_BanditFactionUtility.Get();
            MapComponent_TSC_CryptParley parley = map.GetComponent<MapComponent_TSC_CryptParley>();
            if (brigand == null || faction == null || parley == null)
            {
                return;
            }
            parley.Configure("TSC_Dialogue_WildRivals", "TSC_WildRivalsMet",
                "The rival company draws steel!",
                "The rival company weighs the odds, spits, and rides off the claim.");
            List<IntVec3> interior = GenStep_TSC_PlaceInStructure.InteriorCells(map);
            IntVec3 post = interior.Count > 0 ? interior.RandomElement() : map.Center;
            int n = count.RandomInRange;
            for (int i = 0; i < n; i++)
            {
                PawnKindDef kind = archer != null && i % 3 == 2 ? archer : brigand;
                Pawn rival = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind, faction, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(post, map, 4);
                GenSpawn.Spawn(rival, cell, map);
                parley.Register(rival, isLeader: i == 0);
            }
        }
    }

    /// <summary>
    /// A thing that appears where a tracked pawn falls.
    ///
    /// Built for the toll book: as a scattered check spot it could generate
    /// in open scrub half a map from the toll house, which read as debris,
    /// not discovery. Carried by the crew's bookkeeper and dropped where
    /// they go down, it is where the fight was - and looting the fallen is
    /// something every player already does.
    ///
    /// The drop triggers on death, downing, or despawning (a fleeing keeper
    /// sheds the book at the map edge rather than carrying the questline
    /// away with them). One drop per tracked pawn, then the entry is done.
    /// </summary>
    public class MapComponent_TSC_DropOnFall : MapComponent
    {
        private List<Pawn> pawns = new List<Pawn>();
        private List<ThingDef> things = new List<ThingDef>();
        private List<IntVec3> lastCells = new List<IntVec3>();

        public MapComponent_TSC_DropOnFall(Map map) : base(map) { }

        public static void Track(Pawn pawn, ThingDef def)
        {
            MapComponent_TSC_DropOnFall comp = pawn?.Map?.GetComponent<MapComponent_TSC_DropOnFall>();
            if (comp == null || def == null)
            {
                return;
            }
            comp.pawns.Add(pawn);
            comp.things.Add(def);
            comp.lastCells.Add(pawn.Position);
        }

        public override void MapComponentTick()
        {
            if (pawns.Count == 0 || Find.TickManager.TicksGame % 30 != 0)
            {
                return;
            }
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = pawns[i];
                if (pawn == null)
                {
                    Drop(i); // reference lost (destroyed corpse etc.): last known cell
                    continue;
                }
                if (pawn.Spawned && pawn.Map == map && !pawn.Dead && !pawn.Downed)
                {
                    lastCells[i] = pawn.Position;
                    continue;
                }
                if (pawn.Dead || pawn.Downed || !pawn.Spawned)
                {
                    if (pawn.Spawned && pawn.Map == map)
                    {
                        lastCells[i] = pawn.Position;
                    }
                    else if (pawn.Corpse != null && pawn.Corpse.Spawned && pawn.Corpse.Map == map)
                    {
                        lastCells[i] = pawn.Corpse.Position;
                    }
                    Drop(i);
                }
            }
        }

        private void Drop(int i)
        {
            IntVec3 cell = lastCells[i];
            ThingDef def = things[i];
            pawns.RemoveAt(i);
            things.RemoveAt(i);
            lastCells.RemoveAt(i);
            if (!cell.IsValid || !cell.InBounds(map))
            {
                cell = map.Center;
            }
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(cell, 6f, useCenter: true))
            {
                if (candidate.InBounds(map) && candidate.Standable(map)
                    && candidate.GetFirstBuilding(map) == null)
                {
                    cell = candidate;
                    break;
                }
            }
            Thing thing = GenSpawn.Spawn(ThingMaker.MakeThing(def), cell, map);
            Messages.Message($"Something fell with them: {thing.LabelCap} lies where the fight was.",
                thing, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            Scribe_Collections.Look(ref things, "things", LookMode.Def);
            Scribe_Collections.Look(ref lastCells, "lastCells", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawns = pawns ?? new List<Pawn>();
                things = things ?? new List<ThingDef>();
                lastCells = lastCells ?? new List<IntVec3>();
                // A reference that failed to resolve still owes its drop;
                // keep list lengths in step so indices stay honest.
                while (lastCells.Count < pawns.Count) lastCells.Add(map.Center);
                while (things.Count < pawns.Count) things.Add(null);
            }
        }
    }

    /// <summary>
    /// An actual tower, for the "collapsed tower" discoveries.
    ///
    /// Both tower sites shipped running bare GenStep_AncientRuins - no
    /// layout - so vanilla generated its default city-block ruin complex
    /// and the map contradicted the label. This draws what the description
    /// promises: one compact stone shell, a doorway, and - for the mundane
    /// variant - one side gone, its stones fanned outward as chunks and
    /// rubble where the wall came down. The sorcerer's variant keeps its
    /// walls (`collapsed` false): "the air inside is too still" is not a
    /// thing one says about a pile.
    /// </summary>
    public class GenStep_TSC_CollapsedTower : GenStep
    {
        public int size = 11;
        public bool collapsed = true;

        public override int SeedPart => 559144302;

        public override void Generate(Map map, GenStepParams parms)
        {
            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef stuff = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksGranite");
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail("FlagstoneGranite");
            if (wallDef == null || stuff == null)
            {
                return;
            }
            CellRect shell = CellRect.CenteredOn(map.Center, size, size).ClipInsideMap(map);
            // The doorway: a two-cell gap mid-south. The collapse: one OTHER
            // side chosen at random, mostly gone.
            Rot4 fallen = new[] { Rot4.North, Rot4.East, Rot4.West }.RandomElement();
            foreach (IntVec3 cell in shell)
            {
                bool edge = cell.x == shell.minX || cell.x == shell.maxX
                    || cell.z == shell.minZ || cell.z == shell.maxZ;
                if (!edge)
                {
                    if (floor != null)
                    {
                        map.terrainGrid.SetTerrain(cell, floor);
                    }
                    continue;
                }
                bool doorway = cell.z == shell.minZ
                    && (cell.x == shell.CenterCell.x || cell.x == shell.CenterCell.x + 1);
                if (doorway)
                {
                    continue;
                }
                bool onFallenSide = collapsed
                    && ((fallen == Rot4.North && cell.z == shell.maxZ)
                        || (fallen == Rot4.East && cell.x == shell.maxX)
                        || (fallen == Rot4.West && cell.x == shell.minX));
                if (onFallenSide && Rand.Chance(0.65f))
                {
                    // The stone went SOMEWHERE: chunks and rubble fan outward
                    // from the breach, so the collapse reads as an event,
                    // not an absence.
                    IntVec3 debris = cell + fallen.FacingCell * Rand.RangeInclusive(1, 4)
                        + IntVec3Utility.RandomHorizontalOffset(1.5f);
                    ThingDef chunk = DefDatabase<ThingDef>.GetNamedSilentFail("ChunkGranite");
                    if (chunk != null && debris.InBounds(map) && debris.Standable(map) && Rand.Chance(0.55f))
                    {
                        GenSpawn.Spawn(chunk, debris, map);
                    }
                    if (cell.InBounds(map))
                    {
                        FilthMaker.TryMakeFilth(cell, map, ThingDefOf.Filth_RubbleRock);
                    }
                    continue;
                }
                if (cell.GetEdifice(map) == null)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(wallDef, stuff), cell, map);
                }
            }
            // Roof only survives along the standing walls; the middle is sky.
            // "Roof long fallen" is the def text, and now it is also true.
            if (!collapsed)
            {
                foreach (IntVec3 cell in shell.ContractedBy(1))
                {
                    map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                }
            }
        }
    }

    /// <summary>
    /// One possible set of occupants: who they answer to, what they are, and
    /// whether they are already angry. Pawn kinds are named as strings so a
    /// crew built on another mod's creature simply drops out of the roll in
    /// a load order without it, rather than erroring at startup.
    /// </summary>
    public class TSC_GuardCrew
    {
        /// <summary>"bandits", "insects", or "wild". Null keeps the genstep's own.</summary>
        public string faction;

        public List<string> kinds = new List<string>();

        /// <summary>Wild crews only: spawn them already hunting.</summary>
        public bool manhunter;

        /// <summary>Roll one kind for the whole crew: a wolf pack, not a menagerie.</summary>
        public bool pickOneKind = true;

        public float weight = 1f;

        private List<PawnKindDef> resolved;

        public List<PawnKindDef> Kinds()
        {
            if (resolved != null)
            {
                return resolved;
            }
            resolved = new List<PawnKindDef>();
            foreach (string name in kinds)
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(name);
                if (kind != null)
                {
                    resolved.Add(kind);
                }
            }
            return resolved;
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

        /// <summary>
        /// A thing one member of this crew carries and drops where they
        /// fall (the toll book). Guaranteed either way: if the crew cannot
        /// spawn at all, the thing is placed directly instead - a missing
        /// pawn must never cost the player a questline.
        /// </summary>
        public string dropOnFall;

        /// <summary>
        /// Wild only: rabid - permanent man-hunters. Required when a
        /// CONTRACT is built on these spawns: passive wild animals are not
        /// hostile threats, so "all enemies defeated" would fire with them
        /// still alive (or instantly). A rabid pack counts, and fights.
        /// </summary>
        public bool manhunter;

        /// <summary>
        /// Roll ONE kind from the list and spawn the whole group as it, so
        /// `kinds` becomes a menu of possible packs rather than a recipe
        /// for a mixed zoo. Duplicate entries weight the roll. This is what
        /// makes the same site wolves one visit and boars the next.
        /// </summary>
        public bool pickOneKind;

        /// <summary>
        /// A menu of whole CREWS, one rolled per map: faction, roster and
        /// temper together.
        ///
        /// `kinds` alone can vary the animals in a den but cannot change who
        /// the occupants ARE, which is what the warren needed - its second
        /// set of tenants was insects every single time, so the contract had
        /// one surprise in it and never again. Set this and the crew is
        /// rolled here; faction, kinds, manhunter and pickOneKind on the
        /// genstep itself become the defaults a crew may override.
        /// </summary>
        public List<TSC_GuardCrew> crews;

        public override int SeedPart => 771604318;

        /// <summary>
        /// Weighted pick, skipping any crew whose pawn kinds a given load
        /// order does not actually have. A crew that resolves to nothing is
        /// not a crew.
        /// </summary>
        private TSC_GuardCrew RollCrew()
        {
            if (crews.NullOrEmpty())
            {
                return null;
            }
            List<TSC_GuardCrew> usable = new List<TSC_GuardCrew>();
            float total = 0f;
            foreach (TSC_GuardCrew candidate in crews)
            {
                if (candidate.Kinds().Count == 0)
                {
                    continue;
                }
                usable.Add(candidate);
                total += Mathf.Max(0.01f, candidate.weight);
            }
            if (usable.Count == 0)
            {
                return null;
            }
            float roll = Rand.Range(0f, total);
            foreach (TSC_GuardCrew candidate in usable)
            {
                roll -= Mathf.Max(0.01f, candidate.weight);
                if (roll <= 0f)
                {
                    return candidate;
                }
            }
            return usable[usable.Count - 1];
        }

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
            // One crew off the menu, if this genstep carries a menu. Rolled
            // before anything else reads faction or kinds, so the whole step
            // runs as though the crew had been written in by hand.
            TSC_GuardCrew crew = RollCrew();
            if (crew != null)
            {
                faction = crew.faction ?? faction;
                kinds = crew.Kinds();
                manhunter = crew.manhunter;
                pickOneKind = crew.pickOneKind;
            }
            Faction holders = ResolveFaction();
            List<PawnKindDef> roster = kinds != null && kinds.Count > 0 ? kinds : null;
            if (roster != null && pickOneKind)
            {
                roster = new List<PawnKindDef> { roster.RandomElement() };
            }
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
            else if (guards.Count > 0 && manhunter && faction == "wild")
            {
                foreach (Pawn beast in guards)
                {
                    beast.mindState?.mentalStateHandler?.TryStartMentalState(
                        MentalStateDefOf.ManhunterPermanent, "rabid", forced: true);
                }
            }
            if (!dropOnFall.NullOrEmpty())
            {
                ThingDef carried = DefDatabase<ThingDef>.GetNamedSilentFail(dropOnFall);
                if (carried == null)
                {
                    return;
                }
                if (guards.Count > 0)
                {
                    MapComponent_TSC_DropOnFall.Track(guards.RandomElement(), carried);
                }
                else
                {
                    // No crew, no keeper: place it outright rather than lose it.
                    IntVec3 cell = interior.Count > 0 ? interior.RandomElement() : post;
                    GenSpawn.Spawn(ThingMaker.MakeThing(carried), cell, map);
                }
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
        /// <summary>Several named captives at one site (Act 5's road ambush holds Serra AND Oswin). Dead ones are skipped.</summary>
        public List<NamedNpcDef> npcs = new List<NamedNpcDef>();
        /// <summary>Quest signal sent once every captive here is free (Act 5's ambush completes on it).</summary>
        public string allFreedSignalQuest;
        public string allFreedSignal;

        public override int SeedPart => 668142935;

        public override void Generate(Map map, GenStepParams parms)
        {
            MapComponent_TSC_CaptiveRescue rescueComp = map.GetComponent<MapComponent_TSC_CaptiveRescue>();
            if (rescueComp != null && !allFreedSignal.NullOrEmpty())
            {
                rescueComp.allFreedSignalQuest = allFreedSignalQuest;
                rescueComp.allFreedSignal = allFreedSignal;
            }
            if (npcs != null && npcs.Count > 0)
            {
                foreach (NamedNpcDef def in npcs)
                {
                    if (def == null)
                    {
                        continue;
                    }
                    Pawn named = DialogueStateManager.Current.GetOrGenerateNamedNpc(def, null);
                    if (named == null || named.Dead || named.Spawned)
                    {
                        continue; // dead, or already out in the world
                    }
                    PlaceCaptive(map, named);
                }
                return;
            }
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

        /// <summary>Same holding rules, for each of several named captives.</summary>
        private static void PlaceCaptive(Map map, Pawn captive)
        {
            IntVec3 den = GenStep_TSC_PlaceInStructure.FindInteriorCell(map);
            if (!den.IsValid || !den.Walkable(map))
            {
                den = map.Center;
            }
            TraverseParms walk = TraverseParms.For(TraverseMode.PassDoors);
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(den, map, 6);
            if (!cell.IsValid || !map.reachability.CanReachMapEdge(cell, walk))
            {
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, 60f, useCenter: true))
                {
                    if (candidate.InBounds(map) && candidate.Standable(map)
                        && map.reachability.CanReachMapEdge(candidate, walk))
                    {
                        cell = candidate;
                        break;
                    }
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
        /// <summary>Extra captives held at the same site (Act 5 holds two).</summary>
        private List<Pawn> others = new List<Pawn>();
        /// <summary>Sent once every registered captive is free. Act 5's ambush quest listens for it.</summary>
        public string allFreedSignalQuest;
        public string allFreedSignal;

        public MapComponent_TSC_CaptiveRescue(Map map) : base(map)
        {
        }

        public void Register(Pawn pawn)
        {
            if (captive == null)
            {
                captive = pawn;
                return;
            }
            if (pawn != null && pawn != captive && !others.Contains(pawn))
            {
                others.Add(pawn);
            }
        }

        /// <summary>Every captive here is free, dead, or gone.</summary>
        private bool AllSettled()
        {
            if (!joined && captive != null && !captive.Dead)
            {
                return false;
            }
            foreach (Pawn other in others)
            {
                if (other != null && !other.Dead && other.Faction != Faction.OfPlayer)
                {
                    return false;
                }
            }
            return true;
        }

        private void TickOthers()
        {
            foreach (Pawn other in others)
            {
                if (other == null || other.Dead || other.Faction == Faction.OfPlayer)
                {
                    continue;
                }
                if (!other.Spawned)
                {
                    Pawn carrier = (other.ParentHolder as Pawn_CarryTracker)?.pawn;
                    if (carrier != null && carrier.Faction == Faction.OfPlayer)
                    {
                        FreeOne(other);
                    }
                    continue;
                }
                foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist.Position.InHorDistOf(other.Position, 3f))
                    {
                        FreeOne(other);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Checked once per session against the party's ACTUAL position,
        /// deliberately not saved: every visit re-verifies.
        /// </summary>
        private bool reachabilityVerified;

        /// <summary>
        /// The placement genstep guarantees the captive can reach A map
        /// edge - but two sealed regions can each touch DIFFERENT edges
        /// without connecting to each other, and a prisoner in a cave that
        /// opens onto the far rim passed that test while the party stared
        /// at solid rock (seen in play: a rescue that needed mining).
        /// Nobody knows the party's entry side at map generation, so the
        /// honest check happens here, against a real colonist, and anyone
        /// walled off is moved to the deepest cell the party can actually
        /// walk to.
        /// </summary>
        private void EnsureReachable()
        {
            Pawn anchor = null;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                anchor = colonist;
                break;
            }
            if (anchor == null)
            {
                return;
            }
            RelocateIfSealed(anchor, captive);
            foreach (Pawn other in others)
            {
                RelocateIfSealed(anchor, other);
            }
        }

        private void RelocateIfSealed(Pawn anchor, Pawn prisoner)
        {
            if (prisoner == null || prisoner.Dead || !prisoner.Spawned
                || prisoner.Faction == Faction.OfPlayer)
            {
                return;
            }
            TraverseParms walk = TraverseParms.For(TraverseMode.PassDoors);
            if (map.reachability.CanReach(anchor.Position, prisoner.Position, Verse.AI.PathEndMode.Touch, walk))
            {
                return;
            }
            // Sealed off. The deepest reachable cell keeps the shape of the
            // rescue - a walk into the dark - without the pickaxe.
            IntVec3 best = IntVec3.Invalid;
            float bestScore = -1f;
            for (int i = 0; i < 250; i++)
            {
                IntVec3 candidate = CellFinder.RandomCell(map);
                if (!candidate.Standable(map)
                    || candidate.GetFirstPawn(map) != null
                    || !map.reachability.CanReach(anchor.Position, candidate, Verse.AI.PathEndMode.OnCell, walk))
                {
                    continue;
                }
                // Never in the party's laps: a hard penalty inside 12 cells
                // of any colonist means close cells only win when the whole
                // reachable world is a pocket - in which case close is the
                // truth, and still better than sealed.
                float nearestColonist = float.MaxValue;
                foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
                {
                    nearestColonist = Mathf.Min(nearestColonist, candidate.DistanceTo(colonist.Position));
                }
                float score = candidate.DistanceTo(anchor.Position)
                    + (candidate.Roofed(map) ? 40f : 0f)
                    + (nearestColonist < 12f ? -1000f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            if (!best.IsValid)
            {
                return; // no reachable ground at all; nothing sane to do
            }
            IntVec3 from = prisoner.Position;
            prisoner.DeSpawn();
            GenSpawn.Spawn(prisoner, best, map);
            Log.Warning($"[The Shattered Crown] Captive {prisoner.LabelShortCap} was sealed off from the "
                + $"party (at {from}, no path from {anchor.Position}); relocated to {best}.");
        }

        private void FreeOne(Pawn pawn)
        {
            pawn.SetFaction(Faction.OfPlayer);
            TSC_Homeward.Mark(pawn);
            Find.LetterStack.ReceiveLetter(
                pawn.LabelShortCap + " freed",
                pawn.LabelShortCap + " is in no shape to argue with rescue. "
                + "They fall in with the company, as far as the next friendly gates.",
                LetterDefOf.PositiveEvent, pawn);
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
            if (Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            TickOthers();
            if (!allFreedSignal.NullOrEmpty() && AllSettled())
            {
                TSC_QuestSignals.Send(allFreedSignalQuest, allFreedSignal);
                allFreedSignal = null;
            }
            if (joined || captive == null || captive.Dead)
            {
                return;
            }
            if (!reachabilityVerified && map.mapPawns.FreeColonistsSpawnedCount > 0)
            {
                reachabilityVerified = true;
                EnsureReachable();
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

        /// <summary>
        /// The reform hole: scoop the captive while the caravan is packing
        /// and the map can close before the next 60-tick poll, so the join
        /// never fired and the rescue rode out as a downed NEUTRAL
        /// passenger (Esema, age 13, in playtest). When the map goes away,
        /// anyone who left inside a player caravan joins on the way out.
        /// </summary>
        public override void MapRemoved()
        {
            base.MapRemoved();
            if (!joined && captive != null && !captive.Dead
                && captive.GetCaravan()?.IsPlayerControlled == true)
            {
                JoinParty();
            }
            foreach (Pawn other in others)
            {
                if (other != null && !other.Dead && other.Faction != Faction.OfPlayer
                    && other.GetCaravan()?.IsPlayerControlled == true)
                {
                    FreeOne(other);
                }
            }
        }

        private void JoinParty()
        {
            joined = true;
            captive.SetFaction(Faction.OfPlayer);
            TSC_Homeward.Mark(captive);
            Find.LetterStack.ReceiveLetter(
                $"{captive.LabelShortCap} freed",
                $"{captive.LabelShortCap} was not going to walk out of this place alone, and knows it. "
                + "They throw in with the party: no rate, no charter, just owed - and only as far "
                + "as the next friendly gates.",
                LetterDefOf.PositiveEvent, captive);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref captive, "captive");
            Scribe_Values.Look(ref joined, "joined");
            Scribe_Values.Look(ref announced, "announced");
            Scribe_Collections.Look(ref others, "otherCaptives", LookMode.Reference);
            Scribe_Values.Look(ref allFreedSignalQuest, "allFreedSignalQuest");
            Scribe_Values.Look(ref allFreedSignal, "allFreedSignal");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && others == null)
            {
                others = new List<Pawn>();
            }
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
