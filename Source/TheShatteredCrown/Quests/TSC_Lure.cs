using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// "Lure the Ettersnap": right-click option on a wild Ettersnap. Requires
    /// remembrance moss (carried by the colonist or lying on the map); the
    /// colonist walks up, the moss is consumed, and the creature joins the
    /// player. Without moss the option shows disabled - vanilla taming remains
    /// the hard fallback.
    /// </summary>
    public class FloatMenuOptionProvider_TSC_Lure : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            if (clickedPawn.kindDef != TSC_DefOf.TSC_Ettersnap || clickedPawn.Faction != null || clickedPawn.Dead)
            {
                return null;
            }
            Pawn talker = context.FirstSelectedPawn;
            if (talker == null)
            {
                return null;
            }
            Thing moss = FindMoss(talker);
            if (moss == null)
            {
                return new FloatMenuOption("Lure the ettersnap (requires remembrance moss)", null);
            }
            if (!talker.CanReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
            {
                return new FloatMenuOption("Cannot lure the ettersnap: no path", null);
            }
            FloatMenuOption option = new FloatMenuOption("Lure the ettersnap (consumes remembrance moss)", delegate
            {
                Job job = JobMaker.MakeJob(TSC_DefOf.TSC_LureEttersnap, clickedPawn, moss);
                talker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
            return FloatMenuUtility.DecoratePrioritizedTask(option, talker, clickedPawn);
        }

        public static Thing FindMoss(Pawn talker)
        {
            ThingDef mossDef = TSC_DefOf.TSC_RemembranceMoss;
            if (talker.inventory != null)
            {
                foreach (Thing thing in talker.inventory.innerContainer)
                {
                    if (thing.def == mossDef)
                    {
                        return thing;
                    }
                }
            }
            Map map = talker.MapHeld;
            if (map != null)
            {
                List<Thing> onMap = map.listerThings.ThingsOfDef(mossDef);
                for (int i = 0; i < onMap.Count; i++)
                {
                    if (!onMap[i].IsForbidden(talker))
                    {
                        return onMap[i];
                    }
                }
            }
            return null;
        }
    }

    public class JobDriver_TSC_Lure : JobDriver
    {
        private Pawn Target => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil lure = ToilMaker.MakeToil("TSC_LureEttersnap");
            lure.initAction = delegate
            {
                Pawn target = Target;
                if (target.Faction != null || target.Dead)
                {
                    return;
                }
                Thing moss = job.GetTarget(TargetIndex.B).Thing;
                if (moss == null || moss.Destroyed)
                {
                    moss = FloatMenuOptionProvider_TSC_Lure.FindMoss(pawn);
                }
                if (moss == null)
                {
                    Messages.Message("The remembrance moss is gone.", pawn, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                Thing consumed = moss.SplitOff(1);
                consumed.Destroy();
                target.SetFaction(Faction.OfPlayer);
                Messages.Message(
                    $"The ettersnap tastes the remembrance moss and follows {pawn.LabelShortCap}. It is yours now.",
                    target, MessageTypeDefOf.PositiveEvent, historical: false);
            };
            lure.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return lure;
        }
    }
}
