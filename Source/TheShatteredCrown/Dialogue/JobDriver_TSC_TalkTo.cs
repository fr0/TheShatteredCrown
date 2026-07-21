using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// Walk to the target pawn, then open their conversation window. The window
    /// pauses the game (forcePause), so the conversation happens "in the moment"
    /// the colonist reaches them.
    /// </summary>
    public class JobDriver_TSC_TalkTo : JobDriver
    {
        private Pawn Npc => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnDowned(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil talk = ToilMaker.MakeToil("TSC_OpenConversation");
            talk.initAction = delegate
            {
                Pawn npc = Npc;
                DialogueExtension ext = npc.kindDef?.GetModExtension<DialogueExtension>();
                if (ext?.dialogue != null)
                {
                    pawn.rotationTracker.FaceTarget(npc);
                    npc.rotationTracker.FaceTarget(pawn);
                    Find.WindowStack.Add(new Dialog_Conversation(ext.dialogue, npc, pawn));
                }
            };
            talk.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return talk;
        }
    }
}
