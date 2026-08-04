using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// Stop, drop, and roll: the encounter-mode self-extinguish. Unlike
    /// vanilla ExtinguishSelf (stand still, invisible dice), the pawn
    /// visibly rolls - cycling through facings with dust and smoke kicked
    /// up - and the flames are OUT at the end, guaranteed: a whole turn
    /// spent on the floor has earned certainty. The fire still burns them
    /// while they roll (their fire ticks on their own turn), so ignoring
    /// it early to shoot first remains a real gamble.
    /// </summary>
    public class JobDriver_TSC_BeatFlames : JobDriver
    {
        private const int RollTicks = 120;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil roll = ToilMaker.MakeToil("TSC_BeatFlames");
            roll.defaultCompleteMode = ToilCompleteMode.Delay;
            roll.defaultDuration = RollTicks;
            // Target A is the pawn themself - the progress bar needs a real
            // target index (GetTarget(None) throws and errors the job out).
            roll.WithProgressBarToilDelay(TargetIndex.A);
            roll.handlingFacing = true;
            roll.tickAction = delegate
            {
                // The roll: a quarter-turn every few ticks, dust where the
                // body hits, smoke where the flames lose.
                if (pawn.IsHashIntervalTick(12))
                {
                    pawn.Rotation = pawn.Rotation.Rotated(RotationDirection.Clockwise);
                    FleckMaker.ThrowDustPuffThick(pawn.DrawPos, pawn.Map, 1.2f,
                        new UnityEngine.Color(0.65f, 0.58f, 0.48f));
                }
                if (pawn.IsHashIntervalTick(20))
                {
                    FleckMaker.ThrowSmoke(pawn.DrawPos, pawn.Map, 0.8f);
                }
            };
            roll.AddFinishAction(delegate
            {
                // Bounded, and never destroys twice. AttachableThing only
                // takes itself off the parent's list if it HAS a parent, and
                // Thing.Destroy refuses a second destroy with an error and
                // returns - so an orphaned or already-dead fire would leave
                // this loop spinning on the same object forever. A pawn is
                // never on fire in more than a handful of places anyway.
                for (int i = 0; i < 8; i++)
                {
                    if (!(pawn.GetAttachment(ThingDefOf.Fire) is Fire fire) || fire.Destroyed)
                    {
                        break;
                    }
                    fire.Destroy();
                }
                FleckMaker.ThrowSmoke(pawn.DrawPos, pawn.Map, 1.4f);
                Messages.Message($"{pawn.LabelShortCap} beats out the flames.",
                    pawn, MessageTypeDefOf.NeutralEvent, historical: false);
            });
            yield return roll;
        }
    }
}
