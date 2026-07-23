using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Maewyn's cellar hatch: a real MapPortal into a one-room underground
    /// pocket map (vanilla ancient-hatch machinery, medieval lock). Locked
    /// until the [Thievery] pick succeeds; unlocking while Maewyn is in the
    /// party is the theft moment (callout + affinity, handled by the pick job).
    /// </summary>
    public class Building_TSC_CellarHatch : MapPortal
    {
        private bool unlocked;
        // ONE attempt, ever: same philosophy as dialogue checks. The player
        // chooses who rolls; send the thief, and live with the result.
        private bool attemptSpent;

        public bool Unlocked => unlocked;

        public bool AttemptSpent => attemptSpent;

        public void Unlock()
        {
            unlocked = true;
        }

        public void NoteFailed()
        {
            attemptSpent = true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref unlocked, "unlocked", defaultValue: false);
            Scribe_Values.Look(ref attemptSpent, "attemptSpent", defaultValue: false);
        }

        public override bool IsEnterable(out string reason)
        {
            if (!unlocked)
            {
                reason = "the druid-made lock holds fast";
                return false;
            }
            return base.IsEnterable(out reason);
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            string state = unlocked ? "Unlocked." : "Locked: a druid-made lock. [Thievery] to pick it.";
            return text.NullOrEmpty() ? state : $"{text}\n{state}";
        }

        public override Map GetOtherMap()
        {
            Map other = base.GetOtherMap();
            EnsureExit(other);
            return other;
        }

        /// <summary>
        /// The portal system does NOT spawn exitDef itself (that's the
        /// generating genstep's job in vanilla). The genstep spawns ours; this
        /// self-heal also fixes cellars generated before that fix ("destination
        /// is blocked" - no arrival anchor).
        /// </summary>
        private void EnsureExit(Map other)
        {
            ThingDef exitDef = def.portal?.exitDef;
            if (other == null || exitDef == null || other.listerThings.ThingsOfDef(exitDef).Count > 0)
            {
                return;
            }
            IntVec3 cell = other.Center + new IntVec3(0, 0, 2);
            if (!cell.InBounds(other) || !cell.Standable(other))
            {
                cell = CellFinder.RandomClosewalkCellNear(other.Center, other, 4);
            }
            GenSpawn.Spawn(ThingMaker.MakeThing(exitDef), cell, other);
        }
    }

    /// <summary>Carves the cellar room out of the solid-rock pocket map and stocks it: shelves, torch, and Maewyn's cache (druid book among it).</summary>
    public class GenStep_TSC_Cellar : GenStep
    {
        public override int SeedPart => 712304571;

        public override void Generate(Map map, GenStepParams parms)
        {
            // The Fog genstep (and arriving pawns) anchor on the player start
            // spot; vanilla pocket-map gensteps set it, ours must too.
            MapGenerator.PlayerStartSpot = map.Center;
            CellRect room = CellRect.CenteredOn(map.Center, 11, 9);
            TerrainDef floor = TerrainDef.Named("FlagstoneGranite");
            foreach (IntVec3 cell in room)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    if (things[i].def.category == ThingCategory.Building || things[i].def.category == ThingCategory.Item)
                    {
                        things[i].Destroy();
                    }
                }
                map.terrainGrid.SetTerrain(cell, floor);
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThin);
            }
            // The way back up: the exit ladder is OUR job to place (the portal
            // system reads exitDef but does not spawn it).
            TrySpawn(DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CellarExit"), map, map.Center + new IntVec3(0, 0, 2), null);
            // Furnishings: a shelf against the north wall, a torch for light,
            // and a row of barrels along the south wall (a cellar is a cellar).
            Thing shelf = TrySpawn(ThingDef.Named("Shelf"), map, map.Center + new IntVec3(-2, 0, 3), ThingDef.Named("WoodLog"));
            TrySpawn(ThingDefOf.TorchLamp, map, map.Center + new IntVec3(3, 0, 2), null);
            ThingDef barrel = ThingDef.Named("FermentingBarrel");
            TrySpawn(barrel, map, map.Center + new IntVec3(-4, 0, -3), null);
            TrySpawn(barrel, map, map.Center + new IntVec3(-3, 0, -3), null);
            TrySpawn(barrel, map, map.Center + new IntVec3(4, 0, -3), null);
            if (Rand.Chance(0.6f))
            {
                TrySpawn(barrel, map, map.Center + new IntVec3(-4, 0, 2), null);
            }
            // The cache lives ON the shelf (thirty careful years, not a heap
            // on the floor); overflow falls to the floor beneath it.
            ThingSetMakerDef lootDef = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail("TSC_Loot_MaewynCellar");
            if (lootDef != null)
            {
                List<IntVec3> shelfCells = new List<IntVec3>();
                if (shelf != null)
                {
                    foreach (IntVec3 cell in shelf.OccupiedRect())
                    {
                        shelfCells.Add(cell);
                    }
                }
                List<Thing> loot = lootDef.root.Generate();
                for (int i = 0; i < loot.Count; i++)
                {
                    IntVec3 cell = shelfCells.Count > 0 && i < shelfCells.Count * 3
                        ? shelfCells[i % shelfCells.Count]
                        : CellFinder.RandomClosewalkCellNear(map.Center + new IntVec3(0, 0, -2), map, 3);
                    GenSpawn.Spawn(loot[i], cell, map);
                }
            }
        }

        private static Thing TrySpawn(ThingDef def, Map map, IntVec3 cell, ThingDef stuff)
        {
            if (def == null || !cell.InBounds(map))
            {
                return null;
            }
            return GenSpawn.Spawn(ThingMaker.MakeThing(def, stuff), cell, map);
        }
    }
}
