using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The mosskeepers' barrow mouth: a cave entrance (vanilla insect-lair
    /// portal machinery) on the surface site, leading down into the
    /// TSC_BarrowUnderground pocket map where the crypt itself is carved
    /// into the rock.
    /// </summary>
    public class Building_TSC_BarrowEntrance : MapPortal
    {
        public override Map GetOtherMap()
        {
            Map other = base.GetOtherMap();
            EnsureExit(other);
            return other;
        }

        /// <summary>
        /// Same self-heal as the cellar hatch: the portal system reads exitDef
        /// but never spawns it - if the genstep's exit is missing for any
        /// reason, place one so arrival is never "destination is blocked".
        /// </summary>
        private void EnsureExit(Map other)
        {
            ThingDef exitDef = def.portal?.exitDef;
            if (other == null || exitDef == null || other.listerThings.ThingsOfDef(exitDef).Count > 0)
            {
                return;
            }
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(other.Center, other, 6);
            GenSpawn.Spawn(ThingMaker.MakeThing(exitDef), cell, other);
        }
    }

    /// <summary>
    /// Surface half of the barrow site: clears a patch near the map center
    /// and spawns the barrow mouth. The crypt itself is generated in the
    /// pocket map behind the portal (GenStep_TSC_BarrowCrypt).
    /// </summary>
    public class GenStep_TSC_BarrowSurface : GenStep
    {
        public ThingDef entranceDef;

        public override int SeedPart => 665128374;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (entranceDef == null)
            {
                return;
            }
            IntVec3 spot = FindSpot(map, entranceDef.size);
            CellRect rect = GenAdj.OccupiedRect(spot, Rot4.North, entranceDef.size);
            foreach (IntVec3 cell in rect.ExpandedBy(1))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    if (things[i].def.destroyable && (things[i].def.category == ThingCategory.Building || things[i].def.category == ThingCategory.Plant))
                    {
                        things[i].Destroy();
                    }
                }
            }
            GenSpawn.Spawn(ThingMaker.MakeThing(entranceDef), spot, map);
        }

        private static IntVec3 FindSpot(Map map, IntVec2 size)
        {
            int maxRadius = map.Size.x / 2 - size.x - 2;
            for (int radius = 0; radius < maxRadius; radius++)
            {
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, radius, useCenter: radius == 0))
                {
                    if (SpotOk(map, candidate, size))
                    {
                        return candidate;
                    }
                }
            }
            return map.Center;
        }

        private static bool SpotOk(Map map, IntVec3 spot, IntVec2 size)
        {
            foreach (IntVec3 cell in GenAdj.OccupiedRect(spot, Rot4.North, size))
            {
                if (!cell.InBounds(map) || cell.Fogged(map)
                    || !cell.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy)
                    || cell.GetEdifice(map) != null)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Underground half: carves a chamber out of the solid-rock cavern map,
    /// spawns the crypt layout inside it (so the barrow really is INSIDE the
    /// hill), roofs it with thin rock, and bores a robbers' tunnel from the
    /// rope exit through to the crypt.
    /// </summary>
    public class GenStep_TSC_BarrowCrypt : GenStep
    {
        public LayoutDef layoutDef;
        public int size = 33;

        public override int SeedPart => 447719283;

        public override void Generate(Map map, GenStepParams parms)
        {
            CellRect rect = CellRect.CenteredOn(map.Center, size, size).ClipInsideMap(map).ContractedBy(1);
            // Clear the chamber the crypt will occupy: natural rock out, thin
            // rock roof in (no collapse; the cellar taught us this dance).
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
                // The barrow has ONE kind of tenant, difficulty-scaled:
                // shamblers when Anomaly is present (GenStep_TSC_BarrowUndead,
                // count x threatScale), dormant insects only as the
                // Anomaly-less fallback. Both at once buried a playtest -
                // so the layout is only armed with threat points (colony
                // wealth x threatScale; the pocket map has no site part
                // params of its own) when the shamblers will NOT rise.
                float? threatPoints = ModsConfig.AnomalyActive
                    ? (float?)null
                    : Mathf.Clamp(StorytellerUtility.DefaultThreatPointsNow(Find.World), 150f, 800f);
                worker.Spawn(sketch, map, rect.Min, threatPoints: threatPoints);
            }
            // The layout worker manages floors/walls; the rock overhead is ours.
            foreach (IntVec3 cell in rect)
            {
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
            }
            // The harvest grows loose on the crypt floor (no chest, user
            // decision): moss, sometimes medicine, and the mosskeepers'
            // manual, scattered where the dead lie. Roofed, so nothing rots.
            ThingSetMakerDef lootDef = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail("TSC_Loot_MossCache");
            if (lootDef != null)
            {
                foreach (Thing thing in lootDef.root.Generate())
                {
                    IntVec3 cell = CellFinder.RandomClosewalkCellNear(rect.CenterCell, map, rect.Width / 2 - 2,
                        c => c.Standable(map) && rect.Contains(c) && c.GetFirstItem(map) == null);
                    GenSpawn.Spawn(thing, cell, map);
                }
            }
            ConnectToExit(map, rect);
        }

        /// <summary>
        /// Bore a 3-wide tunnel from the cave exit (spawned by the vanilla
        /// PlaceCaveExit genstep, order 400) to the crypt, breaching its outer
        /// wall - the way the moss-farmers came and went, or the way robbers
        /// got in. If the exit is somehow missing, spawn one beside the crypt
        /// (the portal's EnsureExit would also catch this).
        /// </summary>
        private static void ConnectToExit(Map map, CellRect cryptRect)
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
                IntVec3 spawnCell = new IntVec3(cryptRect.minX - 4, 0, cryptRect.CenterCell.z);
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
            CellRect stopZone = cryptRect.ContractedBy(3);
            foreach (IntVec3 point in GenSight.PointsOnLineOfSight(from, cryptRect.CenterCell))
            {
                if (stopZone.Contains(point))
                {
                    break;
                }
                foreach (IntVec3 cell in CellRect.CenteredOn(point, 3, 3))
                {
                    // Inside the crypt rect only breach the wall line; the
                    // rooms beyond are the layout's business.
                    if (cryptRect.Contains(cell) && cryptRect.ContractedBy(2).Contains(cell))
                    {
                        continue;
                    }
                    CarveCell(map, cell);
                }
            }
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
}
