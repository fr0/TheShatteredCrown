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
    /// </summary>
    public class QuestNode_TSC_GiveSilver : QuestNode
    {
        public SlateRef<int> amount;

        [NoTranslate]
        public SlateRef<string> inSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestGen.quest.AddPart(new QuestPart_TSC_GiveSilver
            {
                amount = amount.GetValue(slate),
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            });
        }

        protected override bool TestRunInt(Slate slate)
        {
            return amount.GetValue(slate) > 0;
        }
    }

    public class QuestPart_TSC_GiveSilver : QuestPart
    {
        public string inSignal;
        public int amount;
        private bool paid;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || paid || amount <= 0)
            {
                return;
            }
            paid = true;
            // Haggled once at a guild hall, better rates forever: the
            // TSC_GuildRates flag is set by the factor's Persuasion check.
            if (DialogueStateManager.Current.IsSet("TSC_GuildRates"))
            {
                amount = UnityEngine.Mathf.RoundToInt(amount * 1.2f);
            }
            // A spawned colonist first (drop at their feet), a caravan second.
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                    silver.stackCount = amount;
                    GenPlace.TryPlaceThing(silver, pawn.Position, map, ThingPlaceMode.Near);
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
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = amount;
                CaravanInventoryUtility.GiveThing(caravan, silver);
                Announce(caravan.PawnsListForReading[0]);
                return;
            }
        }

        private void Announce(Thing near)
        {
            Messages.Message($"The guild pays out {amount} silver for the contract.",
                near, MessageTypeDefOf.PositiveEvent, historical: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref amount, "amount");
            Scribe_Values.Look(ref paid, "paid");
        }
    }
}
