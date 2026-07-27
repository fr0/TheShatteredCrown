using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.Grammar;

namespace TheShatteredCrown
{
    /// <summary>
    /// Picks a NON-HOSTILE humanlike settlement a real journey away (the
    /// "distant city" of a lead), stores it and its faction in the slate,
    /// quest-tags it (so <tag>.MapGenerated signals fire for it), and adds
    /// [<tag>_label] to the quest name/description rules so the text can
    /// name the city. Falls back to the nearest non-hostile settlement if
    /// nothing sits inside the distance band.
    /// </summary>
    public class QuestNode_TSC_GetDistantSettlement : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> storeAs;
        [NoTranslate]
        public SlateRef<string> storeFactionAs;
        [NoTranslate]
        public SlateRef<string> tag;
        public SlateRef<int> minDist;
        public SlateRef<int> maxDist;

        protected override bool TestRunInt(Slate slate)
        {
            return FindSettlement(slate) != null;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Settlement settlement = FindSettlement(slate) ?? TryCreateSettlement(slate);
            if (settlement == null)
            {
                // Dialogue-given quests skip TestRun, so this node CAN run
                // against a world with no usable settlement AND no faction to
                // found one for. Keep the grammar resolvable (no red errors,
                // readable quest text) and say loudly what is missing.
                Log.Warning("[The Shattered Crown] GetDistantSettlement: no usable settlement on the world and "
                    + "no non-hostile humanlike faction to found one for. Quest text falls back to a generic "
                    + "city name and the travel target will be missing.");
                string fallbackTag = tag.GetValue(slate);
                if (!fallbackTag.NullOrEmpty())
                {
                    List<Rule> fallbackRules = new List<Rule>
                    {
                        new Rule_String(fallbackTag + "_label", "a distant city"),
                    };
                    QuestGen.AddQuestNameRules(fallbackRules);
                    QuestGen.AddQuestDescriptionRules(fallbackRules);
                }
                return;
            }
            // The quest log link: without a part exposing the city as a look
            // target, the quest has no "jump to" hyperlink at all.
            QuestGen.quest.AddPart(new QuestPart_TSC_LookTarget { target = settlement });
            slate.Set(storeAs.GetValue(slate), settlement);
            if (!storeFactionAs.GetValue(slate).NullOrEmpty())
            {
                slate.Set(storeFactionAs.GetValue(slate), settlement.Faction);
            }
            string rawTag = tag.GetValue(slate);
            if (!rawTag.NullOrEmpty())
            {
                QuestUtility.AddQuestTag(settlement, QuestGenUtility.HardcodedTargetQuestTagWithQuestID(rawTag));
                List<Rule> rules = new List<Rule>
                {
                    new Rule_String(rawTag + "_label", settlement.Label),
                };
                QuestGen.AddQuestNameRules(rules);
                QuestGen.AddQuestDescriptionRules(rules);
            }
        }

        private static PlanetTile FindAnchor()
        {
            // A POCKET map (the crypt underground) has no world tile - and
            // when the whole party is down there, it is the only map with
            // colonists. Resolve through the pocket's source map, or the
            // party never has a world position while underground and the
            // whole city hunt dies at "no anchor".
            PlanetTile viaPocket = PlanetTile.Invalid;
            foreach (Map map in Find.Maps)
            {
                if (map.mapPawns.FreeColonistsSpawnedCount == 0)
                {
                    continue;
                }
                if (map.Tile.Valid)
                {
                    return map.Tile;
                }
                if (!viaPocket.Valid)
                {
                    // Walk the WHOLE pocket chain: the sunken cellars are
                    // three deep, so one hop up is not enough to reach a map
                    // that has a world tile.
                    Map surface = map;
                    int guard = 8;
                    while (surface != null && !surface.Tile.Valid && guard-- > 0)
                    {
                        surface = (surface.Parent as PocketMapParent)?.sourceMap;
                    }
                    if (surface != null && surface.Tile.Valid)
                    {
                        viaPocket = surface.Tile;
                    }
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.IsPlayerControlled)
                {
                    return caravan.Tile;
                }
            }
            if (viaPocket.Valid)
            {
                return viaPocket;
            }
            // Last resort: any player-relevant map with a real world position
            // (the surface map above the pocket still exists while the party
            // is underground, even with nobody standing on it).
            foreach (Map map in Find.Maps)
            {
                if (map.Tile.Valid)
                {
                    return map.Tile;
                }
            }
            return PlanetTile.Invalid;
        }

