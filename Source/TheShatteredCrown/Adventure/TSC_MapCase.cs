using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// The weathered map case finally keeps its promise.
    ///
    /// It has sat in the watchtower vault since the old intro, sealed with
    /// the border guard's sigil and looking for all the world like a quest
    /// objective - and since the intro rework, nothing so much as glanced
    /// at it. A border-guard's case should hold a border-guard's survey:
    /// break the seal and one of the wilderness discoveries is inked onto
    /// the party's map, a few days' ride out. The intro location now hands
    /// every new game its first treasure map, and the discovery loop gets
    /// a second door in.
    ///
    /// Reading can fail honestly - a world with no reachable tile in range
    /// keeps the case sealed for another road - and the case is spent only
    /// when the map actually marks.
    /// </summary>
    public class FloatMenuOptionProvider_TSC_MapCase : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            if (clickedThing.def.defName != "TSC_WeatheredMapCase")
            {
                yield break;
            }
            Pawn actor = context.FirstSelectedPawn;
            if (actor == null)
            {
                yield break;
            }
            if (!actor.CanReach(clickedThing, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption("Cannot reach the map case: no path", null);
                yield break;
            }
            Thing localThing = clickedThing;
            yield return new FloatMenuOption("Break the seal and read the survey", () =>
            {
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamedSilentFail("TSC_ReadMapCase"), localThing);
                actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
        }
    }

    public class JobDriver_TSC_ReadMapCase : JobDriver
    {
        private const int ReadTicks = 180;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil read = Toils_General.Wait(ReadTicks, TargetIndex.A);
            read.WithProgressBarToilDelay(TargetIndex.A);
            yield return read;
            Toil unseal = ToilMaker.MakeToil("ReadMapCase");
            unseal.initAction = () =>
            {
                Thing thing = job.targetA.Thing;
                TSC_DiscoveryManager discoveries = Find.World?.GetComponent<TSC_DiscoveryManager>();
                bool marked = discoveries != null && discoveries.TryDiscoverNear(
                    pawn.Map.Tile, 3, 8,
                    "The border survey",
                    "The seal cracks and the case gives up a border-guard's survey, older than anyone "
                    + "riding today. Most of it is roads that no longer exist. One location is inked and "
                    + "circled: {0}.\n\n{1}\n\nIt is marked on the party's map. The mark fades in about "
                    + "two weeks; the country it names has waited longer.");
                if (marked)
                {
                    thing.Destroy();
                    return;
                }
                // Nothing in reach of here: the case stays sealed and says
                // so, rather than dissolving into nothing.
                Messages.Message(
                    $"{pawn.LabelShortCap} studies the case unopened: whatever country it maps, "
                    + "it does not lie within reach of this place. Perhaps from another road.",
                    pawn, MessageTypeDefOf.NeutralEvent, historical: false);
            };
            unseal.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return unseal;
        }
    }
}
