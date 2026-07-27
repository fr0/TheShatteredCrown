using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Peaceful entry into friendly towns. Vanilla caravans can only Trade,
    /// Gift, or Attack a settlement - there is no way to WALK IN, which the
    /// story needs (the bard lead completes inside the city, and guild
    /// factors keep their boards there). "Visit" generates the settlement
    /// map and walks the caravan in at the edge; leave by walking out.
    /// </summary>
    public class CaravanArrivalAction_TSC_Visit : CaravanArrivalAction
    {
        private Settlement settlement;

        public CaravanArrivalAction_TSC_Visit()
        {
        }

        public CaravanArrivalAction_TSC_Visit(Settlement settlement)
        {
            this.settlement = settlement;
        }

        public override string Label => $"Visit {settlement.Label}";

        public override string ReportString => $"visiting {settlement.Label}";

        public static FloatMenuAcceptanceReport CanVisit(Caravan caravan, Settlement settlement)
        {
            if (settlement == null || !settlement.Spawned || settlement.Faction == null
                || settlement.Faction.IsPlayer || settlement.Faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }
            return true;
        }

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport baseReport = base.StillValid(caravan, destinationTile);
            if (!baseReport)
            {
                return baseReport;
            }
            if (settlement != null && settlement.Tile != destinationTile)
            {
                return false;
            }
            return CanVisit(caravan, settlement);
        }

        // Vanilla towns are a 34-38 cell block; a CITY the story sends you to
        // should feel like one. Swapped in around first generation only.
        private static readonly IntRange CitySizeRange = new IntRange(52, 58);

        public override void Arrived(Caravan caravan)
        {
            bool firstGeneration = !settlement.HasMap;
            System.Reflection.FieldInfo sizeField = AccessTools.Field(typeof(GenStep_Settlement), "SettlementSizeRange");
            IntRange vanillaSize = default;
            if (firstGeneration && sizeField != null)
            {
                vanillaSize = (IntRange)sizeField.GetValue(null);
                sizeField.SetValue(null, CitySizeRange);
            }
            Map map;
            try
            {
                map = GetOrGenerateMapUtility.GetOrGenerateMap(settlement.Tile, null);
            }
            finally
            {
                if (firstGeneration && sizeField != null)
                {
                    sizeField.SetValue(null, vanillaSize);
                }
            }
            if (map == null)
            {
                return;
            }
            // No solar panels in a medieval story: same purge the bandit
            // camps get. Runs on EVERY visit (idempotent, cheap) so cities
            // generated before the purge rules tightened get cleaned too.
            GenStep_TSC_MedievalPurge.Purge(map, settlement.Label);
            // Vanilla fog stays (interiors reveal on entry, as anywhere);
            // Real FoW's veil is disabled wholesale on friendly maps by the
            // compat shim - the half-and-half looked worse than either.
            EnsureTownsfolk(map, settlement);
            CaravanEnterMapUtility.Enter(caravan, map, CaravanEnterMode.Edge,
                CaravanDropInventoryMode.DoNotDrop, draftColonists: false);
            Messages.Message($"The party enters {settlement.Label} as guests. Leave by walking off the map edge.",
                MessageTypeDefOf.NeutralEvent, historical: false);
        }

        /// <summary>
        /// Settlement defenders scale with threat points, and a nomadic
        /// party's are tiny - a "city" can generate near-empty (which also
        /// tripped the instant-defeat check). Guarantee a town's worth of
        /// locals so the place reads inhabited.
        /// </summary>
        private static void EnsureTownsfolk(Map map, Settlement settlement)
        {
            Faction faction = settlement.Faction;
            if (faction == null)
            {
                return;
            }
            int present = 0;
            foreach (Pawn pawn in map.mapPawns.SpawnedPawnsInFaction(faction))
            {
                if (pawn.RaceProps.Humanlike)
                {
                    present++;
                }
            }
            if (present >= 5)
            {
                return;
            }
            PawnGroupMakerParms parms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Settlement,
                faction = faction,
                points = 600f,
                tile = map.Tile,
                inhabitants = true,
            };
            List<Pawn> locals = new List<Pawn>(PawnGroupMakerUtility.GeneratePawns(parms, warnOnZeroResults: false));
            if (locals.Count == 0)
            {
                return;
            }
            IntVec3 center = map.Center;
            List<Pawn> spawnedNow = new List<Pawn>();
            foreach (Pawn local in locals)
            {
                if (spawnedNow.Count >= 8 - present)
                {
                    Find.WorldPawns.PassToWorld(local, RimWorld.Planet.PawnDiscardDecideMode.Discard);
                    continue;
                }
                IntVec3 cell = CellFinder.StandableCellNear(center, map, 22f);
                if (!cell.IsValid)
                {
                    Find.WorldPawns.PassToWorld(local, RimWorld.Planet.PawnDiscardDecideMode.Discard);
                    continue;
                }
                GenSpawn.Spawn(local, cell, map);
                spawnedNow.Add(local);
            }
            if (spawnedNow.Count > 0)
            {
                Verse.AI.Group.LordMaker.MakeNewLord(faction,
                    new LordJob_DefendBase(faction, center, 0, attackWhenPlayerBecameEnemy: true), map, spawnedNow);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref settlement, "settlement");
        }
    }

    /// <summary>
    /// A peaceful visit is not a conquest: vanilla's defeat check fires the
    /// moment a settlement map exists with no living locals - which a
    /// near-empty friendly town tripped INSTANTLY on entry ("base
    /// destroyed"). You cannot defeat a base you are not at war with; if
    /// the visit turns violent (faction flips hostile), vanilla resumes.
    /// </summary>
    [HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
    public static class Patch_NoDefeatOnFriendlyVisit
    {
        public static bool Prefix(Settlement factionBase)
        {
            if (factionBase?.Faction != null && !factionBase.Faction.IsPlayer
                && !factionBase.Faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetFloatMenuOptions))]
    public static class Patch_Settlement_VisitOption
    {
        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> options,
            Settlement __instance, Caravan caravan)
        {
            foreach (FloatMenuOption option in options)
            {
                yield return option;
            }
            if (!TSC_RpgMode.Active)
            {
                yield break;
            }
            foreach (FloatMenuOption option in CaravanArrivalActionUtility.GetFloatMenuOptions(
                () => CaravanArrivalAction_TSC_Visit.CanVisit(caravan, __instance),
                () => new CaravanArrivalAction_TSC_Visit(__instance),
                $"Visit {__instance.Label}", caravan, __instance.Tile, __instance))
            {
                yield return option;
            }
        }
    }
}
