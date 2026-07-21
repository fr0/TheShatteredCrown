using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// A companion seeks out the target pawn to start a conversation of their
    /// own. The dialogue to open is held by DialogueStateManager as a pending
    /// initiation; it is consumed (and marked fired) only if the walk-up
    /// actually completes, so interruptions simply retry later.
    /// </summary>
    public class JobDriver_TSC_InitiateTalk : JobDriver
    {
        private Pawn Target => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnDowned(TargetIndex.A);
            AddFinishAction(delegate(JobCondition condition)
            {
                if (condition != JobCondition.Succeeded)
                {
                    DialogueStateManager.Current.ConsumePendingInitiation(pawn, arrived: false);
                }
            });
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil talk = ToilMaker.MakeToil("TSC_OpenInitiatedConversation");
            talk.initAction = delegate
            {
                DialogueDef dialogue = DialogueStateManager.Current.ConsumePendingInitiation(pawn, arrived: true);
                if (dialogue != null)
                {
                    Pawn target = Target;
                    pawn.rotationTracker.FaceTarget(target);
                    target.rotationTracker.FaceTarget(pawn);
                    Find.WindowStack.Add(new Dialog_Conversation(dialogue, pawn, target));
                }
            };
            talk.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return talk;
        }
    }
}
