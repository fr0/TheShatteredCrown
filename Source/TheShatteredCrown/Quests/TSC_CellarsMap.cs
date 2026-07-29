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
    /// The Sunken Cellars: Act 2's dungeon, three pocket maps deep. Each
    /// level is one of these gensteps - carve the block, drop the catacomb
    /// layout in it, then dress it with that level's own business:
    ///
    ///   1  The Weeping Undercroft - flooded galleries, the beggar's last
    ///      camp, the sluice wheel, scarabs in the standing water.
    ///   2  The Ossuary - bone galleries and the hollow choir; the dead here
    ///      answer singing, which is the only quiet way past them.
    ///   3  The Reliquary - the Kingsblade, the second shard, and the last
    ///      chorister of the old kingdom, still singing.
    ///
    /// Levels 1 and 2 spawn a stair portal down (placed FAR from the way in,
    /// so descending means crossing the level). Level 3 has no stair: it has
    /// the reliquary.
    /// </summary>
    /// <summary>
    /// Who holds a dungeon floor. Rolled per level so a delve is not one
    /// note all the way down: a bandit crew squatting the upper works, a
    /// nest in the flooded middle, something worse at the bottom.
    /// </summary>
    public class TSC_DungeonOccupant
    {
        public float weight = 1f;
        /// <summary>insects | bandits | wild - decides faction and behaviour.</summary>
        public string faction = "insects";
        public List<PawnKindDef> kinds = new List<PawnKindDef>();
        public IntRange count = new IntRange(3, 5);
        /// <summary>Shown when the party first sets foot on the floor.</summary>
        public string arrivalNote;
    }

    /// <summary>
    /// How deep THIS delve goes. Rolled once on the surface map and read by
    /// every floor below it, so a procedural delve is 1 to 4 floors and the
    /// party does not know which until they run out of stairs.
    /// </summary>
    public class MapComponent_TSC_DelveDepth : MapComponent
    {
        public int depth;

        public MapComponent_TSC_DelveDepth(Map map) : base(map)
        {
        }

        /// <summary>
        /// Resolve the depth for any floor of a delve: walk UP the pocket
        /// chain to the surface map (pocket maps have no world tile of their
        /// own) and read what was rolled there. 0 when this is not a
        /// variable-depth delve at all - the hand-authored cellars.
        /// </summary>
        public static int ForMap(Map map)
        {
            Map surface = map;
            int guard = 8;
            while (surface != null && !surface.Tile.Valid && guard-- > 0)
            {
                surface = (surface.Parent as PocketMapParent)?.sourceMap;
            }
            return surface?.GetComponent<MapComponent_TSC_DelveDepth>()?.depth ?? 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref depth, "depth");
        }
    }

    /// <summary>
    /// Rolls a delve's depth on the surface map. Added to the delve site's
    /// genstep list only - the cellars never run it, so they keep their
    /// authored three floors.
    ///
    /// With `chance` below 1 the site may get NO delve at all: used by
    /// ruined fortresses, where only some castles have anything under them.
    /// The stairhead is placed by this step too, so "no dungeon" means no
    /// stair rather than a stair into nothing.
    /// </summary>
    public class GenStep_TSC_DelveDepth : GenStep
    {
        public IntRange depthRange = new IntRange(1, 4);
        /// <summary>Probability this site has a delve under it at all.</summary>
        public float chance = 1f;
        /// <summary>When set, this step also places the way in (castles: only if the roll succeeded).</summary>
        public ThingDef entranceDef;

        public override int SeedPart => 118837225;

        public override void Generate(Map map, GenStepParams parms)
        {
            MapComponent_TSC_DelveDepth comp = map.GetComponent<MapComponent_TSC_DelveDepth>();
            if (comp == null)
            {
                return;
            }
            if (!Rand.Chance(chance))
            {
                Log.Message("[The Shattered Crown] No delve under this site (roll failed).");
                return;
            }
            comp.depth = depthRange.RandomInRange;
            Log.Message($"[The Shattered Crown] Delve depth rolled: {comp.depth} floor(s).");
            if (entranceDef != null)
            {
                PlaceEntrance(map);
            }
        }

        /// <summary>
        /// Set the stairhead inside the structure if there is room for it,
        /// otherwise just outside: a fortress cellar should read as part of
        /// the fortress, but never at the cost of sealing itself in.
        /// </summary>
        private void PlaceEntrance(Map map)
        {
            IntVec2 size = entranceDef.size;
            IntVec3 spot = IntVec3.Invalid;
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, 30f, useCenter: true).InRandomOrder())
            {
                CellRect rect = GenAdj.OccupiedRect(candidate, Rot4.North, size).ExpandedBy(1);
                if (!rect.InBounds(map))
                {
                    continue;
                }
                bool clear = true;
                foreach (IntVec3 cell in rect)
                {
                    if (!cell.Standable(map) || cell.GetEdifice(map) != null)
                    {
                        clear = false;
                        break;
                    }
                }
                if (!clear || !map.reachability.CanReachMapEdge(candidate, TraverseParms.For(TraverseMode.PassDoors)))
                {
                    continue;
                }
                spot = candidate;
                break;
            }
            if (!spot.IsValid)
            {
                Log.Warning("[The Shattered Crown] Delve entrance found no clear spot; site generated without one.");
                return;
            }
            GenSpawn.Spawn(ThingMaker.MakeThing(entranceDef), spot, map);
        }
    }

    public class GenStep_TSC_CellarLevel : GenStep
    {
        public int level = 1;
        /// <summary>
        /// Opt in to rolled depth (procedural delves). When true, this floor
        /// spawns its down-stair only if the delve goes deeper, and places
        /// the loot only if it IS the bottom. The authored cellars leave
        /// this false and keep their fixed layout.
        /// </summary>
        public bool variableDepth;
        public LayoutDef layoutDef;
        public int size = 44;
        /// <summary>Stair portal to the next level down (null on the last level).</summary>
        public ThingDef stairsDef;
        /// <summary>Vermin nesting on this level; spawned in clusters away from the entrance.</summary>
        public PawnKindDef vermin;
        public IntRange verminCount = new IntRange(3, 5);

        /// <summary>
        /// Hard cap on dormant insect packs the LAYOUT may leave on this
        /// floor. Room chances are probabilistic (each crypt gallery rolls
        /// its own cluster), so a floor could come out wall-to-wall nests;
        /// this prunes the excess after generation. The floor's total is
        /// then this plus one roaming pack from `vermin`/`occupants`.
        /// </summary>
        public int maxDormantPacks = 2;
        /// <summary>Weighted occupant profiles; one is rolled per generated level. Takes precedence over `vermin`.</summary>
        public List<TSC_DungeonOccupant> occupants = new List<TSC_DungeonOccupant>();
        /// <summary>Set spots by def: check-spot props, the beggar's camp, the choir bones.</summary>
        public List<ThingDef> spots = new List<ThingDef>();
        /// <summary>Placed in a carved chamber at the deepest point (the delve's prize).</summary>
        public ThingDef lootDef;

        public override int SeedPart => 386612094 + level;

        public override void Generate(Map map, GenStepParams parms)
        {
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
                // Threat points go to the layout on the lower levels only: the
                // first floor's danger is what WE place (the scarabs), so the
                // way in is never a wall of ambushes before the player has
                // read a single thing.
                worker.Spawn(sketch, map, rect.Min, threatPoints: level >= 2 ? parms.sitePart?.parms.threatPoints : null);
            }
            foreach (IntVec3 cell in rect)
            {
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
            }

            IntVec3 entry = MapGenerator.PlayerStartSpot;
            IntVec3 deepest = FarthestStandable(map, rect, entry);

            // Fixed layout (the authored cellars) unless this genstep opted
            // into rolled depth. A variable-depth floor is the BOTTOM when
            // the delve does not go deeper than it: no stair down, and the
            // prize is here.
            bool goesDeeper = true;
            bool isBottom = false;
            if (variableDepth)
            {
                int depth = MapComponent_TSC_DelveDepth.ForMap(map);
                if (depth <= 0)
                {
                    depth = 3; // depth roll missing (old save, odd entry): sane default
                }
                goesDeeper = level < depth;
                isBottom = !goesDeeper;
            }

            if (stairsDef != null && goesDeeper)
            {
                SpawnStairs(map, deepest, entry);
            }
            PlaceSpots(map, rect, entry);
            PruneDormantPacks(map, rect, deepest);
            // On the reliquary floor the chorister is standing at `deepest`,
            // and insects are hostile to him as much as to the party. Keep
            // the loose ones away from his chamber.
            bool hasReliquary = !variableDepth && level >= 3;
            SpawnOccupants(map, rect, entry, hasReliquary ? deepest : IntVec3.Invalid);
            if (lootDef != null && (!variableDepth || isBottom))
            {
                PlaceLoot(map, deepest);
            }
            if (!variableDepth && level >= 3)
            {
                map.GetComponent<MapComponent_TSC_Reliquary>()?.Build(deepest);
            }
            // The guarantee (same rule as the crypt): the way in must reach
            // the way down, whatever the layout did.
            EnsurePath(map, entry, deepest);
        }

        /// <summary>The stair down, set in its own small landing so a layout wall never seals it.</summary>
        private void SpawnStairs(Map map, IntVec3 at, IntVec3 entry)
        {
            IntVec3 cell = CellFinder.StandableCellNear(at, map, 12f);
            if (!cell.IsValid)
            {
                cell = at;
            }
            foreach (IntVec3 clear in CellRect.CenteredOn(cell, 2, 2).ClipInsideMap(map))
            {
                CarveCell(map, clear);
            }
            GenSpawn.Spawn(stairsDef, cell, map);
        }

        private void PruneDormantPacks(Map map, CellRect rect, IntVec3 deepest)
        {
            TSC_PackBudget.Prune(map, maxDormantPacks, deepest);
        }

        private void PlaceSpots(Map map, CellRect rect, IntVec3 entry)
        {
            foreach (ThingDef spotDef in spots)
            {
                if (spotDef == null)
                {
                    continue;
                }
                // Spread the set pieces through the level rather than piling
                // them at the door: each lands at least a room away from the
                // entrance, in a spot a pawn can actually stand beside.
                //
                // Wall-adjacent first: these props DESCRIBE themselves as
                // wall features (a skull-stacked gallery wall, a sluice wheel
                // set in stone), and a "wall of skulls" standing alone in the
                // middle of an open room reads as a spawn bug. Only if forty
                // tries find no wall seat does it fall back to open floor.
                IntVec3 cell = IntVec3.Invalid;
                IntVec3 fallback = IntVec3.Invalid;
                for (int attempt = 0; attempt < 80 && !cell.IsValid; attempt++)
                {
                    IntVec3 candidate = rect.RandomCell;
                    if (!candidate.Standable(map) || candidate.DistanceTo(entry) <= 12f
                        || !map.reachability.CanReach(entry, candidate, PathEndMode.Touch,
                            TraverseParms.For(TraverseMode.PassDoors)))
                    {
                        continue;
                    }
                    bool againstWall = false;
                    foreach (IntVec3 adjacent in GenAdjFast.AdjacentCellsCardinal(candidate))
                    {
                        if (adjacent.InBounds(map) && adjacent.GetEdifice(map) != null)
                        {
                            againstWall = true;
                            break;
                        }
                    }
                    if (againstWall && attempt < 40)
                    {
                        cell = candidate;
                    }
                    else if (!fallback.IsValid)
                    {
                        fallback = candidate;
                    }
                }
                if (!cell.IsValid)
                {
                    cell = fallback;
                }
                if (cell.IsValid)
                {
                    Thing spot = GenSpawn.Spawn(spotDef, cell, map);
                    // Loot chests are empty shells filled by whoever places
                    // them (the well caves pour their own table in). Ours get
                    // the common cache - an empty reward chest is a taunt.
                    if (spot is Building_Casket casket && casket.GetDirectlyHeldThings().Count == 0
                        && spotDef.defName == "TSC_LootChest")
                    {
                        ThingSetMakerDef lootTable = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail("TSC_Loot_CommonCache");
                        if (lootTable != null)
                        {
                            foreach (Thing item in lootTable.root.Generate())
                            {
                                casket.GetDirectlyHeldThings().TryAdd(item);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Stock the floor. With `occupants` set, one weighted profile is
        /// rolled per level - so the same contract template produces a
        /// bandit crew on one floor and a nest on the next. `vermin` is the
        /// older single-kind path (the hand-authored cellars use it).
        /// </summary>
        /// <summary>Cells within this of the reliquary stay empty: the chorister's ground.</summary>
        public const float ReliquaryKeepClear = 14f;

        private void SpawnOccupants(Map map, CellRect rect, IntVec3 entry, IntVec3 keepClear)
        {
            TSC_DungeonOccupant profile = null;
            if (occupants.Count > 0)
            {
                profile = occupants.RandomElementByWeight(o => Mathf.Max(0.01f, o.weight));
            }
            else if (vermin != null)
            {
                profile = new TSC_DungeonOccupant
                {
                    faction = "insects",
                    kinds = new List<PawnKindDef> { vermin },
                    count = verminCount,
                };
            }
            if (profile == null || profile.kinds.Count == 0)
            {
                return;
            }
            Faction faction = FactionFor(profile.faction);
            int count = profile.count.RandomInRange;
            List<Pawn> spawned = new List<Pawn>();
            // One PACK, not a scatter: the same headcount strewn one-by-one
            // across the floor reads as enemies everywhere, and every corner
            // turned is another fight. Anchored together they are a single
            // encounter the party can find, read, and take on its own terms.
            IntVec3 anchor = IntVec3.Invalid;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                IntVec3 candidate = rect.RandomCell;
                if (candidate.Standable(map) && candidate.DistanceTo(entry) > 18f
                    && (!keepClear.IsValid || candidate.DistanceTo(keepClear) > ReliquaryKeepClear))
                {
                    anchor = candidate;
                    break;
                }
            }
            if (!anchor.IsValid)
            {
                return;
            }
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(anchor, map, 5);
                if (!cell.IsValid || (keepClear.IsValid && cell.DistanceTo(keepClear) <= ReliquaryKeepClear))
                {
                    continue;
                }
                PawnKindDef kind = profile.kinds.RandomElement();
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction,
                    PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
                GenSpawn.Spawn(pawn, cell, map);
                spawned.Add(pawn);
            }
            if (spawned.Count == 0)
            {
                return;
            }
            if (faction != null)
            {
                LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(spawned[0].Position), map, spawned);
            }
            if (!profile.arrivalNote.NullOrEmpty())
            {
                map.GetComponent<MapComponent_TSC_DelveArrival>()?.Announce(profile.arrivalNote);
            }
        }

        private static Faction FactionFor(string key)
        {
            switch (key)
            {
                case "bandits":
                    return TSC_BanditFactionUtility.Get();
                case "wild":
                    return null; // wild animals answer to nobody
                default:
                    return Faction.OfInsects;
            }
        }

        /// <summary>The prize, in a cleared chamber at the far end of the deepest floor.</summary>
        private void PlaceLoot(Map map, IntVec3 at)
        {
            foreach (IntVec3 cell in CellRect.CenteredOn(at, 7, 7).ClipInsideMap(map))
            {
                CarveCell(map, cell);
            }
            IntVec3 spot = CellFinder.StandableCellNear(at, map, 8f);
            if (!spot.IsValid)
            {
                spot = at;
            }
            Thing loot = ThingMaker.MakeThing(lootDef, GenStuff.DefaultStuffFor(lootDef));
            GenPlace.TryPlaceThing(loot, spot, map, ThingPlaceMode.Near);
        }

        // ---------------------------------------------------------------- helpers
        // (Deliberate twins of the crypt's: same job, same rules, and the
        // crypt's are private to a genstep with very different arguments.)

        internal static void CarveCell(Map map, IntVec3 cell)
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

        private static IntVec3 FarthestStandable(Map map, CellRect rect, IntVec3 from)
        {
            IntVec3 best = rect.CenterCell;
            float bestDist = -1f;
            foreach (IntVec3 cell in rect)
            {
                if (!cell.Standable(map))
                {
                    continue;
                }
                float dist = cell.DistanceToSquared(from);
                if (dist > bestDist)
                {
                    best = cell;
                    bestDist = dist;
                }
            }
            return best;
        }

        private static void EnsurePath(Map map, IntVec3 a, IntVec3 b)
        {
            if (!a.IsValid || !b.IsValid
                || map.reachability.CanReach(a, b, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors)))
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
                    CarveCell(map, cursor);
                    CarveCell(map, cursor + IntVec3.East);
                }
            }
        }
    }

    /// <summary>
    /// Holds the "what lives on this floor" line from generation until the
    /// party actually arrives, so the note lands as they step off the stair
    /// instead of into an empty map during worldgen.
    /// </summary>
    public class MapComponent_TSC_DelveArrival : MapComponent
    {
        private string note;
        private bool announced;

        public MapComponent_TSC_DelveArrival(Map map) : base(map)
        {
        }

        public void Announce(string text)
        {
            note = text;
        }

        public override void MapComponentTick()
        {
            if (announced || note.NullOrEmpty() || Find.TickManager.TicksGame % 60 != 0
                || map.mapPawns.FreeColonistsSpawnedCount == 0)
            {
                return;
            }
            announced = true;
            Messages.Message(note, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref note, "note");
            Scribe_Values.Look(ref announced, "announced");
        }
    }

    /// <summary>
    /// The reliquary chest keeps its promise: the quest text says the blade
    /// spawns Excellent, but the crate's LootSpawn rolls random quality.
    /// When THIS chest opens, the blade it just spawned is set right.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Building_Crate), nameof(Building_Crate.Open))]
    public static class Patch_ReliquaryChest_BladeQuality
    {
        public static void Postfix(Building_Crate __instance)
        {
            if (__instance.def?.defName != "TSC_IronboundChest_Kingsblade"
                || __instance.Map == null)
            {
                return;
            }
            ThingDef bladeDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_Weapon_Kingsblade");
            if (bladeDef != null)
            {
                foreach (Thing thing in __instance.Map.listerThings.ThingsOfDef(bladeDef))
                {
                    if (thing.Position.InHorDistOf(__instance.Position, 4f))
                    {
                        thing.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Outsider);
                    }
                }
            }
            // Cracking the blade's chest breaks Aldis's vigil like lifting
            // the shard does: nobody loots his reliquary unremarked.
            __instance.Map.GetComponent<MapComponent_TSC_Reliquary>()?.Notify_ChestOpened();
        }
    }

    /// <summary>
    /// The reliquary at the bottom: an altar holding the Kingsblade and the
    /// second shard, and the last chorister of the old kingdom standing over
    /// them. He is NOT hostile - he is a conversation (see cantor.agd), and
    /// the way past him is a choice: lay him to rest, or take it off him.
    /// </summary>
    public class MapComponent_TSC_Reliquary : MapComponent
    {
        private bool built;
        private IntVec3 altarPos = IntVec3.Invalid;
        private int nextSongTick = -1;
        // Aldis's vigil: the shard he watches and whether the theft scene
        // has already fired (once per floor, ever).
        private Thing shard;
        private bool vigilBroken;

        private const int SongIntervalTicks = 1800;

        public MapComponent_TSC_Reliquary(Map map) : base(map)
        {
        }

        public void Build(IntVec3 at)
        {
            if (built)
            {
                return;
            }
            built = true;
            // A clear chamber for the last room: no layout wall between the
            // party and the thing they came four maps for.
            foreach (IntVec3 cell in CellRect.CenteredOn(at, 9, 9).ClipInsideMap(map))
            {
                GenStep_TSC_CellarLevel.CarveCell(map, cell);
            }
            ClearHostiles(at);
            altarPos = at;
            ThingDef altarDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_ReliquaryAltar");
            if (altarDef != null)
            {
                GenSpawn.Spawn(altarDef, at, map);
            }
            SpawnTreasure(at);
            SpawnCantor(at);
        }

        /// <summary>
        /// Empty the chamber of anything that would eat the chorister.
        ///
        /// The layout's own dormant insect clusters are placed with the rooms,
        /// long before this chamber is carved, so some of them end up standing
        /// exactly where Aldis is about to. Insects are hostile to his faction
        /// as much as to the party's, so left alone they simply kill him -
        /// plot armor downs him instead, which loses the scene either way -
        /// and their brawl drags the player into turn-based combat from the
        /// far side of the map. The rest of the floor keeps its tenants; the
        /// fight starts when the party opens this room, not before.
        /// </summary>
        private void ClearHostiles(IntVec3 at)
        {
            List<Pawn> doomed = new List<Pawn>();
            // Snapshot: AllPawnsSpawned is a cached list and destroying from
            // inside it rebuilds it mid-enumeration.
            List<Pawn> present = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
            foreach (Pawn pawn in present)
            {
                if (pawn == null || pawn.Destroyed || pawn.Faction == Faction.OfPlayer
                    || !pawn.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }
                if (pawn.Position.InHorDistOf(at, GenStep_TSC_CellarLevel.ReliquaryKeepClear))
                {
                    doomed.Add(pawn);
                }
            }
            foreach (Pawn pawn in doomed)
            {
                pawn.Destroy(DestroyMode.Vanish);
            }
            // A hive left in the chamber would just refill it.
            List<Thing> hives = new List<Thing>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing?.def != null && thing.def.defName == "Hive"
                    && thing.Position.InHorDistOf(at, GenStep_TSC_CellarLevel.ReliquaryKeepClear))
                {
                    hives.Add(thing);
                }
            }
            foreach (Thing hive in hives)
            {
                hive.Destroy(DestroyMode.Vanish);
            }
        }

        private void SpawnTreasure(IntVec3 at)
        {
            // The blade rests IN the reliquary chest, as the chronicle
            // promised ("a blade laid to rest with honors"); the chest's
            // LootSpawn delivers it on opening, and the open-postfix below
            // guarantees the Excellent the quest text names. The shard lies
            // bare on the altar: it is the thing the vision points at, and
            // the glint should be the first thing the lamplight finds.
            ThingDef chestDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_IronboundChest_Kingsblade");
            IntVec3 chestSpot = CellFinder.StandableCellNear(at + new IntVec3(-1, 0, 1), map, 6f);
            if (chestDef != null && chestSpot.IsValid)
            {
                GenSpawn.Spawn(chestDef, chestSpot, map);
            }
            else
            {
                SpawnItem("TSC_Weapon_Kingsblade", at + new IntVec3(-1, 0, 1));
            }
            // The shard is not set dressing: Aldis KEEPS it ("I was told to
            // keep it"). It rides in his inventory, so the only roads to it
            // run through him - rest him, fight him, or mourn him - and no
            // dialogue can be stepped out of with the prize already pocketed.
            // If he cannot stand his post (dead before the floor built), the
            // shard falls back to the altar, loose.
        }

        private Thing SpawnItem(string defName, IntVec3 cell)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return null;
            }
            IntVec3 spot = CellFinder.StandableCellNear(cell, map, 6f);
            if (!spot.IsValid)
            {
                return null;
            }
            Thing thing = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            thing.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Outsider);
            GenPlace.TryPlaceThing(thing, spot, map, ThingPlaceMode.Near);
            return thing;
        }

        /// <summary>The chest-open postfix reports here: opening the blade's chest breaks the vigil too.</summary>
        public void Notify_ChestOpened()
        {
            BreakVigil();
        }

        /// <summary>
        /// Nobody skips Aldis. The reliquary is HIS: lifting the shard off
        /// the altar (or cracking the blade's chest) while he stands
        /// unresolved forces the conversation the party walked past - same
        /// contract as the crypt, where the shard leaving the stone wakes
        /// its keeper. He is still a conversation, not an ambush: the scene
        /// that opens is the one they were always meant to have.
        /// </summary>
        private void BreakVigil()
        {
            if (vigilBroken || !built)
            {
                return;
            }
            if (DialogueStateManager.Current.IsSet("TSC_CantorRested")
                || DialogueStateManager.Current.IsSet("TSC_CantorFought"))
            {
                vigilBroken = true; // already settled, nothing to defend
                return;
            }
            NamedNpcDef cantorDef = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Cantor");
            Pawn cantor = cantorDef != null ? DialogueStateManager.Current.GetNamedNpcIfExists(cantorDef) : null;
            if (cantor == null || cantor.Dead || !cantor.Spawned || cantor.Map != map)
            {
                vigilBroken = true; // no keeper left to object
                return;
            }
            vigilBroken = true;
            DialogueDef dialogue = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_Cantor");
            Pawn thief = null;
            float best = float.MaxValue;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                float dist = colonist.Position.DistanceTo(altarPos);
                if (dist < best)
                {
                    best = dist;
                    thief = colonist;
                }
            }
            if (dialogue != null && thief != null)
            {
                Messages.Message("The singing stops.", cantor, MessageTypeDefOf.NeutralEvent, historical: false);
                Find.WindowStack.Add(new Dialog_Conversation(dialogue, thief, cantor));
            }
        }

        private void SpawnCantor(IntVec3 at)
        {
            NamedNpcDef def = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Cantor");
            if (def == null)
            {
                return;
            }
            Pawn cantor = DialogueStateManager.Current.GetOrGenerateNamedNpc(def, GenStep_TSC_Village.VillagerFaction());
            if (cantor == null || cantor.Dead || cantor.Spawned)
            {
                return;
            }
            if (cantor.IsWorldPawn())
            {
                Find.WorldPawns.RemovePawn(cantor);
            }
            IntVec3 cell = CellFinder.StandableCellNear(at + new IntVec3(0, 0, -2), map, 6f);
            if (!cell.IsValid)
            {
                cell = at;
            }
            GenSpawn.Spawn(cantor, cell, map);
            if (cantor.Faction != null)
            {
                LordMaker.MakeNewLord(cantor.Faction, new LordJob_DefendPoint(cell), map, Gen.YieldSingle(cantor));
            }
            GiveShardToKeeper(cantor);
        }

        /// <summary>The shard goes into the keeper's coat, once, if neither of them already has it elsewhere.</summary>
        private void GiveShardToKeeper(Pawn cantor)
        {
            ThingDef shardDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard_Reliquary");
            if (shardDef == null || cantor.inventory?.innerContainer == null)
            {
                return;
            }
            if (cantor.inventory.innerContainer.Contains(shardDef)
                || map.listerThings.ThingsOfDef(shardDef).Count > 0)
            {
                return; // already placed (old floor, or regeneration)
            }
            cantor.inventory.innerContainer.TryAdd(ThingMaker.MakeThing(shardDef), false);
        }

        /// <summary>
        /// The keeper falls, the shard falls with him - onto the stone where
        /// everyone can see it, not buried in a corpse's pockets. Covers
        /// every road: laid to rest, killed, or downed by plot armor.
        /// </summary>
        private void DropShardFrom(Pawn cantor)
        {
            ThingDef shardDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard_Reliquary");
            if (shardDef == null)
            {
                return;
            }
            ThingOwner pockets = cantor.inventory?.innerContainer
                ?? cantor.Corpse?.InnerPawn?.inventory?.innerContainer;
            if (pockets == null)
            {
                return;
            }
            for (int i = pockets.Count - 1; i >= 0; i--)
            {
                if (pockets[i].def == shardDef)
                {
                    IntVec3 cell = cantor.Corpse?.Position ?? cantor.PositionHeld;
                    pockets.TryDrop(pockets[i], cell, map, ThingPlaceMode.Near, out Thing _);
                }
            }
        }

        /// <summary>
        /// He has been singing for eight hundred years and does not stop for
        /// visitors: a verse drifts up the stairs every half minute or so
        /// while he lives, so the room is heard before it is seen.
        /// </summary>
        public override void MapComponentTick()
        {
            if (!built || map.mapPawns.FreeColonistsSpawnedCount == 0)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (now % 60 == 0)
            {
                Pawn keeper = DialogueStateManager.Current.GetNamedNpcIfExists(
                    DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Cantor"));
                if (keeper != null)
                {
                    // The rested passing, made real: the scene SAYS the
                    // vestments settle, so the pawn must actually fall. The
                    // shard is set down first, clean on the stone.
                    if (!keeper.Dead && keeper.Spawned && keeper.Map == map
                        && DialogueStateManager.Current.IsSet("TSC_CantorRested"))
                    {
                        DropShardFrom(keeper);
                        keeper.Kill(null);
                    }
                    // Any other fall (killed, or downed under plot armor):
                    // the shard comes out of his coat onto the floor.
                    else if ((keeper.Dead || keeper.Downed) && (keeper.MapHeld == map || keeper.Corpse?.Map == map))
                    {
                        DropShardFrom(keeper);
                    }
                }
            }
            // The vigil watch: a lifted shard is a lifted shard, however the
            // order was given (right-click, caravan gather, exit manifest).
            // Floors generated before the vigil existed have no stored ref:
            // adopt the shard where it lies, so older saves get the scene too.
            if (!vigilBroken && shard == null && now % 60 == 0)
            {
                ThingDef shardDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard_Reliquary");
                if (shardDef != null)
                {
                    List<Thing> lying = map.listerThings.ThingsOfDef(shardDef);
                    if (lying.Count > 0)
                    {
                        shard = lying[0];
                    }
                }
            }
            if (!vigilBroken && shard != null && now % 60 == 0
                && (!shard.Spawned || shard.Destroyed))
            {
                BreakVigil();
            }
            if (nextSongTick < 0)
            {
                nextSongTick = now + SongIntervalTicks;
                return;
            }
            if (now < nextSongTick)
            {
                return;
            }
            nextSongTick = now + SongIntervalTicks;
            Pawn cantor = DialogueStateManager.Current.GetNamedNpcIfExists(
                DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Cantor"));
            if (cantor == null || cantor.Dead || !cantor.Spawned || cantor.Map != map)
            {
                return;
            }
            MoteMaker.ThrowText(cantor.DrawPos, map, SongLines.RandomElement(), new Color(0.85f, 0.8f, 0.55f));
        }

        private static readonly List<string> SongLines = new List<string>
        {
            "...five roads...",
            "...and the crown went down...",
            "...who carried it...",
            "...the water remembers...",
        };

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref built, "built");
            Scribe_Values.Look(ref altarPos, "altarPos");
            Scribe_Values.Look(ref nextSongTick, "nextSongTick", -1);
            Scribe_References.Look(ref shard, "shard");
            Scribe_Values.Look(ref vigilBroken, "vigilBroken");
        }
    }
}
