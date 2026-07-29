using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The guild pays: on the given signal, silver lands with the party -
    /// into a caravan's inventory on the road, or dropped at a free
    /// colonist's feet on a map. The contract reward primitive.
    ///
    /// Contracts also pay guild coins, the scrip that buys what silver
    /// cannot; set <c>coins</c> alongside <c>amount</c> and both land in
    /// the same payout, announced together.
    /// </summary>
    public class QuestNode_TSC_GiveSilver : QuestNode
    {
        public SlateRef<int> amount;

        public SlateRef<int> coins;

        [NoTranslate]
        public SlateRef<string> inSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestGen.quest.AddPart(new QuestPart_TSC_GiveSilver
            {
                amount = amount.GetValue(slate),
                coins = coins.GetValue(slate),
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            });
        }

        protected override bool TestRunInt(Slate slate)
        {
            return amount.GetValue(slate) > 0 || coins.GetValue(slate) > 0;
        }
    }

    public class QuestPart_TSC_GiveSilver : QuestPart
    {
        public string inSignal;
        public int amount;
        public int coins;
        private bool paid;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || paid || (amount <= 0 && coins <= 0))
            {
                return;
            }
            paid = true;
            // Haggled once at a guild hall, better rates forever: the
            // TSC_GuildRates flag is set by the factor's Persuasion check.
            // Silver only - the haggle is over the charter rate, and coin
            // prices on the quartermaster's shelf stay predictable.
            if (DialogueStateManager.Current.IsSet("TSC_GuildRates"))
            {
                amount = UnityEngine.Mathf.RoundToInt(amount * 1.2f);
            }
            // A spawned colonist first (drop at their feet), a caravan second.
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    PlaceSilver(pawn.Position, map, pawn);
                    TSC_GuildCoins.Give(coins, out _);
                    Announce(pawn);
                    return;
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled || caravan.PawnsListForReading.Count == 0)
                {
                    continue;
                }
                if (amount > 0)
                {
                    Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                    silver.stackCount = amount;
                    CaravanInventoryUtility.GiveThing(caravan, silver);
                }
                TSC_GuildCoins.Give(coins, out _);
                Announce(caravan.PawnsListForReading[0]);
                return;
            }
        }

        private void PlaceSilver(IntVec3 cell, Map map, Pawn payee)
        {
            if (amount <= 0)
            {
                return;
            }
            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = amount;
            // Same reasoning as the coins: a fee paid onto the floor of a
            // site the party is about to leave is a fee they never see.
            if (payee?.inventory?.innerContainer?.TryAdd(silver, false) == true)
            {
                return;
            }
            GenPlace.TryPlaceThing(silver, cell, map, ThingPlaceMode.Near);
        }

        private void Announce(Thing near)
        {
            string pay;
            if (amount > 0 && coins > 0)
            {
                pay = $"{amount} silver and {TSC_GuildCoins.Label(coins)}";
            }
            else if (coins > 0)
            {
                pay = TSC_GuildCoins.Label(coins);
            }
            else
            {
                pay = $"{amount} silver";
            }
            Messages.Message($"The guild pays out {pay} for the contract.",
                near, MessageTypeDefOf.PositiveEvent, historical: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref amount, "amount");
            Scribe_Values.Look(ref coins, "coins");
            Scribe_Values.Look(ref paid, "paid");
        }
    }
}
