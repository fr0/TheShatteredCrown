using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Completes (running its inner node) when any pawn of the given kind belongs
    /// to the player - on any map or in a caravan. The "capture the creature"
    /// primitive: used by the Ettersnap hunt.
    /// </summary>
    public class QuestNode_TSC_DetectPawnAcquired : QuestNode
    {
        public SlateRef<PawnKindDef> kind;
        public QuestNode node;

        [NoTranslate]
        public SlateRef<string> inSignalEnable;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_DetectPawnAcquired part = new QuestPart_TSC_DetectPawnAcquired
            {
                kind = kind.GetValue(slate),
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
            return kind.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_DetectPawnAcquired : QuestPartActivable
    {
        public PawnKindDef kind;

        private const int CheckInterval = 250;

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (kind == null || Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
                {
                    if (pawn.kindDef == kind && !pawn.Dead)
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
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    if (pawn.kindDef == kind && !pawn.Dead)
                    {
                        Complete();
                        return;
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref kind, "kind");
        }
    }
}
