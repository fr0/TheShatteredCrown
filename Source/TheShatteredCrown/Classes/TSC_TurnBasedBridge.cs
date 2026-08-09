using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Registers The Shattered Crown's behaviors into the turn engine's
    /// hooks (TurnBasedHooks, in 0TSC.TurnBased.dll). This file is the
    /// complete list of what the RPG layer adds to plain turn combat:
    /// settings, the Running Start feat, spell energy, and the ability comp
    /// vocabulary. The engine runs fine with none of it - these delegates
    /// are the entire seam.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TSC_TurnBasedBridge
    {
        static TSC_TurnBasedBridge()
        {
            TurnBasedHooks.AutoEndTurn = () => TSC_Mod.Settings?.autoEndTurn ?? true;
            TurnBasedHooks.EnemyBeatTicks = () => TSC_Mod.Settings?.EnemyBeatTicks ?? 30;
            TurnBasedHooks.DamageFactor = () => TSC_Mod.Settings?.tbDamageFactor ?? 1f;

            // Running Start: a monk who has already moved this turn attacks
            // for 1 AP less. Registered here so every preview, label, and
            // charge shows the same discounted price.
            TurnBasedHooks.ModifyAttackApCost = (pawn, cost) =>
            {
                TSC_EncounterController ctrl = TSC_EncounterController.Instance;
                if (ctrl != null && ctrl.Active && ctrl.MovedThisTurn(pawn)
                    && TSC_Feats.Has(pawn, "TSC_Feat_RunningStart"))
                {
                    return Mathf.Max(TSC_EncounterController.MinActionAp, cost - 2f);
                }
                return cost;
            };

            // Spell energy strip on the initiative bar (max 0 = no classes, no bar).
            TurnBasedHooks.EnergyBar = pawn => new Vector2(
                TSC_ProgressionManager.Current.EnergyOf(pawn),
                TSC_ProgressionManager.Current.MaxEnergy(pawn));

            TurnBasedHooks.HediffExtraTip = hediff =>
                hediff is TSC_Hediff_Leveled && hediff.Severity > 1.001f
                    ? $"Strength x{hediff.Severity:0.0#} (caster level)"
                    : null;

            TurnBasedHooks.CompIsBuff = comp => comp is CompProperties_TSC_AreaHediff;
            TurnBasedHooks.CompIsIncidental = comp =>
                comp is CompProperties_TSC_EnergyCost
                || comp is CompProperties_TSC_Vfx
                || comp is CompProperties_TSC_GrantAp;

            TurnBasedHooks.AbilityHasEnergyCost = def =>
            {
                if (def.comps == null)
                {
                    return false;
                }
                for (int i = 0; i < def.comps.Count; i++)
                {
                    if (def.comps[i] is CompProperties_TSC_EnergyCost)
                    {
                        return true;
                    }
                }
                return false;
            };

            // Charge's surge: AP refunded in the same instant the cast is charged.
            TurnBasedHooks.ApRefundFor = verb => CompAbilityEffect_TSC_GrantAp.AmountOn(verb);

            // CE ammo probe for the dry-weapon callout.
            TurnBasedHooks.OutOfAmmo = pawn => TSC_AmmoState.OutOfAmmo(pawn);
        }
    }
}
