using HarmonyLib;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Every generated map ends generation with a VALID player start spot.
    ///
    /// MapGenerator.playerStartSpotInt resets to Invalid at the start of
    /// each generation and only becomes valid if some genstep sets it.
    /// Standard maps have such a genstep; pocket maps made through
    /// MapPortal.GeneratePocketMap (the keep cellar) do not - so the static
    /// stays Invalid, and the first thing to read PlayerStartSpot after
    /// generation logs "Accessing player start spot before setting it."
    /// In this load order that reader is Real Fog of War's seen-fog
    /// component, which inits lazily on the map's first tick and anchors
    /// its initial reveal on the spot.
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
