using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Sets a dialogue flag when its signal fires - the bridge from quest
    /// events into dialogue state (e.g. the ettersnap's acquisition unlocking
    /// Oswin's rites conversation).
    /// </summary>
    public class QuestNode_TSC_SetFlag : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> flag;

        [NoTranslate]
        public SlateRef<string> inSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_SetFlag part = new QuestPart_TSC_SetFlag
            {
                flag = flag.GetValue(slate),
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            };
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return !flag.GetValue(slate).NullOrEmpty();
        }
    }

    public class QuestPart_TSC_SetFlag : QuestPart
    {
        public string inSignal;
        public string flag;
        private bool done;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (done || signal.tag != inSignal || flag.NullOrEmpty())
            {
                return;
            }
            done = true;
            DialogueStateManager.Current.Set(flag);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref flag, "flag");
            Scribe_Values.Look(ref done, "done", defaultValue: false);
        }
    }
}
