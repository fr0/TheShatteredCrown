using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Runs its inner node when a persistent named character is dead - however
    /// they were spawned (quest part, site genstep, or as a recruited colonist).
    /// The death fail-safe primitive: lets quests end informatively instead of
    /// stranding when a critical character is killed.
    /// </summary>
    public class QuestNode_TSC_OnNpcDeath : QuestNode
    {
        public SlateRef<NamedNpcDef> npc;
        public QuestNode node;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_OnNpcDeath part = new QuestPart_TSC_OnNpcDeath
            {
                npcDef = npc.GetValue(slate),
                inSignalEnable = slate.Get<string>("inSignal"),
            };
            QuestGen.quest.AddPart(part);
            if (node != null)
            {
                QuestGenUtility.RunInnerNode(node, part.OutSignalCompleted);
            }
        }

        protected override bool TestRunInt(Slate slate)
        {
            return npc.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_OnNpcDeath : QuestPartActivable
    {
        public NamedNpcDef npcDef;

        private const int CheckInterval = 250;

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (npcDef == null || Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }
            // Dead only: a Destroyed-but-living pawn just had their site despawn
            // under them, which must not read as a death.
            Pawn pawn = DialogueStateManager.Current.GetNamedNpcIfExists(npcDef);
            if (pawn != null && pawn.Dead)
            {
                Complete();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref npcDef, "npcDef");
        }
    }
}
