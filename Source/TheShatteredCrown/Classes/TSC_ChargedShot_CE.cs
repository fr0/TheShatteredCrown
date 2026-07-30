using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Charged Shot, for Combat Extended.
    ///
    /// The vanilla patches hang off Projectile.DamageAmount and
    /// Projectile.Impact. CombatExtended.ProjectileCE does not derive from
    /// Projectile at all - it derives straight from ThingWithComps - so those
    /// patches are not merely shadowed, they are inapplicable: a CE arrow is
    /// not a Projectile by any definition Harmony can reach. The ability was
    /// silently doing nothing under CE, neither adding damage nor spending
    /// its charge, which left the hediff stuck on the pawn forever.
    ///
    /// CE exposes the same two seams under different names and types
    /// (DamageAmount is a float here, not an int), so this mirrors the pair
    /// against them. Everything resolves by reflection behind Prepare(), so
    /// without CE loaded these patches simply never apply.
    /// </summary>
    public static class TSC_ChargedShot_CE
    {
        private static Type projectile;
        private static FieldInfo launcherField;
        private static bool resolved;

        public static Type ProjectileType
        {
            get
            {
                Resolve();
                return projectile;
            }
        }

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;
            projectile = AccessTools.TypeByName("CombatExtended.ProjectileCE");
            if (projectile != null)
            {
                launcherField = AccessTools.Field(projectile, "launcher");
            }
        }

        /// <summary>Who fired it, read off CE's own launcher field.</summary>
        public static Pawn LauncherPawn(Thing projectileInstance)
        {
            Resolve();
            if (launcherField == null || projectileInstance == null)
            {
                return null;
            }
            return launcherField.GetValue(projectileInstance) as Pawn;
        }
    }

    /// <summary>CE's damage getter is a float property; the charge scales it the same way.</summary>
    [HarmonyPatch]
    public static class Patch_ProjectileCE_ChargedShotDamage
    {
        public static bool Prepare()
        {
            return TargetMethod() != null;
        }

        public static MethodBase TargetMethod()
        {
            Type type = TSC_ChargedShot_CE.ProjectileType;
            return type != null ? AccessTools.PropertyGetter(type, "DamageAmount") : null;
        }

        public static void Postfix(Thing __instance, ref float __result)
        {
            Pawn pawn = TSC_ChargedShot_CE.LauncherPawn(__instance);
            if (pawn != null && TSC_ChargedShotUtility.HasCharge(pawn))
            {
                __result *= TSC_ChargedShotUtility.DamageFactorFor(pawn);
            }
        }
    }

    /// <summary>
    /// And the charge is spent on impact, as in vanilla. Without this the
    /// hediff never clears: the shot stays "charged" for the rest of the
    /// fight and every arrow after it hits just as hard.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_ProjectileCE_ChargedShotConsume
    {
        public static bool Prepare()
        {
            return TargetMethod() != null;
        }

        public static MethodBase TargetMethod()
        {
            Type type = TSC_ChargedShot_CE.ProjectileType;
            return type != null
                ? AccessTools.DeclaredMethod(type, "Impact", new[] { typeof(Thing) })
                : null;
        }

        public static void Postfix(Thing __instance)
        {
            Pawn pawn = TSC_ChargedShot_CE.LauncherPawn(__instance);
            if (pawn != null)
            {
                TSC_ChargedShotUtility.ConsumeCharge(pawn);
            }
        }
    }
}
