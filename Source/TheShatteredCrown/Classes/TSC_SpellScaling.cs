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

        public static float Factor(Pawn pawn, AbilityDef ability)
        {
            return 1f + PerLevel * (CasterLevel(pawn, ability) - 1);
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
            float factor = TSC_SpellScaling.Factor(parent.pawn, parent.def);
            Pawn recipient = Props.onlyApplyToSelf ? parent.pawn : target.Pawn;
            TSC_SpellScaling.SetMagnitude(recipient, Props.hediffDef, factor);
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
