using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Puts a persistent named story character (NamedNpcDef) into the quest slate,
    /// generating them on first use. Drop-in replacement for QuestNode_GeneratePawn
    /// wherever the story needs the SAME pawn every time - same name, same face.
    /// The faction is only used if the character doesn't exist yet.
    /// </summary>
    public class QuestNode_TSC_GetNamedNpc : QuestNode
    {
        public SlateRef<NamedNpcDef> npc;
        public SlateRef<Faction> faction;

        [NoTranslate]
        public SlateRef<string> storeAs;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Pawn pawn = DialogueStateManager.Current.GetOrGenerateNamedNpc(npc.GetValue(slate), faction.GetValue(slate));
            slate.Set(storeAs.GetValue(slate), pawn);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return npc.GetValue(slate) != null && !storeAs.GetValue(slate).NullOrEmpty();
        }
    }
}
