using HarmonyLib;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Every generated map ends generation with a VALID player start spot.
    ///
    /// MapGenerator.playerStartSpotInt resets to Invalid at the start of
    /// each generation and only becomes valid if some genstep sets it.
    /// Standard maps have such a genstep. The mod's pocket-map generators
    /// set the spot themselves these days (GenStep_TSC_CellarLevel's
    /// EnsureWayUp, and the well caves / barrow / crypt / cellar content
    /// gensteps), all before vanilla GenStep_Fog reads it at order 1230 -
    /// so this fallback is belt-and-braces for the degenerate rolls where
    /// even those fail (EnsureWayUp finding no way up twice). An unset spot
    /// makes the first reader log "Accessing player start spot before
    /// setting it": vanilla's Fog genstep during generation, or Real Fog of
    /// War's seen-fog component on the map's first tick.
    ///
    /// The fallback is only an anchor for readers like that - by the time
    /// it matters, arrival positioning has already been decided by the
    /// portal or our stair placement. Maps whose generation DID set a spot
    /// are untouched.
    /// </summary>
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
    public static class Patch_MapGenerator_StartSpotFallback
    {
        public static void Postfix(Map __result)
        {
            if (__result == null || MapGenerator.PlayerStartSpotValid)
            {
                return;
            }
            IntVec3 spot = __result.Center;
            if (!spot.Standable(__result))
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(__result.Center, 40f, useCenter: false))
                {
                    if (cell.InBounds(__result) && cell.Standable(__result))
                    {
                        spot = cell;
                        break;
                    }
                }
            }
            MapGenerator.PlayerStartSpot = spot;
        }
    }
}
