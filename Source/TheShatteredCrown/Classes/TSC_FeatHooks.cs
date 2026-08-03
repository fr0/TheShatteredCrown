using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The starred feats: the ones that needed combat hooks rather than data.
    /// Everything here triggers off tick-state that advances in both combat
    /// modes, which is what keeps the feats identical in turn-based and real
    /// time.
    /// </summary>
    public static class TSC_FeatHooks
    {
        /// <summary>Per-pawn "not before this tick" clocks, keyed by thingID. Transient:
        /// resetting on load costs at most one early proc.</summary>
        private static readonly Dictionary<int, int> opportunityReady = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> lastStandGraceEnd = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> lastStandReady = new Dictionary<int, int>();

        public const int OpportunityCooldownTicks = 600;  // 10s, and >= a turn-based round
        public const int LastStandGraceTicks = 300;       // 5s on your feet
        public const int LastStandCooldownTicks = 600;

        public static bool OpportunityReady(Pawn pawn)
        {
            return !opportunityReady.TryGetValue(pawn.thingIDNumber, out int tick)
                || Find.TickManager.TicksGame >= tick;
        }

        public static void NoteOpportunityUsed(Pawn pawn)
        {
            opportunityReady[pawn.thingIDNumber] = Find.TickManager.TicksGame + OpportunityCooldownTicks;
        }

        /// <summary>Last Stand: true while the pawn is inside their stay-up window, starting one if it is earned.</summary>
        public static bool TryHoldUp(Pawn pawn)
        {
            int now = Find.TickManager.TicksGame;
            if (lastStandGraceEnd.TryGetValue(pawn.thingIDNumber, out int graceEnd) && now < graceEnd)
            {
                return true;
            }
            if (lastStandReady.TryGetValue(pawn.thingIDNumber, out int ready) && now < ready)
            {
                return false;
            }
            lastStandGraceEnd[pawn.thingIDNumber] = now + LastStandGraceTicks;
            lastStandReady[pawn.thingIDNumber] = now + LastStandGraceTicks + LastStandCooldownTicks;
            Messages.Message($"{pawn.LabelShortCap} refuses to fall!",
                pawn, MessageTypeDefOf.NegativeHealthEvent, historical: false);
            TSC_EncounterController.Current?.AddLog(
                $"{pawn.LabelShortCap} stays on their feet (Last Stand).",
                TSC_EncounterController.LogWorldColor);
            return true;
        }
    }

    /// <summary>
    /// Last Stand: the first time a fight would put this pawn down, it does
    /// not - for five seconds. MakeDowned is skipped during the grace; when
    /// it lapses the health tracker re-evaluates on its own and the fall
    /// happens normally if nothing has changed.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_MakeDowned_LastStand
    {
        private static readonly System.Reflection.FieldInfo PawnField =
            AccessTools.Field(typeof(Pawn_HealthTracker), "pawn");

        public static bool Prefix(Pawn_HealthTracker __instance)
        {
            if (!(PawnField?.GetValue(__instance) is Pawn pawn)
                || pawn.Dead || !pawn.Spawned
                || pawn.Faction != Faction.OfPlayer
                || !TSC_Feats.Has(pawn, "TSC_Feat_LastStand"))
            {
                return true;
            }
            return !TSC_FeatHooks.TryHoldUp(pawn);
        }
    }

    /// <summary>
    /// Opportunity Attacks and Running Start's real-time half.
    ///
    /// The component watches, every few ticks, the pawns whose feats need
    /// watching. An enemy adjacent to a warden whose next path cell leaves
    /// melee range is stepping out of reach - the warden swings while the
    /// target is still in range, which is the whole trick: detect the
    /// departure at the moment it BEGINS. A monk in motion keeps a short
    /// hediff refreshed, so the strikes right after moving come faster.
    /// </summary>
    public class MapComponent_TSC_FeatCombatHooks : MapComponent
    {
        // 5 ticks is deliberate, not an oversight: a pawn crosses a cell in
        // ~13 ticks, and the opportunity swing has to land while the leaver
        // is still inside MeleeReach - poll slower and they are gone before
        // the poll. The sweep is dictionary lookups per colonist (Feats.Has
        // against the cached progression manager), so the cadence is cheap.
        private const int Interval = 5;
        private const float MeleeReach = 1.9f;

        private static HediffDef runningStart;
        private static HediffDef RunningStartHediff =>
            runningStart ?? (runningStart = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_RunningStart"));

        private readonly List<Pawn> buffer = new List<Pawn>();

        public MapComponent_TSC_FeatCombatHooks(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            buffer.Clear();
            buffer.AddRange(map.mapPawns.FreeColonistsSpawned);
            foreach (Pawn pawn in buffer)
            {
                if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Dead || pawn.Downed)
                {
                    continue;
                }
                if (TSC_Feats.Has(pawn, "TSC_Feat_OpportunityAttacks"))
                {
                    TryOpportunityAttack(pawn);
                }
                if (TSC_Feats.Has(pawn, "TSC_Feat_RunningStart")
                    && pawn.pather != null && pawn.pather.MovingNow)
                {
                    RefreshRunningStart(pawn);
                }
            }
            buffer.Clear();
        }

        private void TryOpportunityAttack(Pawn warden)
        {
            // A warden mid-stride is not holding a line; no swings on the move.
            if (warden.pather != null && warden.pather.MovingNow)
            {
                return;
            }
            if (warden.stances?.curStance is Stance_Busy || !TSC_FeatHooks.OpportunityReady(warden))
            {
                return;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn enemy = pawns[i];
                if (enemy.Dead || enemy.Downed || !enemy.HostileTo(warden)
                    || !enemy.Position.InHorDistOf(warden.Position, MeleeReach))
                {
                    continue;
                }
                // Stepping out: moving, and the next cell is beyond reach.
                if (enemy.pather == null || !enemy.pather.MovingNow
                    || enemy.pather.nextCell.InHorDistOf(warden.Position, MeleeReach))
                {
                    continue;
                }
                if (warden.meleeVerbs?.TryMeleeAttack(enemy) ?? false)
                {
                    TSC_FeatHooks.NoteOpportunityUsed(warden);
                    MoteMaker.ThrowText(warden.DrawPos, map, "Opportunity!", new Color(0.9f, 0.75f, 0.4f));
                    TSC_EncounterController.Current?.AddLog(
                        $"{warden.LabelShortCap} strikes at {enemy.LabelShortCap} as they break away.",
                        TSC_EncounterController.LogWorldColor);
                }
                return; // one look per pulse, hit or miss
            }
        }

        private static void RefreshRunningStart(Pawn monk)
        {
            if (RunningStartHediff == null || monk.health?.hediffSet == null)
            {
                return;
            }
            Hediff existing = monk.health.hediffSet.GetFirstHediffOfDef(RunningStartHediff);
            if (existing == null)
            {
                existing = monk.health.AddHediff(RunningStartHediff);
            }
            HediffComp_Disappears disappears = (existing as HediffWithComps)?.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = 300;
            }
        }
    }
}
