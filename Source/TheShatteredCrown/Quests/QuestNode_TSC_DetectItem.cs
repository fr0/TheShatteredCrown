using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// "Does the company have it?" - the one possession test, shared by the
    /// fetch detector and by the abandonment guard that must not fail a
    /// contract the party has already fulfilled.
    /// </summary>
    public static class TSC_Possession
    {
        /// <summary>
        /// Possession, not proximity: loose items count only on a player HOME
        /// map (hauled home); anywhere else someone must be carrying them.
        /// Sums across every map, pawn, and caravan.
        /// </summary>
        public static int Count(ThingDef item)
        {
            if (item == null)
            {
                return 0;
            }
            int total = 0;
            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome)
                {
                    foreach (Thing thing in map.listerThings.ThingsOfDef(item))
                    {
                        total += thing.stackCount;
                    }
                }
                foreach (Pawn pawn in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    total += HeldCount(pawn, item);
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
                        total += thing.stackCount;
                    }
                }
                // Reforming packs the loot onto the PAWNS, and the caravan's
                // inventory view can lag a tick behind that - count both, or
                // walking out of a site with the prize reads as empty-handed.
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    total += HeldCount(pawn, item);
                }
            }
            return total;
        }

        private static int HeldCount(Pawn pawn, ThingDef item)
        {
            int total = 0;
            if (pawn.inventory?.innerContainer != null)
            {
                foreach (Thing thing in pawn.inventory.innerContainer)
                {
                    if (thing.def == item)
                    {
                        total += thing.stackCount;
                    }
                }
            }
            if (pawn.carryTracker?.CarriedThing?.def == item)
            {
                total += pawn.carryTracker.CarriedThing.stackCount;
            }
            return total;
        }
    }

    /// <summary>
    /// Abandonment guard: runs its inner node (a fail) on the signal ONLY if
    /// the party does not hold the item. Leaving a site is abandoning the
    /// job when you left the prize behind, and finishing it when you did
    /// not - and site.Destroyed fires the instant a caravan reforms, long
    /// before the fetch detector's next poll, so a blind fail on that signal
    /// snatched completed contracts away at the door.
    /// </summary>
    public class QuestNode_TSC_FailUnlessHolding : QuestNode
    {
        public SlateRef<ThingDef> item;
        public SlateRef<int> minCount;
        public QuestNode node;

        [NoTranslate]
        public SlateRef<string> inSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_FailUnlessHolding part = new QuestPart_TSC_FailUnlessHolding
            {
                item = item.GetValue(slate),
                minCount = System.Math.Max(1, minCount.GetValue(slate)),
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)),
                outSignalMissing = QuestGen.GenerateNewSignal("ItemMissing"),
            };
            QuestGen.quest.AddPart(part);
            if (node != null)
            {
                QuestGenUtility.RunInnerNode(node, part.outSignalMissing);
            }
        }

        protected override bool TestRunInt(Slate slate)
        {
            return item.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_FailUnlessHolding : QuestPart
    {
        public ThingDef item;
        public int minCount = 1;
        public string inSignal;
        public string outSignalMissing;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal)
            {
                return;
            }
            if (TSC_Possession.Count(item) >= minCount)
            {
                return; // they have it: the fetch detector will pay out
            }
            Find.SignalManager.SendSignal(new Signal(outSignalMissing, signal.args));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref item, "item");
            Scribe_Values.Look(ref minCount, "minCount", 1);
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref outSignalMissing, "outSignalMissing");
        }
    }

    /// <summary>
    /// Completes (running its inner node) when the player obtains any item of the
    /// given def - on any map or in a caravan. The "fetch the quest item" primitive:
    /// used by the intro to detect the weathered map case leaving the vault.
    /// </summary>
    public class QuestNode_TSC_DetectItem : QuestNode
    {
        public SlateRef<ThingDef> item;
        /// <summary>Total pieces the player must possess (default 1). Cold Iron wants a load, not a sliver.</summary>
        public SlateRef<int> minCount;
        /// <summary>Possession only counts once no map with colonists has an active hostile threat - the crypt's shard is not "recovered" mid-boss-fight.</summary>
        public SlateRef<bool> requireNoHostiles;
        public QuestNode node;

        [NoTranslate]
        public SlateRef<string> inSignalEnable;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_DetectItem part = new QuestPart_TSC_DetectItem
            {
                item = item.GetValue(slate),
                minCount = System.Math.Max(1, minCount.GetValue(slate)),
                requireNoHostiles = requireNoHostiles.GetValue(slate),
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
        public int minCount = 1;
        public bool requireNoHostiles;

        // Was 250 (4 real seconds): long enough that leaving a site could
        // resolve the quest's OTHER outcomes before the fetch was noticed.
        private const int CheckInterval = 60;

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (item == null || Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }
            if (requireNoHostiles)
            {
                foreach (Map map in Find.Maps)
                {
                    if (map.mapPawns.FreeColonistsSpawnedCount > 0
                        && GenHostility.AnyHostileActiveThreatToPlayer(map))
                    {
                        return; // holding the prize mid-fight is not recovering it
                    }
                }
            }
            // Detection means POSSESSION, not proximity: quest loot can lie
            // pre-spawned on site/pocket maps (the barrow moss on the crypt
            // floor), and merely generating the map must not complete the
            // fetch.
            if (TSC_Possession.Count(item) >= minCount)
            {
                Complete();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref item, "item");
            Scribe_Values.Look(ref minCount, "minCount", 1);
        }
    }
}
