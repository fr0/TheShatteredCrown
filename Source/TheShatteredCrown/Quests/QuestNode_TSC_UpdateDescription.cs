using RimWorld;
using RimWorld.QuestGen;
using Verse;
using Verse.Grammar;

namespace TheShatteredCrown
{
    /// <summary>
    /// Rewrites the quest's journal entry (Quests-tab description) when a signal
    /// fires. Vanilla descriptions are static after generation; this lets a quest
    /// read differently as its stages progress ("speak with the envoy" becomes
    /// "travel to the watchtower"). Slate $vars are substituted at generation;
    /// use \n for line breaks.
    /// </summary>
    public class QuestNode_TSC_UpdateDescription : QuestNode
    {
        public SlateRef<string> description;

        [NoTranslate]
        public SlateRef<string> inSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            string text = description.GetValue(slate);
            // Resolve [symbols] (e.g. [envoy_nameDef]) now, while the quest-gen
            // grammar context still exists; at signal time it's long gone.
            // Only invoke the resolver when symbols are present: plain text with
            // real newlines (ParseHelper converts \n during XML load) is rejected
            // by the grammar root parser ("Grammar unresolvable").
            if (text != null && text.IndexOf('[') >= 0)
            {
                try
                {
                    // The grammar parser rejects raw newlines; hand it the escaped
                    // form and restore real newlines afterward.
                    string escaped = text.Replace("\n", "\\n");
                    string resolved = QuestGenUtility.ResolveLocalTextWithDescriptionRules(new RulePack(), escaped);
                    if (!resolved.NullOrEmpty())
                    {
                        text = resolved.Replace("\\n", "\n");
                    }
                }
                catch
                {
                    // fall back to the unresolved text rather than failing generation
                }
            }
            QuestPart_TSC_UpdateDescription part = new QuestPart_TSC_UpdateDescription
            {
                description = text,
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            };
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return !description.GetValue(slate).NullOrEmpty();
        }
    }

    public class QuestPart_TSC_UpdateDescription : QuestPart
    {
        public string inSignal;
        public string description;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag == inSignal && !description.NullOrEmpty())
            {
                quest.description = description.Replace("\\n", "\n");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref description, "description");
        }
    }
}
