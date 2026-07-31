using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Arcane Ward: the target takes NO damage from the next few attacks,
    /// rather than a percentage cut - flat mitigation is Barkskin's niche.
    /// Severity is the charge count (Charged Shot precedent), which is why
    /// this is NOT a TSC_Hediff_Leveled: SetMagnitude would overwrite the
    /// charges with the caster's level factor.
    /// </summary>
    public class TSC_Hediff_ArcaneWard : HediffWithComps
    {
        public int ChargesLeft => Mathf.Max(0, Mathf.RoundToInt(Severity));

        public override string LabelInBrackets
        {
            get
            {
                string charges = ChargesLeft + (ChargesLeft == 1 ? " hit" : " hits");
                string rest = base.LabelInBrackets;
                return rest.NullOrEmpty() ? charges : charges + ", " + rest;
            }
        }

        /// <summary>
        /// True when this hit should be eaten, spending a charge. Periodic
        /// harm burns through: fire is the classic ward-breaker, and
        /// non-violence damage (surgery, execution) is never blocked.
        /// </summary>
        public bool TryAbsorb(DamageInfo dinfo)
        {
            if (ChargesLeft <= 0
                || dinfo.Def == null
                || !dinfo.Def.harmsHealth
                || !dinfo.Def.ExternalViolenceFor(pawn)
                || dinfo.Def == DamageDefOf.Flame)
            {
                return false;
            }
            Severity -= 1f;
            if (pawn.Spawned)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map,
                    ChargesLeft > 0 ? "Warded" : "Ward broken",
                    new Color(0.4f, 0.9f, 1f));
            }
            // Severity 0 auto-removes via Hediff.ShouldRemove on the next
            // health tick; no explicit removal mid-damage-processing.
            return true;
        }
    }

    /// <summary>
    /// The absorption itself. A prefix rather than an IncomingDamageFactor
    /// because "no damage from the next three attacks" is per-HIT state, and
    /// stat factors cannot count hits. Misses never reach TakeDamage, so
    /// charges are only spent on connecting blows - in both combat modes.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_TakeDamage_ArcaneWard
    {
        private static HediffDef wardDef;

        private static HediffDef WardDef =>
            wardDef ?? (wardDef = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_ArcaneWard"));

        public static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!(__instance is Pawn pawn) || pawn.Dead || WardDef == null)
            {
                return true;
            }
            if (!(pawn.health?.hediffSet?.GetFirstHediffOfDef(WardDef) is TSC_Hediff_ArcaneWard ward)
                || !ward.TryAbsorb(dinfo))
            {
                return true;
            }
            __result = new DamageWorker.DamageResult();
            return false;
        }
    }
}
