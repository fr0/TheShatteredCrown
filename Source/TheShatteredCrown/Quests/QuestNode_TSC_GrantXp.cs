using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Grants party XP when its signal fires - place inside a quest's success
    /// path so completing the quest rewards the whole party.
    /// </summary>
    public class QuestNode_TSC_GrantXp : QuestNode
    {
        public SlateRef<int> xp;

        [NoTranslate]
        public SlateRef<string> inSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_GrantXp part = new QuestPart_TSC_GrantXp
            {
                xp = xp.GetValue(slate),
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            };
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return xp.GetValue(slate) > 0;
        }
    }

    public class QuestPart_TSC_GrantXp : QuestPart
    {
        public string inSignal;
        public int xp;
        private bool granted;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (granted || signal.tag != inSignal || xp <= 0)
            {
                return;
            }
            granted = true;
            TSC_ProgressionManager.Current.GrantXpToParty(xp, quest.name);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref xp, "xp", 0);
            Scribe_Values.Look(ref granted, "granted", defaultValue: false);
        }
    }
}
