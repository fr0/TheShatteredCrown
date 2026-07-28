using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// On the given signal, removes items of a def from the player's
    /// possession - pawn inventories and carried stacks on any map, loose
    /// stacks on player home maps, caravan inventories. The counterpart of
    /// QuestNode_TSC_DetectItem for fetch contracts: the client TAKES the
    /// fetched thing, or the next fetch contract of the same kind completes
    /// the moment it is accepted (the strongbox double-pay bug).
    /// </summary>
    public class QuestNode_TSC_TakeItem : QuestNode
    {
        public SlateRef<ThingDef> item;
        public SlateRef<int> count;

        [NoTranslate]
        public SlateRef<string> inSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestGen.quest.AddPart(new QuestPart_TSC_TakeItem
            {
                item = item.GetValue(slate),
                count = System.Math.Max(1, count.GetValue(slate)),
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            });
        }

        protected override bool TestRunInt(Slate slate)
        {
            return item.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_TakeItem : QuestPart
    {
        public string inSignal;
        public ThingDef item;
        public int count = 1;
        private bool taken;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || taken || item == null)
            {
                return;
            }
            taken = true;
            RemoveFromParty(item, count);
        }

        /// <summary>Removes up to count items; mirrors QuestPart_TSC_DetectItem's search scope.</summary>
        public static int RemoveFromParty(ThingDef def, int count)
        {
            int remaining = count;
            List<Thing> doomed = new List<Thing>();
            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome)
                {
                    foreach (Thing thing in map.listerThings.ThingsOfDef(def))
                    {
                        doomed.Add(thing);
                    }
                }
                foreach (Pawn pawn in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    if (pawn.carryTracker?.CarriedThing?.def == def)
                    {
                        doomed.Add(pawn.carryTracker.CarriedThing);
                    }
                    if (pawn.inventory?.innerContainer != null)
                    {
                        foreach (Thing thing in pawn.inventory.innerContainer)
                        {
                            if (thing.def == def)
                            {
                                doomed.Add(thing);
                            }
                        }
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
                    if (thing.def == def)
                    {
                        doomed.Add(thing);
                    }
                }
            }
            foreach (Thing thing in doomed)
            {
                if (remaining <= 0)
                {
                    break;
                }
                int take = System.Math.Min(remaining, thing.stackCount);
                remaining -= take;
                if (take >= thing.stackCount)
                {
                    // A pawn CARRYING this is running a job that still expects
                    // it to exist - hauling it to a pack animal, a stockpile,
                    // a caravan. Destroying it underneath the driver throws
                    // ("Can't transfer item ... because it's not here"), so
                    // end the job first and let them pick something else.
                    EndJobsCarrying(thing);
                    thing.Destroy();
                }
                else
                {
                    thing.SplitOff(take).Destroy();
                }
            }
            return count - remaining;
        }

        /// <summary>
        /// Stop any job built around this exact thing before it vanishes.
        /// Covers the carrier itself and anyone whose current job targets it
        /// (a hauler already walking toward it).
        /// </summary>
        private static void EndJobsCarrying(Thing thing)
        {
            if (thing == null)
            {
                return;
            }
            Pawn carrier = (thing.ParentHolder as Pawn_CarryTracker)?.pawn;
            if (carrier?.jobs?.curJob != null)
            {
                carrier.jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: true);
                return;
            }
            Map map = thing.MapHeld;
            if (map == null)
            {
                return;
            }
            foreach (Pawn pawn in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                Job job = pawn.jobs?.curJob;
                if (job == null)
                {
                    continue;
                }
                if (job.targetA.Thing == thing || job.targetB.Thing == thing || job.targetC.Thing == thing)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: true);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Defs.Look(ref item, "item");
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_Values.Look(ref taken, "taken");
        }
    }
}
