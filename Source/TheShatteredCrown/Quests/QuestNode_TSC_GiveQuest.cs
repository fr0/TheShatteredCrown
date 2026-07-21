using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Gives another quest when this node's signal fires. This is the reliable
    /// chain link between chapters: vanilla has no XML node for "quest 1 grants
    /// quest 2", so we provide one. Typically nested inside a QuestNode_Delay or
    /// QuestNode_Signal in the chapter's script.
    /// </summary>
    public class QuestNode_TSC_GiveQuest : QuestNode
    {
        public SlateRef<QuestScriptDef> questDef;

        [NoTranslate]
        public SlateRef<string> inSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_GiveQuest part = new QuestPart_TSC_GiveQuest
            {
                questDef = questDef.GetValue(slate),
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            };
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return questDef.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_GiveQuest : QuestPart
    {
        public string inSignal;
        public QuestScriptDef questDef;
        private bool given;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (given || signal.tag != inSignal || questDef == null)
            {
                return;
            }
            given = true;
            float points = StorytellerUtility.DefaultThreatPointsNow(Find.World);
            Quest newQuest = QuestUtility.GenerateQuestAndMakeAvailable(questDef, points);
            if (newQuest != null && !newQuest.hidden)
            {
                QuestUtility.SendLetterQuestAvailable(newQuest);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Defs.Look(ref questDef, "questDef");
            Scribe_Values.Look(ref given, "given", defaultValue: false);
        }
    }
}
