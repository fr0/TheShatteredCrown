using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Charged Shot (Ranger): while the TSC_Hediff_ChargedShot buff holds,
    /// the caster's ranged projectiles deal 150% damage; each projectile
    /// IMPACT consumes one charge (severity), missed shots included - a
    /// spent arrow is spent. Multiply-at-read plus consume-at-impact means
    /// every projectile fired under the buff keeps its bonus even if the
    /// last charge is consumed while it flies.
    /// </summary>
    internal static class TSC_ChargedShotUtility
    {
        // 1.8 originally; trimmed to 1.5 when the buff gained an accuracy
        // half and self-buffs dropped to half AP - the damage was the knob
        // to pay those with.
        public const float BaseDamageFactor = 1.5f;

        private static readonly AccessTools.FieldRef<Projectile, Thing> LauncherRef =
            AccessTools.FieldRefAccess<Projectile, Thing>("launcher");

        private static HediffDef cachedDef;
        private static AbilityDef cachedAbility;

        public static HediffDef Def =>
            cachedDef ?? (cachedDef = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_ChargedShot"));

        private static AbilityDef AbilityDef =>
            cachedAbility ?? (cachedAbility = DefDatabase<AbilityDef>.GetNamedSilentFail("TSC_Ability_ChargedShot"));

        /// <summary>The +50% bonus portion scales with ranger level: x1.5 at level 1, x1.8 at 5.</summary>
        public static float DamageFactorFor(Pawn pawn)
        {
            return 1f + (BaseDamageFactor - 1f) * TSC_SpellScaling.Factor(pawn, AbilityDef);
        }

        public static Pawn LauncherPawn(Projectile projectile)
        {
            return LauncherRef(projectile) as Pawn;
        }

        public static bool HasCharge(Pawn pawn)
        {
            return Def != null && pawn.health?.hediffSet?.HasHediff(Def) == true;
        }

        public static void ConsumeCharge(Pawn pawn)
        {
            if (Def == null)
            {
                return;
            }
            Hediff hediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(Def);
            if (hediff == null)
            {
                return;
            }
            hediff.Severity -= 1f;
            if (hediff.Severity < 0.5f)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.DamageAmount), MethodType.Getter)]
    public static class Patch_Projectile_ChargedShotDamage
    {
        public static void Postfix(Projectile __instance, ref int __result)
        {
            Pawn pawn = TSC_ChargedShotUtility.LauncherPawn(__instance);
            if (pawn != null && TSC_ChargedShotUtility.HasCharge(pawn))
            {
                __result = Mathf.RoundToInt(__result * TSC_ChargedShotUtility.DamageFactorFor(pawn));
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), "Impact")]
    public static class Patch_Projectile_ChargedShotConsume
    {
        public static void Postfix(Projectile __instance)
        {
            Pawn pawn = TSC_ChargedShotUtility.LauncherPawn(__instance);
            if (pawn != null)
            {
                TSC_ChargedShotUtility.ConsumeCharge(pawn);
            }
        }
    }
}
