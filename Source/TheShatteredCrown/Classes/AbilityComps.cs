using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    // ---------------------------------------------------------------- heal

    public class CompProperties_TSC_Heal : CompProperties_AbilityEffect
    {
        public float healAmount = 20f;

        public CompProperties_TSC_Heal()
        {
            compClass = typeof(CompAbilityEffect_TSC_Heal);
        }
    }

    /// <summary>Heals injuries on the target, worst first, up to healAmount total severity.</summary>
    public class CompAbilityEffect_TSC_Heal : CompAbilityEffect
    {
        public new CompProperties_TSC_Heal Props => (CompProperties_TSC_Heal)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn pawn = target.Pawn;
            if (pawn == null)
            {
                return;
            }
            float amount = Props.healAmount * TSC_SpellScaling.Factor(parent.pawn, parent.def);
            bool stopBleeding = false;
            bool removeDebuff = false;
            bool secondAlly = false;
            foreach (TSC_FeatAbilityMod mod in TSC_FeatMods.ModsFor(parent.pawn, parent.def))
            {
                amount *= mod.healBonusFactor;
                stopBleeding |= mod.stopsBleeding;
                removeDebuff |= mod.removesOneDebuff;
                secondAlly |= mod.healSecondAllyHalf;
            }
            HealWorstFirst(pawn, amount, stopBleeding);
            if (removeDebuff)
            {
                RemoveOneDebuff(pawn);
            }
            if (secondAlly && pawn.Spawned)
            {
                Pawn other = FindOtherInjuredAlly(pawn);
                if (other != null)
                {
                    HealWorstFirst(other, amount * 0.5f, stopBleeding: false);
                }
            }
        }

        private static void HealWorstFirst(Pawn pawn, float remaining, bool stopBleeding)
        {
            List<Hediff_Injury> injuries = new List<Hediff_Injury>();
            pawn.health.hediffSet.GetHediffs(ref injuries);
            injuries.SortByDescending(injury => injury.Severity);
            foreach (Hediff_Injury injury in injuries)
            {
                // Warm Hands: bind whatever cannot be closed outright, so the
                // patient stops losing blood even when the healing runs out.
                if (stopBleeding && injury.Bleeding)
                {
                    injury.Tended(1f, 1f);
                }
                if (remaining <= 0f)
                {
                    continue;
                }
                float heal = Mathf.Min(remaining, injury.Severity);
                injury.Heal(heal);
                remaining -= heal;
            }
            if (pawn.Spawned)
            {
                FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.HealingCross);
            }
        }

        /// <summary>Unmoved: one timed malady lifted along with the wounds.</summary>
        private static void RemoveOneDebuff(Pawn pawn)
        {
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff.def.isBad && hediff is HediffWithComps withComps
                    && withComps.TryGetComp<HediffComp_Disappears>() != null)
                {
                    pawn.health.RemoveHediff(hediff);
                    return;
                }
            }
        }

        private static Pawn FindOtherInjuredAlly(Pawn healed)
        {
            Pawn best = null;
            float bestDist = 5.5f;
            foreach (Pawn ally in healed.Map.mapPawns.SpawnedPawnsInFaction(healed.Faction))
            {
                if (ally == healed || ally.Dead || !ally.health.hediffSet.HasNaturallyHealingInjury())
                {
                    continue;
                }
                float dist = ally.Position.DistanceTo(healed.Position);
                if (dist < bestDist)
                {
                    best = ally;
                    bestDist = dist;
                }
            }
            return best;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            return target.Pawn != null && base.Valid(target, throwMessages);
        }
    }

    // ---------------------------------------------------------------- cleanse

    public class CompProperties_TSC_Cleanse : CompProperties_AbilityEffect
    {
        public CompProperties_TSC_Cleanse()
        {
            compClass = typeof(CompAbilityEffect_TSC_Cleanse);
        }
    }

    /// <summary>Removes diseases, infections, poisoning, and toxic buildup from the target.</summary>
    public class CompAbilityEffect_TSC_Cleanse : CompAbilityEffect
    {
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn pawn = target.Pawn;
            if (pawn == null)
            {
                return;
            }
            List<Hediff> toRemove = new List<Hediff>();
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (IsCleansable(hediff))
                {
                    toRemove.Add(hediff);
                }
            }
            foreach (Hediff hediff in toRemove)
            {
                pawn.health.RemoveHediff(hediff);
            }
            if (pawn.Spawned)
            {
                FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.HealingCross);
            }
            if (toRemove.Count == 0)
            {
                Messages.Message($"{pawn.LabelShortCap} had nothing to cleanse.", pawn, MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        private static bool IsCleansable(Hediff hediff)
        {
            if (hediff is Hediff_Injury || hediff is Hediff_MissingPart || hediff is Hediff_AddedPart)
            {
                return false;
            }
            if (!hediff.def.isBad)
            {
                return false;
            }
            if (hediff.TryGetComp<HediffComp_Immunizable>() != null)
            {
                return true;
            }
            return hediff.def == HediffDefOf.WoundInfection
                || hediff.def == HediffDefOf.FoodPoisoning
                || hediff.def == HediffDefOf.ToxicBuildup;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            return target.Pawn != null && base.Valid(target, throwMessages);
        }
    }

    // ---------------------------------------------------------------- area hediff

    public class CompProperties_TSC_AreaHediff : CompProperties_AbilityEffect
    {
        public HediffDef hediff;
        public float radius = 6.9f;
        public bool alliesOnly = true;
        public bool enemiesOnly;
        public bool includeCaster = true;
        /// <summary>Spell energy granted to each affected pawn (never the caster - no self-refunds).</summary>
        public float energyRestore;
        /// <summary>Per-pawn fleck on application (the psycast skip distortion). Turn OFF for spells with their own visual identity.</summary>
        public bool touchMark = true;

        public CompProperties_TSC_AreaHediff()
        {
            compClass = typeof(CompAbilityEffect_TSC_AreaHediff);
        }
    }

    /// <summary>
    /// Applies a hediff to every eligible pawn within radius of the target cell -
    /// the bard-song / battle-cry primitive. Reapplying refreshes the duration.
    /// </summary>
    public class CompAbilityEffect_TSC_AreaHediff : CompAbilityEffect
    {
        public new CompProperties_TSC_AreaHediff Props => (CompProperties_TSC_AreaHediff)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster.Map;
            if (map == null || Props.hediff == null)
            {
                return;
            }
            IntVec3 center = target.Cell.IsValid ? target.Cell : caster.Position;
            // An instrument in hand carries a song further; feats can widen
            // any area ability. Both are exactly 1 when not in play.
            float radius = Props.radius * TSC_Instruments.SongRadius(caster, parent.def)
                * TSC_FeatMods.RadiusFactor(caster, parent.def);
            float durationFactor = TSC_FeatMods.DurationFactor(caster, parent.def);
            List<TSC_FeatAbilityMod> featMods = new List<TSC_FeatAbilityMod>(TSC_FeatMods.ModsFor(caster, parent.def));
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.Dead || !pawn.Position.InHorDistOf(center, radius))
                {
                    continue;
                }
                if (!Props.includeCaster && pawn == caster)
                {
                    continue;
                }
                if (Props.enemiesOnly)
                {
                    if (!pawn.HostileTo(caster))
                    {
                        continue;
                    }
                }
                else if (Props.alliesOnly && pawn.Faction != caster.Faction)
                {
                    continue;
                }
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
                Hediff added = pawn.health.AddHediff(Props.hediff);
                float scale = TSC_SpellScaling.Factor(caster, parent.def);
                TSC_SpellScaling.SetMagnitude(pawn, Props.hediff, scale);
                TSC_FeatMods.ApplyDuration(added, durationFactor);
                foreach (TSC_FeatAbilityMod mod in featMods)
                {
                    if (mod.extraHediff != null && !pawn.health.hediffSet.HasHediff(mod.extraHediff))
                    {
                        pawn.health.AddHediff(mod.extraHediff);
                    }
                    // Unignorable: caught in the challenge, the enemy answers it.
                    if (mod.tauntCaster && pawn.HostileTo(caster) && pawn.mindState != null)
                    {
                        pawn.mindState.enemyTarget = caster;
                    }
                }
                if (Props.energyRestore > 0f && pawn != caster)
                {
                    TSC_ProgressionManager.Current.RestoreEnergy(pawn, Props.energyRestore * scale);
                }
                if (Props.touchMark && pawn.Spawned)
                {
                    FleckMaker.ThrowMetaIcon(pawn.Position, map, Props.enemiesOnly ? FleckDefOf.IncapIcon : FleckDefOf.PsycastSkipInnerExit);
                }
            }
            // Encore: the singer is paid too.
            if (Props.energyRestore > 0f)
            {
                foreach (TSC_FeatAbilityMod mod in featMods)
                {
                    if (mod.energyRestoreIncludesCaster)
                    {
                        TSC_ProgressionManager.Current.RestoreEnergy(caster,
                            Props.energyRestore * TSC_SpellScaling.Factor(caster, parent.def));
                        break;
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- vfx

    /// <summary>
    /// Visual identity per spell family - the psycast distortion ring reads
    /// as "generic magic" and loses meaning when every buff pops it.
    /// </summary>
    public enum TSC_VfxStyle
    {
        /// <summary>Tinted psycast-style distortion ring (the default).</summary>
        Ring,
        /// <summary>Leaf swirl and loam puffs: nature magic (Barkskin).</summary>
        Leaves,
        /// <summary>Low dust stomp with a boundary ring: braced defense (Stand Fast). No shimmer.</summary>
        Braced,
    }

    public class CompProperties_TSC_Vfx : CompProperties_AbilityEffect
    {
        public Color color = Color.white;
        /// <summary>Ring size; vanilla psycasts pass their effect radius here, so area spells should use theirs.</summary>
        public float scale = 1.5f;
        public TSC_VfxStyle style = TSC_VfxStyle.Ring;
        /// <summary>Float the ability's name over the target in the effect color - outlives the pause that follows a cast.</summary>
        public bool showLabel;
        public bool atCaster;
        public bool line;
        public bool sparks;
        public bool smoke;

        public CompProperties_TSC_Vfx()
        {
            compClass = typeof(CompAbilityEffect_TSC_Vfx);
        }
    }

    /// <summary>
    /// Data-driven spell visuals: a tinted psycast-style ground ring at the
    /// target (and optionally the caster), an energy line from caster to
    /// target, sparks, smoke - composed per ability in XML.
    /// </summary>
    public class CompAbilityEffect_TSC_Vfx : CompAbilityEffect
    {
        public new CompProperties_TSC_Vfx Props => (CompProperties_TSC_Vfx)props;

        // Not surfaced in FleckDefOf; resolved by name once (null if absent).
        private static readonly FleckDef PsychicLine = DefDatabase<FleckDef>.GetNamedSilentFail("PsycastPsychicLine");

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster?.MapHeld;
            if (map == null)
            {
                return;
            }
            Vector3 casterPos = caster.DrawPos;
            Vector3 targetPos = target.HasThing ? target.Thing.DrawPos
                : target.Cell.IsValid ? target.Cell.ToVector3Shifted()
                : casterPos;
            bool apart = (targetPos - casterPos).sqrMagnitude > 0.1f;
            if (Prefs.DevMode)
            {
                Log.Message($"[TSC] Vfx {parent.def.defName}: style={Props.style} at {targetPos}");
            }
            if (Props.showLabel)
            {
                MoteMaker.ThrowText(targetPos, map, parent.def.LabelCap, Props.color);
            }
            Burst(targetPos, map);
            if (Props.atCaster && apart)
            {
                Burst(casterPos, map);
            }
            if (Props.line && apart && PsychicLine != null)
            {
                FleckMaker.ConnectingLine(casterPos, targetPos, PsychicLine, map);
            }
            if (Props.sparks)
            {
                FleckMaker.ThrowMicroSparks(targetPos, map);
            }
            if (Props.smoke)
            {
                FleckMaker.ThrowSmoke(targetPos, map, 1.2f);
                if (Props.atCaster && apart)
                {
                    FleckMaker.ThrowSmoke(casterPos, map, 1.2f);
                }
            }
        }

        private void Burst(Vector3 pos, Map map)
        {
            switch (Props.style)
            {
                case TSC_VfxStyle.Leaves:
                    Leaves(pos, map);
                    return;
                case TSC_VfxStyle.Braced:
                    Braced(pos, map);
                    return;
                default:
                    Ring(pos, map);
                    return;
            }
        }

        private void Ring(Vector3 pos, Map map)
        {
            if (FleckDefOf.PsycastAreaEffect == null)
            {
                FleckMaker.ThrowDustPuff(pos, map, 1.2f);
                return;
            }
            // The ring is the player's read on how far a song reached, so it
            // has to grow with the instrument the same way the radius does.
            float scale = Props.scale * TSC_Instruments.SongRadius(parent.pawn, parent.def);
            FleckCreationData data = FleckMaker.GetDataStatic(pos, map, FleckDefOf.PsycastAreaEffect, scale);
            data.rotationRate = Rand.Range(-3f, 3f);
            data.instanceColor = Props.color;
            map.flecks.CreateFleck(data);
        }

        private static readonly Color LeafGreen = new Color(0.42f, 0.58f, 0.25f);
        private static readonly Color LoamBrown = new Color(0.45f, 0.35f, 0.22f);

        // Same creation path as Ring() - the one pathway PROVEN to render.
        // FleckMaker.ThrowDustPuff* helpers gate on camera view and mote
        // saturation, either of which can silently eat the whole effect.
        private static readonly FleckDef PuffDef =
            DefDatabase<FleckDef>.GetNamedSilentFail("DustPuffThick")
            ?? DefDatabase<FleckDef>.GetNamedSilentFail("DustPuff");

        private static void Puff(Vector3 pos, Map map, float scale, Color color)
        {
            if (PuffDef == null)
            {
                return;
            }
            FleckCreationData data = FleckMaker.GetDataStatic(pos, map, PuffDef, scale);
            data.instanceColor = color;
            data.rotationRate = Rand.Range(-30f, 30f);
            data.velocityAngle = Rand.Range(0f, 360f);
            data.velocitySpeed = Rand.Range(0.35f, 0.7f);
            map.flecks.CreateFleck(data);
        }

        private static readonly FleckDef SmokeDef = DefDatabase<FleckDef>.GetNamedSilentFail("Smoke");

        /// <summary>Tinted smoke: lives ~2.5s, so the effect survives the re-pause that follows a cast (puffs alone fade in under a second).</summary>
        private static void Plume(Vector3 pos, Map map, float scale, Color color)
        {
            if (SmokeDef == null)
            {
                return;
            }
            FleckCreationData data = FleckMaker.GetDataStatic(pos, map, SmokeDef, scale);
            data.instanceColor = color;
            data.rotationRate = Rand.Range(-15f, 15f);
            data.velocityAngle = Rand.Range(0f, 360f);
            data.velocitySpeed = Rand.Range(0.15f, 0.35f);
            map.flecks.CreateFleck(data);
        }

        /// <summary>Random offset on the GROUND plane (map is X-Z; a Vector2 cast puts scatter into altitude).</summary>
        private static Vector3 PlaneOffset(float radius)
        {
            Vector2 c = Rand.InsideUnitCircle * radius;
            return new Vector3(c.x, 0f, c.y);
        }

        /// <summary>Foliage whirls up around the target: green and bark-brown puffs low and close, lingering green haze behind them.</summary>
        private void Leaves(Vector3 pos, Map map)
        {
            float radius = Mathf.Max(0.5f, Props.scale * 0.55f);
            for (int i = 0; i < 12; i++)
            {
                Color color = i % 3 == 0 ? Props.color : i % 3 == 1 ? LeafGreen : LoamBrown;
                Puff(pos + PlaneOffset(radius), map, Rand.Range(2.2f, 3.2f), color);
            }
            for (int i = 0; i < 4; i++)
            {
                Plume(pos + PlaneOffset(radius * 0.7f), map, Rand.Range(1.8f, 2.6f), LeafGreen);
            }
        }

        // An EffecterDef (15-puff dust burst), not a fleck - triggered by hand.
        private static readonly EffecterDef StompCloud = DefDatabase<EffecterDef>.GetNamedSilentFail("ImpactSmallDustCloud");

        /// <summary>A grounded stomp: dust slams out at the center, and puffs mark the effect's edge where walls allow. Nothing shimmers.</summary>
        private void Braced(Vector3 pos, Map map)
        {
            IntVec3 cell = pos.ToIntVec3();
            if (StompCloud != null && cell.InBounds(map))
            {
                // Both targets must be valid: a sprayer sub-effecter keyed to
                // the B target silently spawns nothing at TargetInfo.Invalid.
                TargetInfo info = new TargetInfo(cell, map);
                Effecter effecter = StompCloud.Spawn();
                effecter.Trigger(info, info);
                effecter.Cleanup();
            }
            // The stomp itself, in the buff's color - guaranteed visible even
            // in a corridor where the boundary ring hits nothing but wall,
            // with lingering haze so the re-pause doesn't erase it.
            for (int i = 0; i < 8; i++)
            {
                Puff(pos + PlaneOffset(1.1f), map, Rand.Range(2.6f, 3.6f), Props.color);
            }
            for (int i = 0; i < 3; i++)
            {
                Plume(pos + PlaneOffset(1.2f), map, Rand.Range(1.8f, 2.4f), Props.color);
            }
            // Boundary ring: for an area buff the scale IS the radius, so the
            // puffs show exactly who stands inside the wall.
            float radius = Mathf.Max(0.8f, Props.scale);
            int points = Mathf.Clamp(Mathf.RoundToInt(radius * 2.5f), 6, 16);
            for (int i = 0; i < points; i++)
            {
                float angle = (360f / points) * i + Rand.Range(-8f, 8f);
                Vector3 at = pos + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * radius;
                if (at.ToIntVec3().InBounds(map))
                {
                    Puff(at, map, Rand.Range(1.6f, 2.2f), Props.color);
                }
            }
        }
    }

    // ---------------------------------------------------------------- direct damage

    public class CompProperties_TSC_Damage : CompProperties_AbilityEffect
    {
        public float damage = 12f;
        public float armorPenetration = 0.3f;
        public DamageDef damageDef;
        /// <summary>0 = single target; otherwise every ENEMY pawn within radius of the target point.</summary>
        public float radius;
        /// <summary>Optional level scaling: +damagePerLevel per caster level in scaleClass (Magic Missile).</summary>
        public TSC_ClassDef scaleClass;
        public float damagePerLevel;

        public CompProperties_TSC_Damage()
        {
            compClass = typeof(CompAbilityEffect_TSC_Damage);
        }
    }

    /// <summary>Sorcery: direct damage to the target, or to every hostile pawn around the target point (allies are spared - precision arcana).</summary>
    public class CompAbilityEffect_TSC_Damage : CompAbilityEffect
    {
        public new CompProperties_TSC_Damage Props => (CompProperties_TSC_Damage)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster?.Map;
            if (map == null)
            {
                return;
            }
            DamageDef damageDef = Props.damageDef ?? DamageDefOf.Burn;
            float damage = Props.damage;
            float radius = Props.radius * TSC_FeatMods.RadiusFactor(caster, parent.def);
            float armorPen = Props.armorPenetration;
            bool ignite = false;
            foreach (TSC_FeatAbilityMod mod in TSC_FeatMods.ModsFor(caster, parent.def))
            {
                armorPen += mod.armorPenetrationBonus;
                ignite |= mod.igniteTarget;
            }
            if (Props.scaleClass != null && Props.damagePerLevel > 0f)
            {
                // Explicit per-level curve (Magic Missile): replaces the
                // generic percentage factor, never stacks with it.
                TSC_ClassRecord record = TSC_ProgressionManager.Current.RecordOf(caster);
                int index = record.classes.IndexOf(Props.scaleClass);
                if (index >= 0)
                {
                    damage += record.levels[index] * Props.damagePerLevel;
                }
            }
            else
            {
                damage *= TSC_SpellScaling.Factor(caster, parent.def);
            }
            if (radius <= 0.01f)
            {
                target.Thing?.TakeDamage(new DamageInfo(damageDef, damage, armorPen, -1f, caster));
                if (ignite)
                {
                    target.Thing?.TryAttachFire(0.35f, caster);
                }
                return;
            }
            IntVec3 center = target.Cell.IsValid ? target.Cell : caster.Position;
            List<Pawn> hit = new List<Pawn>();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!pawn.Dead && pawn.HostileTo(caster) && pawn.Position.InHorDistOf(center, radius))
                {
                    hit.Add(pawn); // collect first: damage can despawn/panic pawns mid-iteration
                }
            }
            foreach (Pawn pawn in hit)
            {
                pawn.TakeDamage(new DamageInfo(damageDef, damage, armorPen, -1f, caster));
                if (ignite)
                {
                    pawn.TryAttachFire(0.35f, caster);
                }
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (Props.radius <= 0.01f && target.Thing == null)
            {
                if (throwMessages)
                {
                    Messages.Message("Must target a creature.", MessageTypeDefOf.RejectInput, historical: false);
                }
                return false;
            }
            return base.Valid(target, throwMessages);
        }
    }

    // ---------------------------------------------------------------- weapon strike

    public class CompProperties_TSC_WeaponStrike : CompProperties_AbilityEffect
    {
        /// <summary>Damage as a multiple of the caster's average weapon damage (3 = Ambush's 300%).</summary>
        public float multiplier = 1f;
        /// <summary>0 = the targeted thing; otherwise every ENEMY pawn within radius of the CASTER (Whirlwind).</summary>
        public float radius;
        public float armorPenetration = 0.25f;
        public DamageDef damageDef;

        public CompProperties_TSC_WeaponStrike()
        {
            compClass = typeof(CompAbilityEffect_TSC_WeaponStrike);
        }
    }

    /// <summary>
    /// Martial arts: damage derived from the caster's own weapon (average tool
    /// power; race tools when unarmed), scaled by a multiplier - Ambush's
    /// triple strike, Whirlwind's all-around blow.
    /// </summary>
    public class CompAbilityEffect_TSC_WeaponStrike : CompAbilityEffect
    {
        public new CompProperties_TSC_WeaponStrike Props => (CompProperties_TSC_WeaponStrike)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster?.Map;
            if (map == null)
            {
                return;
            }
            float damage = AverageWeaponDamage(caster) * Props.multiplier
                * TSC_SpellScaling.Factor(caster, parent.def);
            DamageDef damageDef = Props.damageDef ?? DamageDefOf.Stab;
            float strikeRadius = Props.radius * TSC_FeatMods.RadiusFactor(caster, parent.def);
            if (strikeRadius <= 0.01f)
            {
                target.Thing?.TakeDamage(new DamageInfo(damageDef, damage, Props.armorPenetration, -1f, caster));
                return;
            }
            List<Pawn> hit = new List<Pawn>();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!pawn.Dead && pawn.HostileTo(caster) && pawn.Position.InHorDistOf(caster.Position, strikeRadius))
                {
                    hit.Add(pawn); // collect first: damage can despawn pawns mid-iteration
                }
            }
            foreach (Pawn pawn in hit)
            {
                pawn.TakeDamage(new DamageInfo(damageDef, damage, Props.armorPenetration, -1f, caster));
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (Props.radius <= 0.01f && target.Thing == null)
            {
                if (throwMessages)
                {
                    Messages.Message("Must target a creature.", MessageTypeDefOf.RejectInput, historical: false);
                }
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        private static float AverageWeaponDamage(Pawn pawn)
        {
            List<Tool> tools = pawn.equipment?.Primary?.def.tools;
            if (tools.NullOrEmpty())
            {
                tools = pawn.def.tools; // unarmed: fists, headbutts, whatever the race has
            }
            if (tools.NullOrEmpty())
            {
                return 8f;
            }
            float total = 0f;
            foreach (Tool tool in tools)
            {
                total += tool.power;
            }
            return total / tools.Count;
        }
    }

    // ---------------------------------------------------------------- explosion

    public class CompProperties_TSC_Explosion : CompProperties_AbilityEffect
    {
        public float radius = 2.9f;
        public int damage = 15;
        public DamageDef damageDef;

        public CompProperties_TSC_Explosion()
        {
            compClass = typeof(CompAbilityEffect_TSC_Explosion);
        }
    }

    /// <summary>The big nuke: a real explosion at the target point - fire, sound, and NO regard for friend or foe. Placement is the skill.</summary>
    public class CompAbilityEffect_TSC_Explosion : CompAbilityEffect
    {
        public new CompProperties_TSC_Explosion Props => (CompProperties_TSC_Explosion)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster?.Map;
            if (map == null || !target.Cell.IsValid)
            {
                return;
            }
            GenExplosion.DoExplosion(target.Cell, map,
                Props.radius * TSC_FeatMods.RadiusFactor(caster, parent.def),
                Props.damageDef ?? DamageDefOf.Flame, caster,
                Mathf.RoundToInt(Props.damage * TSC_SpellScaling.Factor(caster, parent.def)));
        }
    }

    // ---------------------------------------------------------------- self teleport

    public class CompProperties_TSC_SelfTeleport : CompProperties_AbilityEffect
    {
        public IntRange stunTicks = IntRange.Zero;

        public CompProperties_TSC_SelfTeleport()
        {
            compClass = typeof(CompAbilityEffect_TSC_SelfTeleport);
        }
    }

    /// <summary>
    /// One-click self-teleport: the targeted cell is the DESTINATION and the
    /// caster is who moves. (Vanilla CompAbilityEffect_Teleport is built for
    /// Skip's two-step targeting - first pick who, then pick where - which a
    /// single-target ability never completes.)
    /// </summary>
    public class CompAbilityEffect_TSC_SelfTeleport : CompAbilityEffect
    {
        public new CompProperties_TSC_SelfTeleport Props => (CompProperties_TSC_SelfTeleport)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster.Map;
            IntVec3 cell = target.Cell;
            if (map == null || !cell.IsValid || !cell.Standable(map))
            {
                return;
            }
            caster.pather?.StopDead();
            caster.Position = cell;
            caster.Notify_Teleported();
            if (Props.stunTicks != IntRange.Zero)
            {
                caster.stances?.stunner?.StunFor(Props.stunTicks.RandomInRange, caster, addBattleLog: false);
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Map map = parent.pawn?.Map;
            if (map == null || !target.Cell.IsValid || !target.Cell.Standable(map))
            {
                if (throwMessages)
                {
                    Messages.Message("Cannot step there.", MessageTypeDefOf.RejectInput, historical: false);
                }
                return false;
            }
            return base.Valid(target, throwMessages);
        }
    }

    // ---------------------------------------------------------------- ongoing vfx

    public class HediffCompProperties_TSC_OngoingVfx : HediffCompProperties
    {
        public FleckDef fleck;
        public Color color = Color.white;
        public float scale = 1f;
        public int interval = 60;
        /// <summary>Random offset radius around the pawn for each puff.</summary>
        public float scatter = 0.35f;

        public HediffCompProperties_TSC_OngoingVfx()
        {
            compClass = typeof(HediffComp_TSC_OngoingVfx);
        }
    }

    /// <summary>
    /// Duration effects stay VISIBLE: while the hediff lasts, tinted flecks
    /// pulse around the pawn (bramble churn at a snared pawn's feet, heat
    /// shimmer on a raging barbarian, sigil rings on a warded ally).
    /// </summary>
    public class HediffComp_TSC_OngoingVfx : HediffComp
    {
        public HediffCompProperties_TSC_OngoingVfx Props => (HediffCompProperties_TSC_OngoingVfx)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = parent.pawn;
            if (Props.fleck == null || !p.Spawned || p.Map == null || !p.IsHashIntervalTick(Props.interval))
            {
                return;
            }
            Vector3 pos = p.DrawPos + new Vector3(
                Rand.Range(-Props.scatter, Props.scatter), 0f, Rand.Range(-Props.scatter, Props.scatter));
            FleckCreationData data = FleckMaker.GetDataStatic(pos, p.Map, Props.fleck, Props.scale);
            data.instanceColor = Props.color;
            p.Map.flecks.CreateFleck(data);
        }
    }

    // ---------------------------------------------------------------- energy cost

    public class CompProperties_TSC_EnergyCost : CompProperties_AbilityEffect
    {
        public float cost = 10f;

        public CompProperties_TSC_EnergyCost()
        {
            compClass = typeof(CompAbilityEffect_TSC_EnergyCost);
        }
    }

    /// <summary>
    /// Spell energy: blocks the cast gizmo when the caster's pool is short, and
    /// drains the pool when the ability applies. Energy regenerates during sleep
    /// (see TSC_ProgressionManager).
    /// </summary>
    public class CompAbilityEffect_TSC_EnergyCost : CompAbilityEffect
    {
        public new CompProperties_TSC_EnergyCost Props => (CompProperties_TSC_EnergyCost)props;

        private float EffectiveCost => Props.cost * TSC_FeatMods.EnergyCostFactor(parent.pawn);

        public override bool GizmoDisabled(out string reason)
        {
            float cost = EffectiveCost;
            float current = TSC_ProgressionManager.Current.EnergyOf(parent.pawn);
            if (current < cost)
            {
                // Overchannel: a dry sorcerer may cast anyway and pay the
                // shortfall in burns.
                if (TSC_Feats.Has(parent.pawn, "TSC_Feat_Overchannel"))
                {
                    reason = null;
                    return false;
                }
                reason = $"Not enough energy: {current:F0} of {cost:F0} needed. Energy returns with sleep.";
                return true;
            }
            reason = null;
            return false;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            float cost = EffectiveCost;
            float current = TSC_ProgressionManager.Current.EnergyOf(parent.pawn);
            if (current < cost && TSC_Feats.Has(parent.pawn, "TSC_Feat_Overchannel"))
            {
                float shortfall = cost - current;
                TSC_ProgressionManager.Current.TryConsumeEnergy(parent.pawn, current);
                parent.pawn.TakeDamage(new DamageInfo(DamageDefOf.Burn, shortfall * 0.5f, 0f, -1f, parent.pawn));
                Messages.Message($"{parent.pawn.LabelShortCap} overchannels: the spell takes its price in flesh.",
                    parent.pawn, MessageTypeDefOf.NegativeEvent, historical: false);
            }
            else
            {
                TSC_ProgressionManager.Current.TryConsumeEnergy(parent.pawn, cost);
            }
            TSC_EncounterController encounter = TSC_EncounterController.Current;
            if (encounter != null && encounter.ActiveOn(parent.pawn?.Map))
            {
                encounter.AddLog(
                    $"{parent.pawn.LabelShortCap} casts {parent.def.LabelCap} ({EffectiveCost:0} Energy, {TSC_EncounterController.ActionApCost:0} AP).",
                    TSC_EncounterController.LogSpellColor);
            }
        }

        public override string ExtraTooltipPart()
        {
            return $"Energy cost: {EffectiveCost:F0}";
        }
    }
}
