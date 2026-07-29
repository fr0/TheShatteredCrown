using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>Put on a PawnKindDef: pawns of that kind spawn with a class (and its abilities/energy pool).</summary>
    public class TSC_ClassExtension : DefModExtension
    {
        public TSC_ClassDef classDef;
        public int levels = 1;
    }

    /// <summary>
    /// Put on an AbilityDef: how the enemy caster AI should aim it. Without
    /// a hint the AI aims at the party (the hexer's Scorch); with
    /// targetAllies it aims at the caster's OWN side - the shaman's kit.
    /// </summary>
    public class TSC_AiCastHint : DefModExtension
    {
        /// <summary>Aim at the caster's own faction (self included) instead of the enemy.</summary>
        public bool targetAllies;
        /// <summary>Only worthwhile on the wounded (heals): most-hurt ally first.</summary>
        public bool requiresInjury;
        /// <summary>Skip allies already carrying this hediff (buffs don't stack).</summary>
        public HediffDef skipIfHediff;
    }

    /// <summary>Seeds classes onto generated pawns whose kind carries TSC_ClassExtension (e.g. the bandit hexer).</summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    public static class Patch_GeneratePawn_SeedClass
    {
        public static void Postfix(Pawn __result, PawnGenerationRequest request)
        {
            TSC_ClassExtension ext = request.KindDef?.GetModExtension<TSC_ClassExtension>();
            if (ext?.classDef == null || __result == null || Verse.Current.Game == null)
            {
                return;
            }
            TSC_ProgressionManager manager = TSC_ProgressionManager.Current;
            if (manager == null)
            {
                return;
            }
            manager.LearnClass(__result, ext.classDef, announce: false);
            for (int i = 1; i < ext.levels; i++)
            {
                manager.DebugAddClassLevel(__result, ext.classDef, announce: false);
            }
        }
    }

    /// <summary>
    /// Caster AI: hostile pawns with aiCanUse abilities cast them at player
    /// pawns in range with line of sight. Deliberately self-contained (no
    /// dependence on vanilla think trees): a periodic scan queues the vanilla
    /// cast job, which then pays Energy and - in turn-based - AP through the
    /// existing plumbing. In turn-based, only the ACTIVE pawn may cast.
    /// </summary>
    public class MapComponent_TSC_EnemyCasterAI : MapComponent
    {
        // Must fit inside a turn's 45-tick idle grace or casters idle out.
        private const int IntervalTicks = 30;

        public MapComponent_TSC_EnemyCasterAI(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % IntervalTicks != 7)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            bool turnBased = ctrl != null && ctrl.ActiveOn(map) && !ctrl.ApproachMode;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn caster = pawns[i];
                if (caster.Dead || caster.Downed || caster.Faction == Faction.OfPlayer
                    || !caster.HostileTo(Faction.OfPlayer)
                    || caster.abilities == null || caster.abilities.abilities.Count == 0
                    || !caster.Awake() || caster.InMentalState)
                {
                    continue;
                }
                if (turnBased && caster != ctrl.ActivePawn)
                {
                    continue; // frozen pawns don't act
                }
                if (caster.CurJobDef == JobDefOf.CastAbilityOnThing)
                {
                    continue; // already casting
                }
                TryCast(caster, turnBased, ctrl);
            }
        }

        private void TryCast(Pawn caster, bool turnBased, TSC_EncounterController ctrl)
        {
            List<Ability> abilities = caster.abilities.abilities;
            for (int i = 0; i < abilities.Count; i++)
            {
                Ability ability = abilities[i];
                if (!ability.def.aiCanUse || !ability.CanCast || ability.verb == null)
                {
                    continue;
                }
                if (turnBased && ctrl.ApOf(caster) < TSC_EncounterController.ActionApCost)
                {
                    return; // no AP for any cast this turn
                }
                if (!HasEnergyFor(caster, ability.def))
                {
                    continue;
                }
                Pawn target = FindTarget(caster, ability);
                if (target != null)
                {
                    ability.QueueCastingJob(target, LocalTargetInfo.Invalid);
                    return;
                }
            }
        }

        private static bool HasEnergyFor(Pawn caster, AbilityDef def)
        {
            if (def.comps == null)
            {
                return true;
            }
            for (int i = 0; i < def.comps.Count; i++)
            {
                if (def.comps[i] is CompProperties_TSC_EnergyCost energy)
                {
                    return TSC_ProgressionManager.Current.EnergyOf(caster) >= energy.cost;
                }
            }
            return true;
        }

        private Pawn FindTarget(Pawn caster, Ability ability)
        {
            float range = ability.verb.verbProps?.range ?? 0f;
            if (range <= 0f)
            {
                return null;
            }
            TSC_AiCastHint hint = ability.def.GetModExtension<TSC_AiCastHint>();
            if (hint != null && hint.targetAllies)
            {
                return FindAllyTarget(caster, range, hint);
            }
            Pawn best = null;
            float bestDist = float.MaxValue;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn candidate = pawns[i];
                if (candidate.Dead || candidate.Downed || !candidate.HostileTo(caster)
                    || !candidate.Position.InHorDistOf(caster.Position, range)
                    || !GenSight.LineOfSight(caster.Position, candidate.Position, map, skipFirstCell: true))
                {
                    continue;
                }
                float dist = (candidate.Position - caster.Position).LengthHorizontalSquared;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>
        /// The shaman's aim: own-faction pawns (self included), most-hurt
        /// first for heals, unbuffed-nearest for buffs. Downed allies still
        /// count - dragging a brigand back from the edge is the whole job.
        /// </summary>
        private Pawn FindAllyTarget(Pawn caster, float range, TSC_AiCastHint hint)
        {
            Pawn best = null;
            float bestScore = float.MaxValue;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn candidate = pawns[i];
                if (candidate.Dead || candidate.Faction != caster.Faction
                    || !candidate.Position.InHorDistOf(caster.Position, range)
                    || !GenSight.LineOfSight(caster.Position, candidate.Position, map, skipFirstCell: true))
                {
                    continue;
                }
                if (hint.skipIfHediff != null
                    && candidate.health.hediffSet.HasHediff(hint.skipIfHediff))
                {
                    continue;
                }
                float score;
                if (hint.requiresInjury)
                {
                    float health = candidate.health.summaryHealth.SummaryHealthPercent;
                    if (health >= 0.95f && candidate.health.hediffSet.BleedRateTotal <= 0f)
                    {
                        continue; // not hurt enough to spend the cast on
                    }
                    score = health; // most hurt wins
                }
                else
                {
                    score = (candidate.Position - caster.Position).LengthHorizontalSquared;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }
    }
}
