using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// LayoutWorker_AncientRuins salts its layouts with ancient BLAST DOORS -
    /// correct for a glitterworld ruin, absurd in a granite keep held by
    /// bandits with spears. The worker is otherwise the one that builds the
    /// best rooms, and the only concrete alternative (LayoutWorker_SimpleRuin)
    /// generates a plainer layout, so the worker stays and the blast doors go.
    ///
    /// Scoped to our own layouts by def, so vanilla's ancient complexes keep
    /// theirs.
    /// </summary>
    [HarmonyPatch(typeof(LayoutWorker_AncientRuins), "SpawnRandomBlastDoors", MethodType.Getter)]
    public static class Patch_Layout_NoBlastDoors
    {
        public static bool Prepare()
        {
            return AccessTools.PropertyGetter(typeof(LayoutWorker_AncientRuins), "SpawnRandomBlastDoors") != null;
        }

        public static void Postfix(LayoutWorker_AncientRuins __instance, ref bool __result)
        {
            if (__result && __instance?.Def != null && __instance.Def.defName.StartsWith("TSC_"))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// The curtain wall: what turns a building into a castle.
    ///
    /// The keep used to generate through vanilla's GenStep_AncientRuins,
    /// which scatters several disconnected ruin regions across the map. That
    /// is the right look for a collapsed complex and the wrong one for a seat
    /// somebody is currently holding. The keep now places a single coherent
    /// structure, and this rings it.
    ///
    /// The wall is TWO courses thick: a solid inner course that actually
    /// stops people, and a crenellated outer face that does not. A single
    /// line of wall reads as somebody's base; merlons and corner towers read
    /// as a castle from the first glance at the minimap, which is the whole
    /// point of the exercise.
    ///
    /// Worn, not wrecked. One or two stretches have fallen and were never
    /// rebuilt, with rubble at their feet - but the yard inside is swept,
    /// because the difference between a ruin and a garrison is whether
    /// anybody has picked the rocks up.
    /// </summary>
    public class GenStep_TSC_Curtain : GenStep
    {
        /// <summary>Open ground left between the keep and its wall: the courtyard.</summary>
        public int margin = 8;
        /// <summary>Gate opening, in cells, in the middle of the south face.</summary>
        public int gateWidth = 3;
        /// <summary>How many stretches of wall have fallen and never been rebuilt.</summary>
        public IntRange breaches = new IntRange(1, 2);
        public IntRange breachLength = new IntRange(2, 3);
        /// <summary>Corner towers, this many cells on a side.</summary>
        public int towerSize = 5;
        /// <summary>Masonry of the wall itself. Granite for a castle, logs for a palisade.</summary>
        public ThingDef wallStuff;
        /// <summary>The gate leaves. Steel reads as the iron gate the keep is known for.</summary>
        public ThingDef gateStuff;
        /// <summary>Battlements on the outer face. A timber palisade has none.</summary>
        public bool crenellated = true;
        /// <summary>Corner towers and flanking gate towers: castle furniture, not camp furniture.</summary>
        public bool towers = true;

        public override int SeedPart => 174495382;

        public override void Generate(Map map, GenStepParams parms)
        {
            CellRect keep = KeepBounds(map);
            if (keep.Width <= 0)
            {
                return; // nothing was built to wall in
            }
            CellRect outer = keep.ExpandedBy(margin).ClipInsideMap(map);
            if (outer.Width < 16 || outer.Height < 16)
            {
                return;
            }
            CellRect inner = outer.ContractedBy(1);
            ThingDef stuff = wallStuff ?? ThingDefOf.BlocksGranite;
            List<IntVec3> gate = GateCells(inner);
            HashSet<IntVec3> gateway = GateSpan(inner, outer);
            // Sweep BEFORE the breaches are made, or the sweep takes their
            // rubble with it and the collapses read as tidy doorways.
            Courtyard(map, inner, keep);
            HashSet<IntVec3> fallen = Breaches(map, inner, gate);

            // The barrier itself.
            foreach (IntVec3 cell in inner.EdgeCells)
            {
                if (cell.InBounds(map) && !fallen.Contains(cell) && !gate.Contains(cell))
                {
                    Raise(map, cell, stuff);
                }
            }
            // The face: merlons, with gaps where the wall behind them fell.
            foreach (IntVec3 cell in crenellated ? outer.EdgeCells : Enumerable.Empty<IntVec3>())
            {
                if (!cell.InBounds(map) || gateway.Contains(cell) || (cell.x + cell.z) % 2 != 0)
                {
                    continue;
                }
                if (fallen.Contains(new IntVec3(Mathf.Clamp(cell.x, inner.minX, inner.maxX), 0,
                        Mathf.Clamp(cell.z, inner.minZ, inner.maxZ))))
                {
                    continue; // no battlement standing over a collapse
                }
                Raise(map, cell, stuff);
            }
            if (towers)
            {
                Towers(map, outer, stuff);
            }
            Gatehouse(map, inner, outer, gate, stuff);
            Torches(map, inner, keep);
        }

        /// <summary>
        /// The footprint of what the layout built, measured from its walls.
        /// Doors are not counted: exterior doors sit on the outside face and
        /// would push the ring out a cell for nothing.
        /// </summary>
        private static CellRect KeepBounds(Map map)
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
                minX = Mathf.Min(minX, pos.x);
                maxX = Mathf.Max(maxX, pos.x);
                minZ = Mathf.Min(minZ, pos.z);
                maxZ = Mathf.Max(maxZ, pos.z);
            }
            return CellRect.FromLimits(minX, minZ, maxX, maxZ);
        }

        /// <summary>
        /// The gate: one row of leaves in the structural course, in the middle
        /// of the south face. Hanging a second row in the outer course as well
        /// gave a six-door airlock, and on a palisade - where the outer course
        /// is only battlements, and a palisade has none - it left three doors
        /// standing in open ground a cell clear of the wall.
        /// </summary>
        private List<IntVec3> GateCells(CellRect inner)
        {
            List<IntVec3> cells = new List<IntVec3>();
            int startX = inner.minX + inner.Width / 2 - gateWidth / 2;
            for (int i = 0; i < gateWidth; i++)
            {
                cells.Add(new IntVec3(startX + i, 0, inner.minZ));
            }
            return cells;
        }

        /// <summary>The gateway: the gate row plus the mouth in front of it, which stays open.</summary>
        private HashSet<IntVec3> GateSpan(CellRect inner, CellRect outer)
        {
            HashSet<IntVec3> span = new HashSet<IntVec3>(GateCells(inner));
            int startX = inner.minX + inner.Width / 2 - gateWidth / 2;
            for (int i = 0; i < gateWidth; i++)
            {
                span.Add(new IntVec3(startX + i, 0, outer.minZ));
            }
            return span;
        }

        /// <summary>
        /// Fallen stretches. Kept off the gate face so the way in stays
        /// legible, and off the corners so the towers still read as towers.
        /// </summary>
        private HashSet<IntVec3> Breaches(Map map, CellRect inner, List<IntVec3> gate)
        {
            HashSet<IntVec3> fallen = new HashSet<IntVec3>();
            List<IntVec3> candidates = new List<IntVec3>();
            foreach (IntVec3 cell in inner.EdgeCells)
            {
                bool nearCornerX = cell.x - inner.minX < towerSize + 2 || inner.maxX - cell.x < towerSize + 2;
                bool nearCornerZ = cell.z - inner.minZ < towerSize + 2 || inner.maxZ - cell.z < towerSize + 2;
                if (!(nearCornerX && nearCornerZ) && cell.z != inner.minZ)
                {
                    candidates.Add(cell);
                }
            }
            if (candidates.Count == 0)
            {
                return fallen;
            }
            int count = breaches.RandomInRange;
            for (int i = 0; i < count; i++)
            {
                IntVec3 start = candidates.RandomElement();
                bool horizontal = start.z == inner.minZ || start.z == inner.maxZ;
                int run = breachLength.RandomInRange;
                for (int step = 0; step < run; step++)
                {
                    IntVec3 cell = horizontal
                        ? new IntVec3(start.x + step, 0, start.z)
                        : new IntVec3(start.x, 0, start.z + step);
                    if (gate.Contains(cell) || !inner.Contains(cell) || !inner.IsOnEdge(cell))
                    {
                        continue;
                    }
                    fallen.Add(cell);
                    if (cell.InBounds(map))
                    {
                        FilthMaker.TryMakeFilth(cell, map, ThingDefOf.Filth_RubbleBuilding);
                    }
                }
            }
            return fallen;
        }

        /// <summary>
        /// A swept flagstone yard. The chunk-strewn ground is most of what
        /// makes a stone building read as abandoned, so everything loose goes
        /// - rocks, rubble, scrub - and the Brand's yard looks walked on.
        /// </summary>
        private static void Courtyard(Map map, CellRect wall, CellRect keep)
        {
            TerrainDef yard = DefDatabase<TerrainDef>.GetNamedSilentFail("FlagstoneGranite")
                ?? TerrainDefOf.PackedDirt;
            foreach (IntVec3 cell in wall)
            {
                if (!cell.InBounds(map) || keep.Contains(cell))
                {
                    continue;
                }
                Sweep(map, cell);
                if (cell.GetEdifice(map) == null && !cell.GetTerrain(map).IsWater)
                {
                    map.terrainGrid.SetTerrain(cell, yard);
                }
            }
        }

        /// <summary>Corner towers: the silhouette that says castle at a glance.</summary>
        private void Towers(Map map, CellRect outer, ThingDef stuff)
        {
            IntVec3[] corners =
            {
                new IntVec3(outer.minX, 0, outer.minZ),
                new IntVec3(outer.maxX - towerSize + 1, 0, outer.minZ),
                new IntVec3(outer.minX, 0, outer.maxZ - towerSize + 1),
                new IntVec3(outer.maxX - towerSize + 1, 0, outer.maxZ - towerSize + 1),
            };
            foreach (IntVec3 corner in corners)
            {
                Tower(map, new CellRect(corner.x, corner.z, towerSize, towerSize), stuff);
            }
        }

        private static void Tower(Map map, CellRect tower, ThingDef stuff)
        {
            foreach (IntVec3 cell in tower)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                Sweep(map, cell);
                if (tower.IsOnEdge(cell))
                {
                    Raise(map, cell, stuff);
                }
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            }
        }

        /// <summary>
        /// "The walls are patched but the gate is iron." A roofed passage
        /// through both courses, with a squat tower shouldering it on each
        /// side - the one part of the wall the Brand has certainly maintained.
        /// </summary>
        private void Gatehouse(Map map, CellRect inner, CellRect outer, List<IntVec3> gate, ThingDef stuff)
        {
            int startX = inner.minX + inner.Width / 2 - gateWidth / 2;
            if (towers)
            {
                foreach (int side in new[] { startX - 3, startX + gateWidth })
                {
                    Tower(map, new CellRect(side, outer.minZ, 3, 3), stuff);
                }
            }
            foreach (IntVec3 cell in gate)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                cell.GetEdifice(map)?.Destroy(DestroyMode.Vanish);
                Sweep(map, cell);
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Door, gateStuff ?? ThingDefOf.Steel), cell, map);
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            }
        }

        /// <summary>
        /// Torches inside the wall. Small thing, big difference: a lit yard
        /// reads as a garrison that goes out at night, an unlit one as
        /// somewhere nobody has been in fifty years.
        /// </summary>
        private static void Torches(Map map, CellRect wall, CellRect keep)
        {
            ThingDef torch = DefDatabase<ThingDef>.GetNamedSilentFail("TorchLamp");
            if (torch == null)
            {
                return;
            }
            foreach (IntVec3 cell in wall.ContractedBy(2).EdgeCells)
            {
                if ((cell.x + cell.z) % 8 != 0 || !cell.InBounds(map) || keep.Contains(cell))
                {
                    continue;
                }
                if (cell.GetEdifice(map) != null || !cell.Standable(map))
                {
                    continue;
                }
                // Torches are not madeFromStuff: passing WoodLog logs an error.
                GenSpawn.Spawn(ThingMaker.MakeThing(torch), cell, map);
            }
        }

        private static void Raise(Map map, IntVec3 cell, ThingDef stuff)
        {
            if (cell.GetEdifice(map) != null)
            {
                return; // the keep's own masonry wins any overlap
            }
            Sweep(map, cell);
            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, stuff), cell, map);
        }

        /// <summary>Clear a cell of everything loose: chunks, filth, scrub, outcrops.</summary>
        private static void Sweep(Map map, IntVec3 cell)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing thing = things[i];
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
}
