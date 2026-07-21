using System.Collections.Generic;
using RimWorld;
using RimWorld.QuestGen;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// Puts a quest-spawned pawn under a "visiting the colony" lord so they walk
    /// to the colony and loiter there instead of wandering off the map. Without a
    /// lord, non-player pawns on a player map have no group AI.
    /// </summary>
    public class QuestNode_TSC_VisitorLord : QuestNode
    {
        public SlateRef<Pawn> pawn;
        public SlateRef<float> durationDays;

        [NoTranslate]
        public SlateRef<string> inSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_VisitorLord part = new QuestPart_TSC_VisitorLord
            {
                pawn = pawn.GetValue(slate),
                durationTicks = (int)(durationDays.GetValue(slate) * GenDate.TicksPerDay),
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            };
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }
    }

    public class QuestPart_TSC_VisitorLord : QuestPart
    {
        public string inSignal;
        public Pawn pawn;
        public int durationTicks = 720000;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || pawn == null || !pawn.Spawned || pawn.Faction == null)
            {
                return;
            }
            pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedByQuest);
            Map map = pawn.Map;
            System.Collections.Generic.List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            IntVec3 chillSpot = colonists.Count > 0 ? colonists[0].Position : map.Center;
            LordMaker.MakeNewLord(pawn.Faction, new LordJob_VisitColony(pawn.Faction, chillSpot, durationTicks), map, Gen.YieldSingle(pawn));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref durationTicks, "durationTicks", 720000);
        }
    }
}
