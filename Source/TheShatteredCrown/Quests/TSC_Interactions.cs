using System.Collections.Generic;
using RimWorld;
using UnityEngine;
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
        /// <summary>
        /// DCs drift up as the party grows. Proficiencies climb every level,
        /// so a fixed DC that read as a real gamble at level 1 becomes a
        /// formality by level 10 and the whole system stops mattering. The
        /// scaling is DELIBERATELY gentler than proficiency growth: the
        /// party still gets meaningfully better at what it trained for, it
        /// just never stops rolling.
        ///
        /// +1 DC per 3 levels of the party's best-in-that-skill member,
        /// capped at +4 so nothing becomes unreachable, and never applied
        /// below level 3 (the opening hours play exactly as authored).
        ///
        /// The cap stays at 4 on purpose. Raising it to 8 was measured and
        /// reverted: it cannot touch a specialist (proficiency 23 against a
        /// DC-7 check clears even on a natural 1, at any cap), and since
        /// the scaling reads the PARTY'S BEST value, the only thing it
        /// actually did was tax every other pawn for the specialist's
        /// excellence - a focused pawn went 90% -> 70%, a generalist
        /// 50% -> 30%. What gates an expert is an authored DC, not a
        /// scaling constant, so the late acts carry high base DCs instead.
        /// </summary>
        private const int LevelsPerStep = 3;
        private const int MaxScaling = 4;
        private const int FreeLevels = 2;

        public static int ScaledDc(Pawn pawn, TSC_ProficiencyDef prof, int baseDc)
        {
            int level = PartyLevelFor(prof);
            if (level <= FreeLevels)
            {
                return baseDc;
            }
            int step = Mathf.Min(MaxScaling, (level - FreeLevels) / LevelsPerStep);
            return baseDc + step;
        }

        /// <summary>
        /// The party's ceiling in this proficiency, not the roller's: the
        /// world reacts to how capable the COMPANY has become, so sending
        /// your worst reader at a Lore check is still a bad idea, but it
        /// does not dodge the scaling either.
        /// </summary>
        private static int PartyLevelFor(TSC_ProficiencyDef prof)
        {
            if (Verse.Current.Game == null || prof == null)
            {
                return 0;
            }
            int best = 0;
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                best = Mathf.Max(best, TSC_ProgressionManager.Current.EffectiveProficiency(pawn, prof));
            }
            return best;
        }

        public static bool Roll(Pawn pawn, TSC_ProficiencyDef prof, int dc, out string line)
        {
            int scaled = ScaledDc(pawn, prof, dc);
            int roll = Rand.RangeInclusive(1, 10);
            int bonus = TSC_ProgressionManager.Current.EffectiveProficiency(pawn, prof);
            bool success = roll + bonus >= scaled;
            string drift = scaled != dc ? $" (DC {dc}+{scaled - dc})" : "";
            line = $"{prof.LabelCap} check ({pawn.LabelShortCap}): {roll} + {bonus} = {roll + bonus} vs {scaled}{drift}: {(success ? "Success!" : "Failure")}";
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
            if (clickedThing.def != TSC_DefOf.TSC_MossCache && clickedThing.def != TSC_DefOf.TSC_CellarHatch)
            {
                yield break;
            }
            // The hatch is a MapPortal, not an openable crate: only the crates
            // need the IOpenable gate (this check ahead of the hatch branch
            // silently removed the pick option after the portal conversion).
            if (clickedThing.def == TSC_DefOf.TSC_MossCache
                && (!(clickedThing is IOpenable openable) || !openable.CanOpen))
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
            if (clickedThing.def == TSC_DefOf.TSC_CellarHatch)
            {
                Building_TSC_CellarHatch hatch = clickedThing as Building_TSC_CellarHatch;
                if (hatch != null && hatch.Unlocked)
                {
                    yield break; // unlocked: the portal's own Enter option takes over
                }
                if (hatch != null && hatch.AttemptSpent)
                {
                    yield return new FloatMenuOption(
                        "Cannot pick the lock: it has taken the party's measure", null);
                    yield break;
                }
                // Druid-made lock: finesse or nothing (no Athletics option),
                // and ONE attempt EVER - choose your picker well.
                yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                    "[Thievery] Pick the cellar lock (one attempt only)", delegate
                    {
                        actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(TSC_DefOf.TSC_PickChest, localThing), JobTag.Misc);
                    }), actor, clickedThing);
                yield break;
            }
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

                // Maewyn's cellar hatch: not an openable crate but a locked
                // MAP PORTAL. Pick-only; success UNLOCKS the way down (failure
                // just holds, retry allowed), and a successful pick has a
                // witness if Maewyn is in the party.
                if (chest is Building_TSC_CellarHatch hatch)
                {
                    if (hatch.Unlocked || hatch.AttemptSpent)
                    {
                        return;
                    }
                    bool picked = TSC_CheckUtility.Roll(pawn, TSC_DefOf.TSC_Prof_Thievery, 8, out string pickLine);
                    if (picked)
                    {
                        hatch.Unlock();
                        Messages.Message($"{pickLine}\nThe druid-made lock yields, grudgingly. Cold cellar air breathes up through the hatch.",
                            chest, MessageTypeDefOf.PositiveEvent, historical: false);
                        NoteCellarTheft(pawn);
                    }
                    else
                    {
                        hatch.NoteFailed();
                        Messages.Message($"{pickLine}\nThe pick snaps off something that is not quite metal. The lock holds, and it will not be surprised twice.",
                            chest, MessageTypeDefOf.NeutralEvent, historical: false);
                    }
                    return;
                }

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

        /// <summary>
        /// Robbing Maewyn's cellar with Maewyn in the party: she knows her own
        /// lock's voice. The -10 lands unconditionally; her confrontation opens
        /// as a REAL conversation with the picker (closing it unanswered leaves
        /// the "About your cellar..." follow-up available in her hub).
        /// </summary>
        private static void NoteCellarTheft(Pawn picker)
        {
            NamedNpcDef def = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Maewyn");
            DialogueStateManager state = DialogueStateManager.Current;
            Pawn maewyn = state.GetNamedNpcIfExists(def);
            if (def == null || maewyn == null || maewyn.Dead || maewyn.Faction != Faction.OfPlayer)
            {
                return;
            }
            state.Set("TSC_MaewynCellarTheft");
            state.ChangeAffinity(def, -10);
            DialogueDef confrontation = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_Maewyn_CellarTheft");
            if (confrontation != null)
            {
                Find.WindowStack.Add(new Dialog_Conversation(confrontation, maewyn, picker));
            }
            else
            {
                Messages.Message("Maewyn: \"Thirty years that hatch kept itself shut, and I know my own lock's voice, rider.\"",
                    maewyn, MessageTypeDefOf.NegativeEvent, historical: false);
            }
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

    /// <summary>
    /// The camp crates are the company's ACTIVE stores: opening one uninvited
    /// while Serra lives is theft. She calls it out once, and each opened
    /// crate costs -5 affinity - unless she has offered supplies (the
    /// TSC_Serra_Supplies dialogue flag makes it sanctioned).
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Building_Crate), nameof(Building_Crate.Open))]
    public static class Patch_CampCrate_Theft
    {
        public static void Postfix(Building_Crate __instance)
        {
            if (__instance.def != TSC_DefOf.TSC_CampCrate_Provisions
                && __instance.def != TSC_DefOf.TSC_CampCrate_Sundries)
            {
                return;
            }
            DialogueStateManager state = DialogueStateManager.Current;
            if (state.IsSet("TSC_Serra_Supplies"))
            {
                return; // she offered: sanctioned
            }
            NamedNpcDef serraDef = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Serra");
            Pawn serra = serraDef != null ? state.GetNamedNpcIfExists(serraDef) : null;
            if (serra == null || serra.Dead)
            {
                return; // nobody left to object
            }
            if (!state.IsSet("TSC_SerraCrateCalledOut"))
            {
                state.Set("TSC_SerraCrateCalledOut");
                Messages.Message("Serra: \"Those are my company's stores, courier. In this pass, that rope is the difference between wintering and starving. ASK next time.\"",
                    serra, MessageTypeDefOf.NegativeEvent, historical: false);
            }
            state.ChangeAffinity(serraDef, -5);
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
