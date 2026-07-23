using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Completes (running its inner node) when the player obtains any item of the
    /// given def - on any map or in a caravan. The "fetch the quest item" primitive:
    /// used by the intro to detect the weathered map case leaving the vault.
    /// </summary>
    public class QuestNode_TSC_DetectItem : QuestNode
    {
        public SlateRef<ThingDef> item;
        public QuestNode node;

        [NoTranslate]
        public SlateRef<string> inSignalEnable;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_DetectItem part = new QuestPart_TSC_DetectItem
            {
                item = item.GetValue(slate),
                inSignalEnable = QuestGenUtility.HardcodedSignalWithQuestID(inSignalEnable.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            };
            QuestGen.quest.AddPart(part);
            if (node != null)
            {
                QuestGenUtility.RunInnerNode(node, part.OutSignalCompleted);
            }
        }

        protected override bool TestRunInt(Slate slate)
        {
            return item.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_DetectItem : QuestPartActivable
    {
        public ThingDef item;

        private const int CheckInterval = 250;

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (item == null || Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }
            foreach (Map map in Find.Maps)
            {
                // Detection means POSSESSION, not proximity: quest loot can
                // lie pre-spawned on site/pocket maps (the barrow moss on the
                // crypt floor), and merely generating the map must not
                // complete the fetch. Loose items only count on a player HOME
                // map (hauled home); elsewhere someone must pick them up.
                if (map.IsPlayerHome && map.listerThings.ThingsOfDef(item).Count > 0)
                {
                    Complete();
                    return;
                }
                foreach (Pawn pawn in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    if (PawnHolds(pawn))
                    {
                        Complete();
                        return;
                    }
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled)
                {
                    continue;
                }
                foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan))
                {
                    if (thing.def == item)
                    {
                        Complete();
                        return;
                    }
                }
            }
        }

        private bool PawnHolds(Pawn pawn)
        {
            if (pawn.carryTracker?.CarriedThing?.def == item)
            {
                return true;
            }
            ThingOwner inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
            {
                foreach (Thing thing in inventory)
                {
                    if (thing.def == item)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref item, "item");
        }
    }
}
