using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The escort: a contract where the objective walks beside you and can
    /// die. The guild has somebody who needs to be somewhere else - alive -
    /// and the road between here and there is the whole of the job.
    ///
    /// The charge joins the party the moment the contract is accepted, as an
    /// ordinary temporary member: they walk, they ride, they eat your food,
    /// and every ambush on the way wants them specifically dead in the way
    /// ambushes want everyone dead. Deliver them to the destination's gates
    /// and they walk through; lose them and the contract is lost with them.
    /// </summary>
    public class QuestNode_TSC_Escort : QuestNode
    {
        /// <summary>How far away the destination gates may be, in world tiles.</summary>
        public SlateRef<float> maxTileDistance = 16f;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestGen.quest.AddPart(new QuestPart_TSC_Escort
            {
                inSignalEnable = slate.Get<string>("inSignal"),
                maxTileDistance = maxTileDistance.GetValue(slate),
                outSignalDelivered = QuestGenUtility.HardcodedSignalWithQuestID("escort.Delivered"),
                outSignalDied = QuestGenUtility.HardcodedSignalWithQuestID("escort.Died"),
                outSignalNoRoad = QuestGenUtility.HardcodedSignalWithQuestID("escort.NoRoad"),
            });
        }

        protected override bool TestRunInt(Slate slate)
        {
            // Generation paths that skip TestRun exist (the board, the debug
            // spawner), so the real safety is the lazy pick at accept time;
            // this gate covers the storyteller path.
            return QuestPart_TSC_Escort.PickDestination(maxTileDistance.GetValue(slate)) != null;
        }
    }

    public class QuestPart_TSC_Escort : QuestPartActivable
    {
        public WorldObject destination;
        public float maxTileDistance = 16f;
        public string outSignalDelivered;
        public string outSignalDied;
        public string outSignalNoRoad;

        private Pawn charge;

        private const int CheckInterval = 150;

        /// <summary>
        /// The quest tab's clickable targets: the clerk, and the gates they
        /// are owed to. The destination is only chosen at accept time, so
        /// the generated quest text cannot name it - these two overrides
        /// are how a late-bound destination reaches the UI at all.
        /// </summary>
        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                if (charge != null && !charge.Destroyed)
                {
                    yield return charge;
                }
                if (destination != null && !destination.Destroyed)
                {
                    yield return destination;
                }
            }
        }

        public override string DescriptionPart =>
            destination == null
                ? null
                : $"Destination: {destination.Label}."
                  + (charge != null && !charge.Dead ? $" The charge is {charge.LabelShortCap}." : "");

        /// <summary>
        /// The gates nearest to wherever the party actually is: any
        /// visitable, non-hostile settlement in range of the party's tile.
        /// This used to be vanilla's QuestNode_GetNearbySettlement, which
        /// dereferences the slate's map - and quests generated from the
        /// board or the debug spawner may have no map in the slate at all,
        /// which was an NRE at generation. Picking here, at ACCEPT time,
        /// also means the destination is measured from where the party is
        /// when the road actually starts.
        /// </summary>
        public static WorldObject PickDestination(float maxTiles)
        {
            PlanetTile origin = PartyTile();
            if (!origin.Valid)
            {
                return null;
            }
            // Both kinds of gates count: vanilla settlements AND this mod's
            // villages, which are Sites. Contracts are accepted standing in
            // a town, the town itself is excluded as "not a road", and in
            // village country the settlements can be far apart - so a
            // settlements-only picker found nothing and the contract failed
            // the moment it was taken.
            WorldObject best = null;
            float bestDistance = float.MaxValue;
            foreach (Settlement settlement in Find.WorldObjects.SettlementBases)
            {
                if (settlement.Faction == null || settlement.Faction == Faction.OfPlayer
                    || settlement.Faction.HostileTo(Faction.OfPlayer) || !settlement.Visitable)
                {
                    continue;
                }
                Consider(settlement, origin, maxTiles, ref best, ref bestDistance);
            }
            foreach (Site site in Find.WorldObjects.Sites)
            {
                if (TSC_Homeward.VillageSite(site))
                {
                    Consider(site, origin, maxTiles, ref best, ref bestDistance);
                }
            }
            return best;
        }

        private static void Consider(WorldObject candidate, PlanetTile origin, float maxTiles,
            ref WorldObject best, ref float bestDistance)
        {
            float distance = Find.WorldGrid.ApproxDistanceInTiles(origin, candidate.Tile);
            // A destination the party is already standing in is not a road.
            if (distance < 1f || distance > maxTiles || distance >= bestDistance)
            {
                return;
            }
            best = candidate;
            bestDistance = distance;
        }

        private static PlanetTile PartyTile()
        {
            foreach (Map map in Find.Maps)
            {
                if (map.mapPawns.FreeColonistsSpawnedCount > 0)
                {
                    return map.Tile;
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.IsPlayerControlled)
                {
                    return caravan.Tile;
                }
            }
            return PlanetTile.Invalid;
        }

        protected override void Enable(SignalArgs receivedArgs)
        {
            base.Enable(receivedArgs);
            if (destination == null)
            {
                destination = PickDestination(maxTileDistance);
            }
            if (destination == null)
            {
                // No road can be arranged from here: void the contract with
                // its own letter rather than blame a charge who never
                // existed for dying.
                Complete();
                Find.SignalManager.SendSignal(new Signal(outSignalNoRoad));
                return;
            }
            SpawnCharge();
        }

        /// <summary>
        /// The charge appears beside the party when the contract is taken:
        /// the guild walks them out of the back room and hands them over.
        /// </summary>
        private void SpawnCharge()
        {
            Map map = null;
            Pawn anchor = null;
            foreach (Map candidate in Find.Maps)
            {
                foreach (Pawn colonist in candidate.mapPawns.FreeColonistsSpawned)
                {
                    map = candidate;
                    anchor = colonist;
                    break;
                }
                if (map != null)
                {
                    break;
                }
            }
            if (map == null)
            {
                // Accepted from a caravan mid-journey: the charge was waiting
                // at the meeting point all along and simply joins the column.
                Caravan caravan = null;
                foreach (Caravan candidate in Find.WorldObjects.Caravans)
                {
                    if (candidate.IsPlayerControlled)
                    {
                        caravan = candidate;
                        break;
                    }
                }
                if (caravan == null)
                {
                    return; // nowhere to put them; the tick will retry
                }
                charge = Generate();
                caravan.AddPawn(charge, addCarriedPawnToWorldPawnsIfAny: true);
                if (!charge.IsWorldPawn())
                {
                    Find.WorldPawns.PassToWorld(charge);
                }
                Announce();
                return;
            }
            charge = Generate();
            // Never on the anchor's own cell: two drafted pawns sharing a
            // cell is what winds up vanilla's shuffle reflex, and under the
            // turn freeze that reflex spins in place.
            IntVec3 cell = CellFinder.StandableCellNear(anchor.Position, map, 5f);
            if (!cell.IsValid || cell == anchor.Position)
            {
                cell = CellFinder.RandomClosewalkCellNear(anchor.Position, map, 6);
            }
            GenSpawn.Spawn(charge, cell.IsValid ? cell : anchor.Position, map);
            Announce();
        }

        private Pawn Generate()
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_EscortCharge")
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager");
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            return pawn;
        }

        private void Announce()
        {
            if (charge == null)
            {
                return;
            }
            // A letter, not a message: it survives being missed, and its
            // look targets are the whole briefing - click once for the
            // clerk, once for the gates.
            Find.LetterStack.ReceiveLetter(
                "The charge",
                $"{charge.LabelShortCap} travels with the company until they stand inside "
                + $"{destination?.Label ?? "the destination"}'s walls. The guild expects them alive.",
                LetterDefOf.NeutralEvent,
                new LookTargets(new List<GlobalTargetInfo> { charge, destination }));
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }
            if (charge == null)
            {
                SpawnCharge(); // accepted with no map and no caravan; keep trying
                return;
            }
            if (charge.Destroyed || charge.Dead)
            {
                Complete();
                Find.SignalManager.SendSignal(new Signal(outSignalDied));
                return;
            }
            // Handed away, imprisoned, gift-caravanned off: the party no
            // longer has them, and the contract does not care why.
            if (charge.Faction != Faction.OfPlayer)
            {
                Complete();
                Find.SignalManager.SendSignal(new Signal(outSignalDied));
                return;
            }
            if (destination == null)
            {
                return;
            }
            bool arrived = (charge.IsCaravanMember() && charge.GetCaravan().Tile == destination.Tile)
                || (charge.MapHeld != null && charge.MapHeld.Parent == destination);
            if (!arrived)
            {
                return;
            }
            Deliver();
            Complete();
            Find.SignalManager.SendSignal(new Signal(outSignalDelivered));
        }

        /// <summary>They walk through the gates and out of the party.</summary>
        private void Deliver()
        {
            Caravan caravan = charge.GetCaravan();
            if (caravan != null)
            {
                caravan.RemovePawn(charge);
            }
            else if (charge.Spawned)
            {
                charge.DeSpawn();
            }
            // A village site may carry no faction; the clerk still needs one
            // that is not ours.
            charge.SetFaction(destination.Faction ?? Faction.OfAncients);
            if (!charge.IsWorldPawn())
            {
                Find.WorldPawns.PassToWorld(charge);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref destination, "destination");
            Scribe_Values.Look(ref maxTileDistance, "maxTileDistance", 16f);
            Scribe_Values.Look(ref outSignalNoRoad, "outSignalNoRoad");
            Scribe_References.Look(ref charge, "charge");
            Scribe_Values.Look(ref outSignalDelivered, "outSignalDelivered");
            Scribe_Values.Look(ref outSignalDied, "outSignalDied");
        }
    }
}
