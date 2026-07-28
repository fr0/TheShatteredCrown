using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Branches on a dialogue flag WHEN A SIGNAL FIRES, not at generation.
    ///
    /// The distinction is the whole point. Vanilla's QuestNode_Condition-style
    /// nodes are evaluated while the quest is being written, which is long
    /// before the player has done anything - so they cannot express "if they
    /// found the ledger". This part sits in the running quest and reads the
    /// flag at the moment the signal arrives, which is what makes a contract
    /// able to end differently depending on what happened inside it.
    ///
    /// Pair it with CompProperties_TSC_CheckSpot.successFlag (a spot records a
    /// discovery) or DialogueEffect_SetFlag (a conversation records a choice).
    /// </summary>
    public class QuestNode_TSC_IfFlag : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> flag;

        [NoTranslate]
        public SlateRef<string> inSignal;

        /// <summary>Runs when the flag IS set at signal time.</summary>
        public QuestNode node;

        /// <summary>Runs when it is not. Optional - leave it out for a plain gate.</summary>
        public QuestNode elseNode;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_IfFlag part = new QuestPart_TSC_IfFlag
            {
                flag = flag.GetValue(slate),
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            };
            // Both branches are BUILT now and gated at run time by which
            // out-signal the part fires; quest parts cannot be constructed
            // lazily once the quest is live.
            part.outSignalTrue = QuestGen.GenerateNewSignal("TSC_IfFlagTrue");
            part.outSignalFalse = QuestGen.GenerateNewSignal("TSC_IfFlagFalse");
            if (node != null)
            {
                QuestGenUtility.RunInnerNode(node, part.outSignalTrue);
            }
            if (elseNode != null)
            {
                QuestGenUtility.RunInnerNode(elseNode, part.outSignalFalse);
            }
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            if (node != null)
            {
                node.TestRun(slate);
            }
            if (elseNode != null)
            {
                elseNode.TestRun(slate);
            }
            return true;
        }
    }

    public class QuestPart_TSC_IfFlag : QuestPart
    {
        public string flag;
        public string inSignal;
        public string outSignalTrue;
        public string outSignalFalse;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || flag.NullOrEmpty())
            {
                return;
            }
            bool set = DialogueStateManager.Current != null && DialogueStateManager.Current.IsSet(flag);
            string outSignal = set ? outSignalTrue : outSignalFalse;
            if (!outSignal.NullOrEmpty())
            {
                Find.SignalManager.SendSignal(new Signal(outSignal, signal.args));
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref flag, "flag");
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref outSignalTrue, "outSignalTrue");
            Scribe_Values.Look(ref outSignalFalse, "outSignalFalse");
        }
    }
}