        /// <summary>
        /// A world with no cities still needs one for the lead: FOUND one for
        /// the friendly story faction inside the distance band. Better a city
        /// appearing on the map than a quest pointing at nothing.
        /// </summary>
        private Settlement TryCreateSettlement(Slate slate)
        {
            PlanetTile anchor = FindAnchor();
            if (!anchor.Valid)
            {
                Log.Warning("[The Shattered Crown] GetDistantSettlement: no anchor (no colonist map, no caravan).");
                return null;
            }
            Faction faction = GenStep_TSC_Village.VillagerFaction();
            if (faction == null)
            {
                Log.Warning("[The Shattered Crown] GetDistantSettlement: no non-hostile humanlike faction exists to found a city for.");
                return null;
            }
            int min = System.Math.Max(1, minDist.GetValue(slate));
            int max = maxDist.GetValue(slate) > 0 ? maxDist.GetValue(slate) : 30;
            bool ValidTile(PlanetTile t) => !Find.WorldObjects.AnyWorldObjectAt(t) && TileFinder.IsValidTileForNewSettlement(t);
            // Traversal distance can dead-end in a walled-off region (a pass,
            // an island) long before 12 tiles - fall back to shorter reach,
            // then to the generic site finder.
            if (!TileFinder.TryFindPassableTileWithTraversalDistance(anchor, min, max, out PlanetTile tile, ValidTile)
                && !TileFinder.TryFindPassableTileWithTraversalDistance(anchor, 1, max, out tile, ValidTile)
                && !TileFinder.TryFindNewSiteTile(out tile, anchor, min, max, allowCaravans: false,
                    allowedLandmarks: null, selectLandmarkChance: 0f, canSelectComboLandmarks: false,
                    tileFinderMode: TileFinderMode.Near, exitOnFirstTileFound: false, canBeSpace: false,
                    layer: null, validator: ValidTile))
            {
                Log.Warning("[The Shattered Crown] GetDistantSettlement: no valid tile found for a new city (region too small?).");
                return null;
            }
            Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.SetFaction(faction);
            settlement.Tile = tile;
            settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
            Find.WorldObjects.Add(settlement);
            Log.Message($"[The Shattered Crown] GetDistantSettlement: world had no usable settlement; "
                + $"founded {settlement.Name} ({faction.Name}) {min}-{max} tiles out for the lead.");
            return settlement;
        }

        private Settlement FindSettlement(Slate slate)
        {
            PlanetTile anchor = FindAnchor();
            if (!anchor.Valid)
            {
                return null;
            }
            int min = minDist.GetValue(slate);
            int max = maxDist.GetValue(slate) > 0 ? maxDist.GetValue(slate) : 30;
            // Tiered: farthest NON-HOSTILE inside the band, else nearest
            // non-hostile anywhere, else nearest humanlike settlement even if
            // hostile - a compromised lead beats an unresolvable quest.
            Settlement bandBest = null;
            float bandBestDist = -1f;
            Settlement nearestFriendly = null;
            float nearestFriendlyDist = float.MaxValue;
            Settlement nearestAny = null;
            float nearestAnyDist = float.MaxValue;
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == null || settlement.Faction.IsPlayer
                    || settlement.Faction.def.hidden || !settlement.Faction.def.humanlikeFaction)
                {
                    continue;
                }
                float dist = Find.WorldGrid.ApproxDistanceInTiles(anchor, settlement.Tile);
                if (dist < nearestAnyDist)
                {
                    nearestAny = settlement;
                    nearestAnyDist = dist;
                }
                if (settlement.Faction.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }
                if (dist < nearestFriendlyDist)
                {
                    nearestFriendly = settlement;
                    nearestFriendlyDist = dist;
                }
                // Farthest inside the band: a lead should feel like a journey.
                if (dist >= min && dist <= max && dist > bandBestDist)
                {
                    bandBest = settlement;
                    bandBestDist = dist;
                }
            }
            Settlement chosen = bandBest ?? nearestFriendly ?? nearestAny;
            if (chosen != null && Prefs.DevMode)
            {
                string tier = chosen == bandBest ? "band" : chosen == nearestFriendly ? "nearest-friendly" : "nearest-ANY (hostile fallback)";
                Log.Message($"[TSC] GetDistantSettlement: {chosen.Label} ({chosen.Faction?.Name}), tier={tier}, "
                    + $"dist={Find.WorldGrid.ApproxDistanceInTiles(anchor, chosen.Tile):0.#}");
            }
            return chosen;
        }
    }

    /// <summary>Exposes one world object as a quest look target, so the quest log gets its jump-to hyperlink.</summary>
    public class QuestPart_TSC_LookTarget : QuestPart
    {
        public WorldObject target;

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                foreach (GlobalTargetInfo baseTarget in base.QuestLookTargets)
                {
                    yield return baseTarget;
                }
                if (target != null && !target.Destroyed)
                {
                    yield return target;
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref target, "target");
        }
    }
}
