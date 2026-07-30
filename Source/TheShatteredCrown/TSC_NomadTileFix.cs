using System;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The backstop for the same problem, one layer down.
    ///
    /// Patch_TryFindRandomPlayerTile_Nomads below answers "where is the
    /// player" when vanilla cannot, which covers the callers that ASK. Some
    /// do not: QuestNode_Root_Loot_AncientComplex works out its own root tile
    /// and hands it straight to the site finder, so when a nomadic party
    /// leaves it holding PlanetTile -1 the search runs anyway and the planet
    /// layer logs "Attempted to access a tile with ID -1" - once per quest,
    /// every time the storyteller so much as CONSIDERS one, because the
    /// eligibility test runs the search.
    ///
    /// Searching outward from a tile that does not exist has no meaningful
    /// answer, so this says so: no tile found, cleanly, before the query is
    /// built. The caller's own "could not find a site" path then runs exactly
    /// as it would have. Deliberately NOT gated on RPG mode - an invalid root
    /// is bad input from anybody.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_TryFindNewSiteTile_NoRoot
    {
        /// <summary>Shared with the water rejection below: same overload, two concerns.</summary>
        internal static MethodBase TargetForPatches()
        {
            return Target();
        }

        /// <summary>The overload that takes a root tile; the other one derives its own.</summary>
        private static MethodBase Target()
        {
            foreach (MethodInfo method in typeof(TileFinder).GetMethods(AccessTools.all))
            {
                if (method.Name != "TryFindNewSiteTile")
                {
                    continue;
                }
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length > 1 && parameters[1].ParameterType == typeof(PlanetTile))
                {
                    return method;
                }
            }
            return null;
        }

        public static bool Prepare()
        {
            return Target() != null;
        }

        public static MethodBase TargetMethod()
        {
            return Target();
        }

        public static bool Prefix(ref PlanetTile tile, PlanetTile nearTile, ref bool __result)
        {
            if (nearTile.Valid)
            {
                return true;
            }
            tile = PlanetTile.Invalid;
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// "Area is now safe" said once, not every time a warg dozes off.
    ///
    /// FormCaravanComp announces reformability on every TRUE-to-FALSE
    /// transition of the map's active-threat state, and a sleeping hostile
    /// is not an "active threat" (GenHostility checks Awake). Insects
    /// rarely slept; the beasts that replaced them sleep on a schedule,
    /// one animal at a time - so the threat state flickers and the message
    /// repeats. The pack is behaving correctly; the announcement was not
    /// built for hostiles that nap.
    ///
    /// Debounced by suppressing the TRANSITION: within the cooldown after
    /// a message fires, the "was threatened last tick" field is cleared
    /// before the comp reads it, so the comp sees no edge and stays quiet.
    /// Reform RULES are untouched - sneaking out past sleeping beasts
    /// still works exactly as vanilla allows; only the repeat of the toast
    /// is suppressed. RPG-mode games only.
    /// </summary>
    [HarmonyPatch(typeof(RimWorld.Planet.FormCaravanComp), "CompTickInterval")]
    public static class Patch_FormCaravan_SafeMessageDebounce
    {
        private const int CooldownTicks = 2500; // one in-game hour

        private static readonly AccessTools.FieldRef<RimWorld.Planet.FormCaravanComp, bool> LastTickRef =
            AccessTools.FieldRefAccess<RimWorld.Planet.FormCaravanComp, bool>("anyActiveThreatLastTick");

        private static readonly System.Collections.Generic.Dictionary<RimWorld.Planet.FormCaravanComp, int>
            lastAnnounce = new System.Collections.Generic.Dictionary<RimWorld.Planet.FormCaravanComp, int>();

        public static void Prefix(RimWorld.Planet.FormCaravanComp __instance)
        {
            if (!TSC_RpgMode.Active || !LastTickRef(__instance))
            {
                return; // no threat last tick: no edge is possible this tick
            }
            if (__instance.AnyActiveThreatNow)
            {
                return; // still threatened: no edge
            }
            // An edge is about to fire. First one announces and starts the
            // clock; edges inside the cooldown are erased before the comp
            // sees them.
            int now = Find.TickManager.TicksGame;
            if (lastAnnounce.TryGetValue(__instance, out int last) && now - last < CooldownTicks)
            {
                LastTickRef(__instance) = false;
                return;
            }
            lastAnnounce[__instance] = now;
        }
    }

    /// <summary>
    /// No sites on water, ever, in this mod's scenarios.
    ///
    /// Vanilla's site finder has a last-resort fallback (TryFillFindTile's
    /// BackupValidTile) that traverses impassable tiles and ACCEPTS
    /// water-covered ones - a sensible answer for a gravship colony, and
    /// how "Contract: Bad Neighbors" ended up far out in the ocean when the
    /// strict searches near the party came up empty. A medieval company can
    /// never use that answer, so it is rejected outright: better a loudly
    /// failed generation (the RequireSite tripwire names it) than a
    /// contract the party can spend a week riding toward and never reach.
    /// Gated on RPG mode so vanilla and gravship playthroughs keep
    /// vanilla's behavior.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_TryFindNewSiteTile_NoWater
    {
        public static bool Prepare()
        {
            return Patch_TryFindNewSiteTile_NoRoot.TargetForPatches() != null;
        }

        public static MethodBase TargetMethod()
        {
            return Patch_TryFindNewSiteTile_NoRoot.TargetForPatches();
        }

        private static bool inRetry;

        public static void Postfix(ref PlanetTile tile, ref bool __result,
            PlanetTile nearTile, int minDist, int maxDist, bool allowCaravans,
            System.Collections.Generic.List<RimWorld.LandmarkDef> allowedLandmarks,
            bool canSelectComboLandmarks, TileFinderMode tileFinderMode,
            bool exitOnFirstTileFound, bool canBeSpace, PlanetLayer layer,
            Predicate<PlanetTile> validator)
        {
            if (!TSC_RpgMode.Active)
            {
                return;
            }
            // TOTAL failure (no tile at all, not even a water one): vanilla's
            // fast query can come up empty in ways that are opaque from out
            // here, and a save proved it can do so for EVERY search around a
            // town - which minted a whole board of TILELESS sites. Run our
            // own plain spiral: passable-traversal outward, ordinary validity,
            // no landmarks, no fast-query cleverness. Dumb and reliable.
            if (!__result && nearTile.Valid)
            {
                if (TrySpiral(nearTile, minDist, System.Math.Max(maxDist * 3, 40), out PlanetTile found))
                {
                    tile = found;
                    __result = true;
                }
                return;
            }
            if (!__result || !tile.Valid)
            {
                return;
            }
            if (!Find.World.Impassable(tile) && !Find.WorldGrid[tile].WaterCovered)
            {
                return;
            }
            tile = PlanetTile.Invalid;
            __result = false;
            // Water was vanilla's answer because no valid land existed in
            // the REQUESTED range - common on coasts and islands, where the
            // ocean bug used to hide this exact shortage. Rather than fail
            // the contract, look farther: the guild's jobs can be a longer
            // ride, they cannot be underwater. The retry re-enters this
            // postfix (the flag stops recursion); a water result from the
            // retry is still rejected, and THEN generation fails loudly.
            if (inRetry)
            {
                return;
            }
            inRetry = true;
            try
            {
                if (TileFinder.TryFindNewSiteTile(out PlanetTile farther, nearTile,
                    minDist, System.Math.Max(maxDist * 3, 25), allowCaravans, allowedLandmarks,
                    0f, canSelectComboLandmarks, tileFinderMode, exitOnFirstTileFound,
                    canBeSpace, layer, validator))
                {
                    tile = farther;
                    __result = true;
                }
                else if (TrySpiral(nearTile, minDist, System.Math.Max(maxDist * 3, 40), out PlanetTile found))
                {
                    tile = found;
                    __result = true;
                }
            }
            finally
            {
                inRetry = false;
            }
        }

        /// <summary>Plain outward traversal: settleable, unoccupied, dry land. No cleverness to fail.</summary>
        internal static bool TrySpiral(PlanetTile nearTile, int minDist, int maxDist, out PlanetTile tile)
        {
            return TileFinder.TryFindPassableTileWithTraversalDistance(nearTile, minDist, maxDist, out tile,
                t => t.Valid
                    && !Find.WorldObjects.AnyWorldObjectAt(t)
                    && !Find.World.Impassable(t)
                    && !Find.WorldGrid[t].WaterCovered
                    && TileFinder.IsValidTileForNewSettlement(t));
        }

        /// <summary>
        /// The test every guard must use. Tile.Valid is a LIE for the bug
        /// this mod actually has: a failed tile search leaves DEFAULT tile
        /// 0 in the slate - a real tile (id 0 passes Valid), just one
        /// nobody chose, sitting wherever tile #0 happens to be on this
        /// world (an ocean, on the save that found this). Usable means:
        /// valid id, and land the party could actually stand on.
        /// </summary>
        internal static bool UsableSiteTile(PlanetTile tile)
        {
            return tile.Valid
                && !Find.World.Impassable(tile)
                && !Find.WorldGrid[tile].WaterCovered;
        }

        /// <summary>Best anchor for repairs: the root map holding the most colonists, else any caravan.</summary>
        internal static PlanetTile PartyAnchorTile()
        {
            Map best = null;
            int bestCount = 0;
            foreach (Map map in Find.Maps)
            {
                int colonists = map.mapPawns.FreeColonistsSpawnedCount;
                Map root = TSC_Threat.RootMap(map);
                if (colonists > bestCount && root != null && root.Tile.Valid)
                {
                    bestCount = colonists;
                    best = root;
                }
            }
            if (best != null)
            {
                return best.Tile;
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.IsPlayerControlled && caravan.Tile.Valid)
                {
                    return caravan.Tile;
                }
            }
            return PlanetTile.Invalid;
        }
    }

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

    /// <summary>
    /// The quest-side half of the nomad fix. QuestGen_Get.GetMap only
    /// accepts PLAYER HOME maps, and a nomadic party never owns one - so
    /// every site-spawning quest generated with a null slate map, rooted
    /// its tile search at PlanetTile -1 ("Attempted to access a tile with
    /// ID -1", logged from QuestNode_GetSiteTile), and let the tile finder
    /// fall back to a semi-random tile. Sites landed SOMEWHERE, which is
    /// why this hid for so long; "3~8 tiles away" was quietly a lie.
    ///
    /// When vanilla comes up empty in an RPG-mode game, answer with where
    /// the party actually is: the surface map (pocket chains walk to their
    /// root) holding the most free colonists. Vanilla results are never
    /// overridden.
    /// </summary>
    [HarmonyPatch(typeof(RimWorld.QuestGen.QuestGen_Get), nameof(RimWorld.QuestGen.QuestGen_Get.GetMap))]
    public static class Patch_QuestGetMap_Nomads
    {
        public static void Postfix(ref Map __result)
        {
            if (__result != null || !TSC_RpgMode.Active)
            {
                return;
            }
            // OUR scripts only. This patch briefly answered EVERY map
            // request, which let the storyteller's map-requiring quests
            // (Odyssey signals and kin) pass their can-run tests in a
            // homeless nomad game and start piling into the quest tab.
            // During storyteller candidate checks no quest is being built,
            // so this also leaves TestRun behavior fully vanilla.
            RimWorld.QuestScriptDef generating = RimWorld.QuestGen.QuestGen.quest?.root;
            if (generating?.defName == null || !generating.defName.StartsWith("TSC_"))
            {
                return;
            }
            Map best = null;
            int bestCount = 0;
            foreach (Map map in Find.Maps)
            {
                int colonists = map.mapPawns.FreeColonistsSpawnedCount;
                if (colonists == 0)
                {
                    continue;
                }
                Map root = TSC_Threat.RootMap(map);
                if (root == null || !root.Tile.Valid)
                {
                    continue;
                }
                if (colonists > bestCount)
                {
                    bestCount = colonists;
                    // Pocket-floor parties claim their ROOT map: the site
                    // search must anchor on a real world tile.
                    best = root;
                }
            }
            __result = best;
        }
    }
}
