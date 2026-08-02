using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Universal spell scaling: every spell's effect grows +15% per level in
    /// the class that grants it (level 5 = x1.6). The granting class is
    /// resolved from the class defs' unlock tables, so no per-ability
    /// annotation is needed. MAGNITUDE scales, never duration (user
    /// decision): damage, healing, weapon-strike multipliers, hediff stat
    /// strengths (via TSC_Hediff_Leveled, severity = the factor), Charged
    /// Shot's damage bonus, and Song of Rest's energy restore. Magic Missile
    /// opts out (its explicit +2 damage/level curve is steeper). Vanilla
    /// hediffs (PsychicInvisibility, PsychicShock), teleport ranges, and the
    /// binary cleanses stay unscaled.
    /// </summary>
    public static class TSC_SpellScaling
    {
        public const float PerLevel = 0.15f;

        private static Dictionary<AbilityDef, TSC_ClassDef> classByAbility;

        /// <summary>The class whose unlock table grants this ability, or null for non-class abilities (Shardfall, vanilla).</summary>
        public static TSC_ClassDef GrantingClass(AbilityDef ability)
        {
            if (ability == null)
            {
                return null;
            }
            if (classByAbility == null)
            {
                classByAbility = new Dictionary<AbilityDef, TSC_ClassDef>();
                foreach (TSC_ClassDef classDef in DefDatabase<TSC_ClassDef>.AllDefsListForReading)
                {
                    foreach (TSC_ClassUnlock unlock in classDef.unlocks)
                    {
                        if (unlock.ability != null && !classByAbility.ContainsKey(unlock.ability))
                        {
                            classByAbility[unlock.ability] = classDef;
                        }
                    }
                }
            }
            classByAbility.TryGetValue(ability, out TSC_ClassDef grantingClass);
            return grantingClass;
        }

        public static int CasterLevel(Pawn pawn, AbilityDef ability)
        {
            TSC_ClassDef grantingClass = GrantingClass(ability);
            if (pawn == null || grantingClass == null)
            {
                return 1;
            }
            TSC_ClassRecord record = TSC_ProgressionManager.Current.RecordOf(pawn);
            int index = record.classes.IndexOf(grantingClass);
            return index >= 0 ? Mathf.Max(1, record.levels[index]) : 1;
        }

        /// <summary>
        /// Level scaling, times whatever the caster is holding. The
        /// instrument term is 1 for everyone who is not a bard with a lute
        /// in hand, so every other spell in the game is untouched.
        /// </summary>
        public static float Factor(Pawn pawn, AbilityDef ability)
        {
            return SteadyFactor(pawn, ability) * GriefFactor(pawn);
        }

        /// <summary>Everything in Factor except the kindled roll: what the tooltip can honestly print.</summary>
        public static float SteadyFactor(Pawn pawn, AbilityDef ability)
        {
            float level = 1f + PerLevel * (CasterLevel(pawn, ability) - 1);
            return level * TSC_Instruments.SongPower(pawn, ability);
        }

        /// <summary>
        /// What Madoc did with the fire he left keeping watch, expressed as
        /// arithmetic (Dialogues/madoc_fire.agd).
        ///
        /// KINDLED - he kept the treaty open. The fires stay fond of him and
        /// they are not reliable: every cast rolls somewhere between a damp
        /// squib and something the company talks about for a week. Slightly
        /// better on average than plain casting, and never the same twice -
        /// but the AVERAGE sits below Lucid's steady 1.3, because reliability
        /// is what he declined to buy.
        ///
        /// LUCID - he released it, and said the true thing out loud to a
        /// thing that cannot be lied to. Steadier and stronger, every time,
        /// which is exactly what he traded the coat for.
        ///
        /// Neither hediff exists on anybody else, so every other caster in
        /// the game multiplies by 1.
        ///
        /// The kindled roll is SEEDED by pawn and tick rather than drawn from
        /// the live stream: Factor is called several times inside one cast
        /// (magnitude comp, damage comp) and from the gizmo tooltip every
        /// frame, and a roll that changes between those calls shows the
        /// player one number and applies another. Same tick, same roll.
        /// </summary>
        public const float KindledMin = 0.5f;
        public const float KindledMax = 2.0f;

        public static bool IsKindled(Pawn pawn)
        {
            return TSC_DefOf.TSC_Hediff_MadocKindled != null
                && (pawn?.health?.hediffSet?.HasHediff(TSC_DefOf.TSC_Hediff_MadocKindled) ?? false);
        }

        public static float GriefFactor(Pawn pawn)
        {
            HediffSet health = pawn?.health?.hediffSet;
            if (health == null)
            {
                return 1f;
            }
            if (TSC_DefOf.TSC_Hediff_MadocLucid != null
                && health.HasHediff(TSC_DefOf.TSC_Hediff_MadocLucid))
            {
                return 1.3f;
            }
            if (IsKindled(pawn))
            {
                Rand.PushState(Gen.HashCombineInt(pawn.thingIDNumber, Find.TickManager.TicksGame));
                float roll = Rand.Range(KindledMin, KindledMax);
                Rand.PopState();
                return roll;
            }
            // Maewyn, after the grove (Dialogues/maewyn_grove.agd). Handing
            // it to a keeper leaves her a source and a correspondence to
            // keep up: a smaller lift, and she also keeps the Nature she has
            // somewhere to practise (TSC_FeatMods.ProficiencyBonus). Giving
            // it back to the hills puts all of it into the road instead.
            if (TSC_DefOf.TSC_Hediff_MaewynKept != null
                && health.HasHediff(TSC_DefOf.TSC_Hediff_MaewynKept))
            {
                return 1.15f;
            }
            if (TSC_DefOf.TSC_Hediff_MaewynLetGo != null
                && health.HasHediff(TSC_DefOf.TSC_Hediff_MaewynLetGo))
            {
                return 1.3f;
            }
            return 1f;
        }

        /// <summary>
        /// Sets the freshly-applied hediff's magnitude to the caster's level
        /// factor - ONLY for TSC_Hediff_Leveled (severity carries the factor
        /// there; vanilla hediffs use severity for their own semantics and
        /// must not be touched).
        /// </summary>
        public static void SetMagnitude(Pawn target, HediffDef hediffDef, float factor)
        {
            if (target == null || hediffDef == null)
            {
                return;
            }
            Hediff hediff = target.health?.hediffSet?.GetFirstHediffOfDef(hediffDef);
            if (hediff is TSC_Hediff_Leveled)
            {
                hediff.Severity = Mathf.Max(1f, factor);
            }
        }
    }

    /// <summary>
    /// Drop-in replacement for vanilla CompAbilityEffect_GiveHediff that
    /// scales the applied hediff's MAGNITUDE by the caster's level factor.
    /// </summary>
    public class CompAbilityEffect_TSC_GiveHediffLeveled : CompAbilityEffect_GiveHediff
    {
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            float factor = TSC_SpellScaling.Factor(caster, parent.def);
            Pawn recipient = Props.onlyApplyToSelf ? caster : target.Pawn;
            TSC_SpellScaling.SetMagnitude(recipient, Props.hediffDef, factor);
            if (recipient == null)
            {
                return;
            }
            Hediff applied = recipient.health?.hediffSet?.GetFirstHediffOfDef(Props.hediffDef);
            foreach (TSC_FeatAbilityMod mod in TSC_FeatMods.ModsFor(caster, parent.def))
            {
                TSC_FeatMods.ApplyDuration(applied, mod.durationFactor);
                if (applied != null && mod.severityBonus > 0f)
                {
                    applied.Severity += mod.severityBonus;
                }
                // Bloodied Fury: the worse the wounds, the bigger the rage.
                if (applied != null && mod.scaleWithMissingHealth)
                {
                    float missing = 1f - Mathf.Clamp01(recipient.health.summaryHealth.SummaryHealthPercent);
                    applied.Severity *= 1f + missing;
                }
                if (mod.extraHediff != null && !recipient.health.hediffSet.HasHediff(mod.extraHediff))
                {
                    recipient.health.AddHediff(mod.extraHediff);
                }
                if (mod.extraDamageOnApply > 0f && recipient != caster)
                {
                    recipient.TakeDamage(new DamageInfo(DamageDefOf.Scratch, mod.extraDamageOnApply, 0.2f, -1f, caster));
                }
                if (mod.clearsStun)
                {
                    TSC_FeatMods.ClearStun(recipient);
                }
                if (mod.restoreCasterEnergy > 0f)
                {
                    TSC_ProgressionManager.Current.RestoreEnergy(caster, mod.restoreCasterEnergy);
                }
                // Shared Blessing: the same grace, laid on a second head.
                if (mod.duplicateToNearbyAlly && recipient.Spawned)
                {
                    Pawn second = FindSecondAlly(recipient, caster, mod.allyRadius, Props.hediffDef);
                    if (second != null)
                    {
                        Hediff copy = second.health.AddHediff(Props.hediffDef);
                        TSC_SpellScaling.SetMagnitude(second, Props.hediffDef, factor);
                        TSC_FeatMods.ApplyDuration(copy, mod.durationFactor);
                    }
                }
            }
        }

        private static Pawn FindSecondAlly(Pawn recipient, Pawn caster, float radius, HediffDef hediffDef)
        {
            Pawn best = null;
            float bestDist = radius + 0.5f;
            foreach (Pawn ally in recipient.Map.mapPawns.SpawnedPawnsInFaction(caster.Faction))
            {
                if (ally == recipient || ally.Dead || ally.health.hediffSet.HasHediff(hediffDef))
                {
                    continue;
                }
                float dist = ally.Position.DistanceTo(recipient.Position);
                if (dist < bestDist)
                {
                    best = ally;
                    bestDist = dist;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// The ability tooltip tells the truth: the XML descriptions carry base
    /// numbers, so the gizmo hover appends what THIS caster actually gets -
    /// the generic x-factor, or Magic Missile-style explicit curves. (The
    /// health tab is already honest: hediff tooltips read the SCALED stage.)
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Command_Ability), "Tooltip", HarmonyLib.MethodType.Getter)]
    public static class Patch_AbilityTooltip_ScaledValues
    {
        private static readonly System.Reflection.FieldInfo AbilityField =
            HarmonyLib.AccessTools.Field(typeof(Command_Ability), "ability");

        public static void Postfix(Command_Ability __instance, ref string __result)
        {
            if (!TSC_RpgMode.Active || AbilityField == null)
            {
                return;
            }
            Ability ability = AbilityField.GetValue(__instance) as Ability;
            Pawn caster = ability?.pawn;
            if (ability?.def == null || caster == null)
            {
                return;
            }
            TSC_ClassDef grantingClass = TSC_SpellScaling.GrantingClass(ability.def);
            if (grantingClass == null)
            {
                return;
            }
            int level = TSC_SpellScaling.CasterLevel(caster, ability.def);
            // Explicit-curve spells (Magic Missile) replace the generic
            // factor; show their own law instead of a factor that never runs.
            if (ability.def.comps != null)
            {
                foreach (AbilityCompProperties comp in ability.def.comps)
                {
                    if (comp is CompProperties_TSC_Damage damage
                        && damage.scaleClass != null && damage.damagePerLevel > 0f)
                    {
                        __result += $"\n\n{grantingClass.LabelCap} {level}: +{damage.damagePerLevel * level:0.#} damage from level (+{damage.damagePerLevel:0.#} per level).";
                        return;
                    }
                }
            }
            // Madoc kindled: the factor is a per-cast roll, so a single
            // number here would be a lie that changed every frame. Show the
            // spread instead.
            if (TSC_SpellScaling.IsKindled(caster))
            {
                float steady = TSC_SpellScaling.SteadyFactor(caster, ability.def);
                __result += $"\n\n{grantingClass.LabelCap} {level}: listed effects x{steady * TSC_SpellScaling.KindledMin:0.00} to x{steady * TSC_SpellScaling.KindledMax:0.00} for this caster - the fires are fond of him, and not reliable.";
                return;
            }
            float factor = TSC_SpellScaling.Factor(caster, ability.def);
            __result += factor > 1.001f
                ? $"\n\n{grantingClass.LabelCap} {level}: listed effects x{factor:0.00} for this caster (durations unchanged)."
                : $"\n\n{grantingClass.LabelCap} {level}: base strength. Effects grow +15% per {grantingClass.label} level.";
        }
    }

    /// <summary>
    /// A buff/debuff whose strength scales with the caster's class level:
    /// severity carries the scale factor (1 = baseline; initialSeverity of
    /// the timed-buff base is 1, so non-spell applications stay baseline).
    /// The current stage is rebuilt with scaled numbers: stat offsets and
    /// capacity offsets multiply by the factor; stat factors and pain scale
    /// their DISTANCE from 1 (a 0.65 incoming-damage factor at x1.6 becomes
    /// 0.44, a 1.30 mark becomes 1.48). Single-stage hediffs only - stage
    /// selection by severity would fight the factor encoding.
    /// </summary>
    public class TSC_Hediff_Leveled : HediffWithComps
    {
        private HediffStage cachedStage;
        private float cachedFactor = -1f;

        public override HediffStage CurStage
        {
            get
            {
                HediffStage baseStage = base.CurStage;
                float factor = Severity;
                if (baseStage == null || factor <= 1.001f)
                {
                    return baseStage;
                }
                if (cachedStage != null && Mathf.Abs(cachedFactor - factor) < 0.001f)
                {
                    return cachedStage;
                }
                cachedStage = BuildScaled(baseStage, factor);
                cachedFactor = factor;
                return cachedStage;
            }
        }

        private static HediffStage BuildScaled(HediffStage stage, float factor)
        {
            HediffStage result = new HediffStage
            {
                painFactor = stage.painFactor >= 1f
                    ? 1f + (stage.painFactor - 1f) * factor
                    : Mathf.Max(0f, 1f - (1f - stage.painFactor) * factor),
            };
            if (stage.statOffsets != null)
            {
                result.statOffsets = new List<StatModifier>();
                foreach (StatModifier modifier in stage.statOffsets)
                {
                    result.statOffsets.Add(new StatModifier { stat = modifier.stat, value = modifier.value * factor });
                }
            }
            if (stage.statFactors != null)
            {
                result.statFactors = new List<StatModifier>();
                foreach (StatModifier modifier in stage.statFactors)
                {
                    result.statFactors.Add(new StatModifier { stat = modifier.stat, value = 1f + (modifier.value - 1f) * factor });
                }
            }
            if (stage.capMods != null)
            {
                result.capMods = new List<PawnCapacityModifier>();
                foreach (PawnCapacityModifier capMod in stage.capMods)
                {
                    result.capMods.Add(new PawnCapacityModifier { capacity = capMod.capacity, offset = capMod.offset * factor });
                }
            }
            return result;
        }
    }
}
