using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// Physical proficiency interactions:
    /// - Mosskeeper's chest: [Thievery] pick it quietly (fail wakes the nests)
    ///   or [Athletics] break it open (always loud; fail = the lid holds).
    /// - Campfire: [Performance] a fireside tale inspiring nearby colonists.
    /// The ACTOR rolls their own proficiency here - hands and voice, not party assist.
    /// </summary>
    public static class TSC_CheckUtility
    {
        public static bool Roll(Pawn pawn, TSC_ProficiencyDef prof, int dc, out string line)
        {
            int roll = Rand.RangeInclusive(1, 10);
            int bonus = TSC_ProgressionManager.Current.EffectiveProficiency(pawn, prof);
            bool success = roll + bonus >= dc;
            line = $"{prof.LabelCap} check ({pawn.LabelShortCap}): {roll} + {bonus} = {roll + bonus} vs {dc}: {(success ? "Success!" : "Failure")}";
            return success;
        }
    }

    public class FloatMenuOptionProvider_TSC_Chest : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            if (clickedThing.def != TSC_DefOf.TSC_MossCache)
            {
                yield break;
            }
            if (!(clickedThing is IOpenable openable) || !openable.CanOpen)
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
                yield return new FloatMenuOption("Cannot reach the chest: no path", null);
                yield break;
            }
            Thing localThing = clickedThing;
            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                "[Thievery] Pick the chest's lock quietly", delegate
                {
                    actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(TSC_DefOf.TSC_PickChest, localThing), JobTag.Misc);
                }), actor, clickedThing);
            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                "[Athletics] Break the chest open", delegate
                {
                    actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(TSC_DefOf.TSC_ForceChest, localThing), JobTag.Misc);
                }), actor, clickedThing);
        }
    }

    public class JobDriver_TSC_OpenChest : JobDriver
    {
        private Thing Chest => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Chest, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil open = ToilMaker.MakeToil("TSC_OpenChest");
            open.initAction = delegate
            {
                Thing chest = Chest;
                if (!(chest is IOpenable openable) || !openable.CanOpen)
                {
                    return;
                }
                bool thievery = job.def == TSC_DefOf.TSC_PickChest;
                TSC_ProficiencyDef prof = thievery ? TSC_DefOf.TSC_Prof_Thievery : TSC_DefOf.TSC_Prof_Athletics;
                int dc = thievery ? 8 : 7;
                bool success = TSC_CheckUtility.Roll(pawn, prof, dc, out string line);

                if (thievery)
                {
                    if (success)
                    {
                        openable.Open();
                        Messages.Message($"{line}\nThe lock gives without a sound; nothing in the dark stirs.", chest, MessageTypeDefOf.PositiveEvent, historical: false);
                    }
                    else
                    {
                        openable.Open();
                        WakeDormant(chest);
                        Messages.Message($"{line}\nThe pick slips; the lid screeches open, and the dark answers.", chest, MessageTypeDefOf.ThreatSmall, historical: false);
                    }
                }
                else
                {
                    if (success)
                    {
                        openable.Open();
                        WakeDormant(chest);
                        Messages.Message($"{line}\nThe lid gives with a crack that echoes off every wall. Loud, but open.", chest, MessageTypeDefOf.CautionInput, historical: false);
                    }
                    else
                    {
                        Messages.Message($"{line}\nThe lid holds. Old kingdom joinery has opinions about brute force.", chest, MessageTypeDefOf.NeutralEvent, historical: false);
                    }
                }
            };
            open.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return open;
        }

        private static void WakeDormant(Thing around)
        {
            Map map = around.Map;
            if (map == null)
            {
                return;
            }
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!pawn.Position.InHorDistOf(around.Position, 30f))
                {
                    continue;
                }
                CompCanBeDormant dormant = pawn.TryGetComp<CompCanBeDormant>();
                if (dormant != null && !dormant.Awake)
                {
                    dormant.WakeUp();
                }
            }
        }
    }

    public class FloatMenuOptionProvider_TSC_Campfire : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            if (clickedThing.def != ThingDefOf.Campfire || !TSC_RpgMode.Active)
            {
                return null;
            }
            Pawn actor = context.FirstSelectedPawn;
            if (actor == null || !actor.IsFreeColonist)
            {
                return null;
            }
            if (actor.health.hediffSet.GetFirstHediffOfDef(TSC_DefOf.TSC_Hediff_Performed) != null)
            {
                return new FloatMenuOption("Perform by the fire (performed recently)", null);
            }
            if (!actor.CanReach(clickedThing, PathEndMode.Touch, Danger.Deadly))
            {
                return null;
            }
            Thing localThing = clickedThing;
            FloatMenuOption option = new FloatMenuOption("[Performance] Tell a tale by the fire", delegate
            {
                actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(TSC_DefOf.TSC_Perform, localThing), JobTag.Misc);
            });
            return FloatMenuUtility.DecoratePrioritizedTask(option, actor, clickedThing);
        }
    }

    public class JobDriver_TSC_Perform : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil perform = ToilMaker.MakeToil("TSC_Perform");
            perform.initAction = delegate
            {
                bool success = TSC_CheckUtility.Roll(pawn, TSC_DefOf.TSC_Prof_Performance, 7, out string line);
                pawn.health.AddHediff(TSC_DefOf.TSC_Hediff_Performed);
                if (success)
                {
                    int inspired = 0;
                    foreach (Pawn colonist in pawn.Map.mapPawns.FreeColonistsSpawned)
                    {
                        if (colonist == pawn || !colonist.Position.InHorDistOf(pawn.Position, 9.9f))
                        {
                            continue;
                        }
                        colonist.health.AddHediff(HediffDef.Named("TSC_Hediff_InspiredVerse"));
                        inspired++;
                    }
                    SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
                    Messages.Message($"{line}\nThe tale lands. {inspired} listener(s) walk away inspired.", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
                }
                else
                {
                    Messages.Message($"{line}\nThe tale wanders, loses its ending, and dies by the fire. The silence is polite.", pawn, MessageTypeDefOf.NeutralEvent, historical: false);
                }
            };
            perform.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return perform;
        }
    }
}
