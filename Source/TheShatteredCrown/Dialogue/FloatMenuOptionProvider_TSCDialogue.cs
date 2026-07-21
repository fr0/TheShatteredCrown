using RimWorld;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// Adds a "Talk to X" right-click option on any pawn whose PawnKindDef carries
    /// a DialogueExtension. Uses the 1.6 FloatMenuOptionProvider system, which
    /// discovers subclasses automatically - no Harmony needed. The option issues a
    /// walk-up job; the conversation window opens when the colonist reaches them.
    /// </summary>
    public class FloatMenuOptionProvider_TSCDialogue : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            DialogueExtension ext = clickedPawn.kindDef?.GetModExtension<DialogueExtension>();
            if (ext?.dialogue == null)
            {
                return null;
            }
            if (clickedPawn.Dead || clickedPawn.Downed || clickedPawn.HostileTo(Faction.OfPlayer))
            {
                return null;
            }
            Pawn talker = context.FirstSelectedPawn;
            if (talker == null || talker == clickedPawn)
            {
                return null;
            }
            if (!talker.CanReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
            {
                return new FloatMenuOption($"Cannot talk to {clickedPawn.LabelShort}: no path", null);
            }
            FloatMenuOption option = new FloatMenuOption($"Talk to {clickedPawn.LabelShort}", delegate
            {
                Job job = JobMaker.MakeJob(TSC_DefOf.TSC_TalkTo, clickedPawn);
                talker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
            return FloatMenuUtility.DecoratePrioritizedTask(option, talker, clickedPawn);
        }
    }
}
