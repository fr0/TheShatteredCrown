using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A spell that travels.
    ///
    /// Magic Missile used to be an instant TakeDamage plus a drawn line -
    /// functional, but the hit lands before the eye has anything to follow.
    /// This is a real projectile: it flies, the turn engine's projectile
    /// hold keeps the turn open until it lands (ThingRequestGroup.Projectile
    /// is def-driven, so the existing hold sees it with no changes), and
    /// the damage - computed by the same TSC_AbilityDamage formula at CAST
    /// time, when the caster's levels and feats are in hand - rides along
    /// and is applied on impact.
    /// </summary>
    public class Projectile_TSC_Spell : Projectile
    {
        private float damageOverride = -1f;
        private float armorPenOverride = -1f;
        private bool igniteTarget;

        public void ConfigureHit(float damage, float armorPen, bool ignite)
        {
            damageOverride = damage;
            armorPenOverride = armorPen;
            igniteTarget = ignite;
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            IntVec3 cell = Position;
            Thing caster = launcher;
            base.Impact(hitThing, blockedByShield);
            if (blockedByShield)
            {
                return;
            }
            if (hitThing != null)
            {
                float amount = damageOverride >= 0f ? damageOverride : DamageAmount;
                float armorPen = armorPenOverride >= 0f ? armorPenOverride : ArmorPenetration;
                DamageDef damageDef = def.projectile.damageDef ?? DamageDefOf.Burn;
                hitThing.TakeDamage(new DamageInfo(damageDef, amount, armorPen, -1f, caster));
                if (igniteTarget)
                {
                    hitThing.TryAttachFire(0.35f, caster as Pawn);
                }
            }
            if (map != null)
            {
                // The burst at the point of arrival: brief, bright, done.
                FleckMaker.Static(cell, map, FleckDefOf.ExplosionFlash, 3f);
                FleckMaker.ThrowMicroSparks(cell.ToVector3Shifted(), map);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref damageOverride, "damageOverride", -1f);
            Scribe_Values.Look(ref armorPenOverride, "armorPenOverride", -1f);
            Scribe_Values.Look(ref igniteTarget, "igniteTarget", defaultValue: false);
        }
    }

    /// <summary>
    /// Launches a spell projectile at the target instead of dealing damage
    /// on the spot. Inherits every damage knob from CompProperties_TSC_Damage
    /// (base, per-level curve, armor pen, feat mods) - only the delivery
    /// changes. Single-target by design: radius spells keep the instant comp.
    /// </summary>
    public class CompProperties_TSC_SpellBolt : CompProperties_TSC_Damage
    {
        public ThingDef projectileDef;

        public CompProperties_TSC_SpellBolt()
        {
            compClass = typeof(CompAbilityEffect_TSC_SpellBolt);
        }
    }

    public class CompAbilityEffect_TSC_SpellBolt : CompAbilityEffect
    {
        public new CompProperties_TSC_SpellBolt Props => (CompProperties_TSC_SpellBolt)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster?.Map;
            if (map == null || Props.projectileDef == null || !target.IsValid)
            {
                return;
            }
            float damage = TSC_AbilityDamage.Resolve(caster, parent.def, Props,
                out float armorPen, out bool ignite);
            if (!(GenSpawn.Spawn(Props.projectileDef, caster.Position, map) is Projectile_TSC_Spell bolt))
            {
                Log.Warning($"[The Shattered Crown] {Props.projectileDef.defName} is not a "
                    + "Projectile_TSC_Spell; the spell fizzles.");
                return;
            }
            bolt.ConfigureHit(damage, armorPen, ignite);
            // IntendedTarget only: a spell does not clip the ally standing in
            // the lane. Precision arcana, same promise the instant version kept.
            bolt.Launch(caster, caster.DrawPos, target, target, ProjectileHitFlags.IntendedTarget);
        }
    }
}
