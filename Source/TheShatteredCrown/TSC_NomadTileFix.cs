using System;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Vanilla's "where is the player" fallback assumes a settled home map:
    /// TryFindRandomPlayerTile only accepts maps that are IsPlayerHome with
    /// colonists on them. A nomadic party - the normal state of this mod's
    /// scenarios - lives on SITE maps and pocket dungeon levels, so the
    /// fallback finds nothing, the tile finder roots at PlanetTile -1, and
    /// the storyteller logs "Attempted to access a tile with ID -1" every
    /// time it so much as CONSIDERS a vanilla quest (the eligibility test
    /// runs the tile search).
    ///
    /// When vanilla comes up empty in an RPG-mode game, answer with where
    /// the party actually is: any map holding a free colonist (rooted
    /// through the pocket chain, since pocket maps have no world tile), then
    /// any player caravan. Vanilla results are never overridden - this only
    /// fills the hole - and the caller's validator and layer constraints are
    /// honored so a substituted tile is never one vanilla would reject.
    /// </summary>
    [HarmonyPatch(typeof(TileFinder), nameof(TileFinder.TryFindRandomPlayerTile))]
    public static class Patch_TryFindRandomPlayerTile_Nomads
    {
        public static void Postfix(ref PlanetTile tile, Predicate<PlanetTile> validator,
            PlanetLayer layer, ref bool __result)
        {
            if (__result || !TSC_RpgMode.Active)
            {
                return;
            }
            foreach (Map map in Find.Maps)
            {
                if (map.mapPawns.FreeColonistsSpawnedCount == 0)
                {
                    continue;
                }
                if (Accept(TSC_Threat.RootMap(map)?.Tile ?? PlanetTile.Invalid, validator, layer, ref tile))
                {
                    __result = true;
                    return;
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.IsPlayerControlled
                    && Accept(caravan.Tile, validator, layer, ref tile))
                {
                    __result = true;
                    return;
                }
            }
        }

        private static bool Accept(PlanetTile candidate, Predicate<PlanetTile> validator,
            PlanetLayer layer, ref PlanetTile tile)
        {
            if (!candidate.Valid)
            {
                return false;
            }
            if (layer != null && candidate.Layer != layer)
            {
                return false;
            }
            if (validator != null && !validator(candidate))
            {
                return false;
            }
            tile = candidate;
            return true;
        }
    }
}
