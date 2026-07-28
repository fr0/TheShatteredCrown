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

        /// <summary>
        /// Held by somebody standing in a guild hall.
        ///
        /// Carrying the prize out of the ruin is the adventure; handing it
        /// over is the contract. Counting mere possession meant a fetch job
        /// paid out the moment the lid came off, with the coffer still in a
        /// cellar four days' travel from anyone who wanted it - which made
        /// the return trip optional and the guild an abstraction.
        ///
        /// Caravans do not count: a caravan is on the road, not at a table.
        /// The party has to actually arrive and enter the settlement.
        /// </summary>
        public static int CountAtGuild(ThingDef item)
        {
            if (item == null)
            {
                return 0;
            }
            int total = 0;
            foreach (Map map in Find.Maps)
            {
                if (!TSC_GuildHallUtility.IsGuildHall(map))
                {
                    continue;
                }
                foreach (Pawn pawn in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    total += HeldCount(pawn, item);
                }
                // Set down on the floor of the hall still counts as delivered:
                // the player has done the travelling, and making them keep it
                // in a backpack is bookkeeping, not gameplay.
                foreach (Thing thing in map.listerThings.ThingsOfDef(item))
                {
                    if (thing.Spawned)
                    {
                        total += thing.stackCount;
                    }
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

        /// <summary>
        /// The item must be brought to a guild hall, not merely held. Set on
        /// guild fetch contracts, where the job is delivery; left off for
        /// story items the party simply needs to have found.
        /// </summary>
        public SlateRef<bool> deliverToGuild;
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
                deliverToGuild = deliverToGuild.GetValue(slate),
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
        public bool deliverToGuild;

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
            int held = deliverToGuild ? TSC_Possession.CountAtGuild(item) : TSC_Possession.Count(item);
            if (held >= minCount)
            {
                Complete();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref item, "item");
            Scribe_Values.Look(ref minCount, "minCount", 1);
            Scribe_Values.Look(ref deliverToGuild, "deliverToGuild", false);
        }
    }
}
