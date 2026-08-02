using RimWorld;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// Puts the cellar door in the border ruin, under a roof.
    ///
    /// It has to be INSIDE the standing structure, because the whole scene
    /// turns on the door having been barred by a beam off the north gable -
    /// a door in an open field has nothing to fall on it. So: search for a
    /// roofed, enclosed cell first, and only fall back to open ground if the
    /// layout produced a ruin with no roof left at all.
    /// </summary>
    public class GenStep_TSC_BranDoor : GenStep
    {
        public ThingDef doorDef;

        public override int SeedPart => 918273645;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (doorDef == null)
            {
                return;
            }
            IntVec3 spot = FindSpot(map, roofedOnly: true);
            if (!spot.IsValid)
            {
                spot = FindSpot(map, roofedOnly: false);
            }
            if (!spot.IsValid)
            {
                Log.Warning("[The Shattered Crown] No room for the cellar door in the border ruin.");
                return;
            }
            GenSpawn.Spawn(ThingMaker.MakeThing(doorDef), spot, map);
        }

        /// <summary>
        /// A cell whose whole footprint is clear, standable and (optionally)
        /// under a roof, biased toward the north end of the map because that
        /// is where Bran says the cellar is and somebody will check.
        /// </summary>
        private IntVec3 FindSpot(Map map, bool roofedOnly)
        {
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MinValue;
            foreach (IntVec3 cell in map.AllCells)
            {
                if (cell.DistanceToEdge(map) < 12)
                {
                    continue;
                }
                if (roofedOnly && !cell.Roofed(map))
                {
                    continue;
                }
                bool clear = true;
                foreach (IntVec3 part in GenAdj.OccupiedRect(cell, Rot4.North, doorDef.size))
                {
                    if (!part.InBounds(map) || !part.Standable(map) || part.GetEdifice(map) != null)
                    {
                        clear = false;
                        break;
                    }
                }
                if (!clear)
                {
                    continue;
                }
                // The whole scene is a proximity dialogue: a pile the party
                // cannot walk up to is a quest that never fires. Ruin doors
                // count as passable for the check; a sealed-solid room does
                // not.
                if (!map.reachability.CanReachMapEdge(cell,
                        TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly)))
                {
                    continue;
                }
                float score = cell.z;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell;
                }
            }
            return best;
        }
    }
}
