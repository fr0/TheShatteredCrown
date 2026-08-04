using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Minor magical enhancements on random armor loot. An enchantment is a
    /// per-INSTANCE property (one warded gambeson among many plain ones), so
    /// it lives in a ThingComp injected onto every apparel def at startup -
    /// vanilla and modded alike, which is the whole point: a Medieval
    /// Overhaul cuirass can roll warded exactly like ours can.
    ///
    /// Effects apply only while WORN: stat enchants ride a StatWorker
    /// postfix that sums offsets from the wearer's worn apparel (the same
    /// place vanilla adds def-level gear offsets), and the keen enchant
    /// scales damage DEALT by the wearer at impact time. Take the piece off
    /// and the magic goes quiet with it.
    /// </summary>
    public class TSC_EnchantDef : Def
    {
        /// <summary>Stat offsets granted to the wearer while worn.</summary>
        public List<StatModifier> statOffsets;

        /// <summary>Multiplier on all damage the wearer DEALS (melee and ranged alike).</summary>
        public float damageDealtFactor = 1f;

        /// <summary>One quartermaster's line for the inspect pane.</summary>
        public string flavor;

        /// <summary>
        /// Weapon workings, as opposed to armor ones: rolled onto blades
        /// and bows, and they DO something when a hit lands rather than
        /// sitting in a stat sheet. One proc roll per landed hit covers
        /// every rider this def carries.
        /// </summary>
        public bool forWeapons;

        /// <summary>Odds per landed hit that the working fires at all.</summary>
        public float onHitChance = 1f;

        /// <summary>Extra damage dealt when it fires, of onHitDamageDef's kind.</summary>
        public float onHitDamage;

        public DamageDef onHitDamageDef;

        /// <summary>Stun laid on the target when it fires. 0 = no stun.</summary>
        public int stunTicks;

        /// <summary>A hediff dosed onto the target when it fires (venom, chill).</summary>
        public HediffDef onHitHediff;

        public float onHitSeverity = 1f;

        /// <summary>
        /// The effects as one plain line ("Move speed +0.30 - Incoming
        /// damage -8%"), built from the same numbers the enchant applies so
        /// the shop card can never drift from the def. The prose
        /// description overflowed the enchanter's two-line card and told
        /// the buyer everything except the number they were buying.
        /// </summary>
        public string EffectLine()
        {
            return string.Join("   ·   ", EffectParts());
        }

        /// <summary>Every effect as its own plain phrase, passive and on-hit alike.</summary>
        public List<string> EffectParts()
        {
            List<string> parts = new List<string>();
            if (statOffsets != null)
            {
                foreach (StatModifier offset in statOffsets)
                {
                    parts.Add($"{offset.stat.LabelCap} "
                        + offset.stat.Worker.ValueToString(offset.value, false, ToStringNumberSense.Offset));
                }
            }
            if (!Mathf.Approximately(damageDealtFactor, 1f))
            {
                parts.Add($"Damage dealt {(damageDealtFactor - 1f).ToStringPercent("+0;-0")}");
            }
            string odds = onHitChance < 1f ? $"{onHitChance.ToStringPercent()} chance of " : "";
            if (onHitDamage > 0f && onHitDamageDef != null)
            {
                parts.Add($"On hit: {odds}+{onHitDamage:0} {onHitDamageDef.label}");
            }
            if (stunTicks > 0)
            {
                parts.Add($"On hit: {odds}stun {stunTicks / 60f:0.#}s");
            }
            if (onHitHediff != null)
            {
                string dose = onHitSeverity < 1f ? $" +{onHitSeverity.ToStringPercent()}" : "";
                parts.Add($"On hit: {odds}{onHitHediff.label}{dose}");
            }
            return parts;
        }
    }

    public class Comp_TSC_Enchant : ThingComp
    {
        public TSC_EnchantDef enchant;

        public override string TransformLabel(string label) =>
            enchant == null ? label : $"{label} ({enchant.label})";

        public override string CompInspectStringExtra()
        {
            if (enchant == null)
            {
                return null;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(enchant.forWeapons
                ? $"Enchanted ({enchant.label}), while wielded:"
                : $"Enchanted ({enchant.label}), while worn:");
            foreach (string part in enchant.EffectParts())
            {
                sb.Append("\n  " + part);
            }
            if (!enchant.flavor.NullOrEmpty())
            {
                sb.Append("\n" + enchant.flavor);
            }
            return sb.ToString();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref enchant, "enchant");
        }
    }

    [StaticConstructorOnStartup]
    public static class TSC_Enchanter
    {
        /// <summary>Chance per armor piece in a generated loot set (chests, stashes, quest rewards).</summary>
        public const float LootChance = 0.12f;
        /// <summary>Chance per worn armor piece on a generated NPC - their gear drops, so fights are loot too.</summary>
        public const float WornChance = 0.04f;

        /// <summary>Stats any enchant can touch: the fast-path filter for the StatWorker postfix.</summary>
        public static readonly HashSet<StatDef> EnchantableStats = new HashSet<StatDef>();

        static TSC_Enchanter()
        {
            CompProperties comp = new CompProperties(typeof(Comp_TSC_Enchant));
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if ((def.IsApparel || IsWeaponDef(def)) && def.comps != null
                    && !def.comps.Any(c => c.compClass == typeof(Comp_TSC_Enchant)))
                {
                    def.comps.Add(comp);
                }
            }
            foreach (TSC_EnchantDef def in DefDatabase<TSC_EnchantDef>.AllDefsListForReading)
            {
                if (def.statOffsets == null)
                {
                    continue;
                }
                foreach (StatModifier offset in def.statOffsets)
                {
                    if (offset.stat != null)
                    {
                        EnchantableStats.Add(offset.stat);
                    }
                }
            }
        }

        /// <summary>Armor test on the INSTANCE, so stuffed and modded pieces price their own protection.</summary>
        private static bool IsArmor(Thing t) =>
            t.def.IsApparel
            && (t.GetStatValue(StatDefOf.ArmorRating_Sharp) > 0.05f
                || t.GetStatValue(StatDefOf.ArmorRating_Blunt) > 0.05f);

        /// <summary>A real hand-held weapon: the equip test, not the tools test (wood is not a club).</summary>
        public static bool IsWeaponDef(ThingDef def) =>
            def.category == ThingCategory.Item && def.IsWeapon && !def.IsApparel
            && def.equipmentType == EquipmentType.Primary && !def.destroyOnDrop;

        public static bool IsWeaponThing(Thing t) => t != null && IsWeaponDef(t.def);

        /// <summary>
        /// Story artifacts refuse the bench and the loot roll alike. The
        /// marker is tradeability None - the same "the world does not deal
        /// in this" flag the Kingsblade and the Crown already carry - so a
        /// named thing is protected by what it IS, not by being listed.
        /// </summary>
        public static bool Artifact(Thing t) => t?.def != null && t.def.tradeability == Tradeability.None;

        /// <summary>The floor a random working needs to have settled on a piece.</summary>
        public const QualityCategory MinRolledQuality = QualityCategory.Good;

        /// <summary>
        /// Whether a chance roll may land on this piece at all. Magic follows
        /// craft: a working takes to a well-made blade and slides off a
        /// shoddy one, so the roll only ever looks at good quality or better.
        /// It is also the difference between "enchanted" meaning something
        /// and every third bandit carrying a shock club.
        ///
        /// No quality comp at all means no craft to speak of, and it fails
        /// the same test. That is deliberate: the pieces without quality are
        /// the odd utility items, not the swords and cuirasses.
        ///
        /// Only the RANDOM paths ask this. The enchanter's bench still takes
        /// whatever the party carries in and charges for it - a paid working
        /// is the player's decision, not the loot table's.
        /// </summary>
        public static bool GoodEnoughToRoll(Thing t) =>
            t.TryGetQuality(out QualityCategory quality) && quality >= MinRolledQuality;

        public static void MaybeEnchant(Thing thing, float chance)
        {
            if (thing == null || Artifact(thing) || !GoodEnoughToRoll(thing) || !Rand.Chance(chance))
            {
                return;
            }
            bool weapon = IsWeaponThing(thing);
            if (!weapon && !IsArmor(thing))
            {
                return;
            }
            Comp_TSC_Enchant comp = thing.TryGetComp<Comp_TSC_Enchant>();
            if (comp == null || comp.enchant != null)
            {
                return;
            }
            // Two pools, one roll: armor draws the passive workings and a
            // weapon draws the ones that DO something when a hit lands.
            List<TSC_EnchantDef> pool = new List<TSC_EnchantDef>();
            foreach (TSC_EnchantDef def in DefDatabase<TSC_EnchantDef>.AllDefsListForReading)
            {
                if (def.forWeapons == weapon)
                {
                    pool.Add(def);
                }
            }
            comp.enchant = pool.RandomElementWithFallback();
        }

        /// <summary>Total offset for a stat from everything the pawn is wearing.</summary>
        public static float WornOffset(Pawn pawn, StatDef stat)
        {
            float total = 0f;
            List<Apparel> worn = pawn.apparel?.WornApparel;
            if (worn == null)
            {
                return 0f;
            }
            for (int i = 0; i < worn.Count; i++)
            {
                TSC_EnchantDef enchant = worn[i].TryGetComp<Comp_TSC_Enchant>()?.enchant;
                if (enchant?.statOffsets == null)
                {
                    continue;
                }
                foreach (StatModifier offset in enchant.statOffsets)
                {
                    if (offset.stat == stat)
                    {
                        total += offset.value;
                    }
                }
            }
            return total;
        }

        /// <summary>Product of damage-dealt factors from everything the pawn is wearing.</summary>
        public static float WornDamageFactor(Pawn pawn)
        {
            float factor = 1f;
            List<Apparel> worn = pawn.apparel?.WornApparel;
            if (worn == null)
            {
                return 1f;
            }
            for (int i = 0; i < worn.Count; i++)
            {
                TSC_EnchantDef enchant = worn[i].TryGetComp<Comp_TSC_Enchant>()?.enchant;
                if (enchant != null)
                {
                    factor *= enchant.damageDealtFactor;
                }
            }
            return factor;
        }
    }

    /// <summary>Loot sets: chests, stashes, and quest rewards all flow through here.</summary>
    [HarmonyPatch(typeof(ThingSetMaker), nameof(ThingSetMaker.Generate), new[] { typeof(ThingSetMakerParams) })]
    public static class Patch_ThingSetMaker_Enchant
    {
        public static void Postfix(List<Thing> __result)
        {
            if (!TSC_RpgMode.Active || __result == null)
            {
                return;
            }
            for (int i = 0; i < __result.Count; i++)
            {
                TSC_Enchanter.MaybeEnchant(__result[i], TSC_Enchanter.LootChance);
            }
        }
    }

    /// <summary>Worn enemy gear: what they wear is what they drop.</summary>
    [HarmonyPatch(typeof(PawnApparelGenerator), nameof(PawnApparelGenerator.GenerateStartingApparelFor))]
    public static class Patch_PawnApparel_Enchant
    {
        public static void Postfix(Pawn pawn)
        {
            // OfPlayerSilentFail, not OfPlayer. Apparel is generated during
            // WORLD GENERATION too: FactionGenerator makes each faction's
            // leader as it creates the faction, and the player faction may not
            // exist yet when it does. OfPlayer logs "Could not find player
            // faction" in that window, which turned starting a new game into a
            // wall of red for every leader generated before us.
            //
            // Surfaced by adding Medieval Overhaul: more factions means more
            // leaders generated ahead of the player's own faction. The bug was
            // always there, just usually invisible.
            Faction player = Faction.OfPlayerSilentFail;
            if (!TSC_RpgMode.Active || pawn?.apparel == null
                || player == null || pawn.Faction == player || !pawn.RaceProps.Humanlike)
            {
                return;
            }
            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                TSC_Enchanter.MaybeEnchant(apparel, TSC_Enchanter.WornChance);
            }
        }
    }

    /// <summary>
    /// Carried enemy weapons: what they swing is what they drop, at the
    /// same rate their armor enchants at. The blade in a bandit's hand is
    /// the other half of the loot table.
    /// </summary>
    [HarmonyPatch(typeof(PawnWeaponGenerator), nameof(PawnWeaponGenerator.TryGenerateWeaponFor))]
    public static class Patch_PawnWeapon_Enchant
    {
        public static void Postfix(Pawn pawn)
        {
            // OfPlayerSilentFail for the same reason as the apparel patch:
            // weapons generate during world generation too, before the
            // player faction exists.
            Faction player = Faction.OfPlayerSilentFail;
            if (!TSC_RpgMode.Active || player == null
                || pawn?.equipment?.Primary == null
                || pawn.Faction == player || !pawn.RaceProps.Humanlike)
            {
                return;
            }
            TSC_Enchanter.MaybeEnchant(pawn.equipment.Primary, TSC_Enchanter.WornChance);
        }
    }

    /// <summary>
    /// The worn-only stat application, in the same place vanilla applies
    /// def-level gear offsets. Fast path: bail unless the stat is one an
    /// enchant can touch at all.
    /// </summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    public static class Patch_StatWorker_Enchant
    {
        public static void Postfix(StatRequest req, ref float __result, StatDef ___stat)
        {
            if (!TSC_Enchanter.EnchantableStats.Contains(___stat) || !(req.Thing is Pawn pawn))
            {
                return;
            }
            __result += TSC_Enchanter.WornOffset(pawn, ___stat);
        }
    }

    /// <summary>The stat tooltip owns up to the magic.</summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetExplanationUnfinalized))]
    public static class Patch_StatExplanation_Enchant
    {
        public static void Postfix(StatRequest req, ref string __result, StatDef ___stat)
        {
            if (!TSC_Enchanter.EnchantableStats.Contains(___stat) || !(req.Thing is Pawn pawn))
            {
                return;
            }
            float offset = TSC_Enchanter.WornOffset(pawn, ___stat);
            if (!Mathf.Approximately(offset, 0f))
            {
                __result += $"\n\nEnchanted gear: {___stat.Worker.ValueToString(offset, false, ToStringNumberSense.Offset)}";
            }
        }
    }

    /// <summary>
    /// The keen enchant: damage the wearer DEALS, melee and ranged alike,
    /// scaled at impact. Worn-only by construction - it reads the worn list
    /// at the moment the hit lands.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_TakeDamage_Enchant
    {
        /// <summary>Reentry guard: the shock rider must not proc itself.</summary>
        private static bool applyingRider;

        /// <summary>
        /// Weapon workings fire here, after the hit has actually landed:
        /// __result carries what got through, so a deflected blow procs
        /// nothing. dinfo.Weapon must be the wielded weapon's def - that is
        /// what ties the damage to the enchanted thing rather than to a
        /// fire, a trap, or our own rider (which carries no weapon).
        /// </summary>
        public static void Postfix(Thing __instance, DamageInfo dinfo, DamageWorker.DamageResult __result)
        {
            if (applyingRider || !TSC_RpgMode.Active || __result == null || __result.totalDamageDealt <= 0f)
            {
                return;
            }
            if (!(__instance is Pawn victim) || victim.Dead
                || !(dinfo.Instigator is Pawn attacker) || attacker == victim)
            {
                return;
            }
            ThingWithComps weapon = attacker.equipment?.Primary;
            if (weapon == null || dinfo.Weapon != weapon.def)
            {
                return;
            }
            TSC_EnchantDef enchant = weapon.TryGetComp<Comp_TSC_Enchant>()?.enchant;
            if (enchant == null || !enchant.forWeapons || !Rand.Chance(enchant.onHitChance))
            {
                return;
            }
            applyingRider = true;
            try
            {
                if (enchant.onHitDamage > 0f && enchant.onHitDamageDef != null)
                {
                    victim.TakeDamage(new DamageInfo(enchant.onHitDamageDef, enchant.onHitDamage,
                        0f, -1f, attacker));
                    if (victim.Spawned)
                    {
                        FleckMaker.ThrowMicroSparks(victim.DrawPos, victim.Map);
                    }
                }
                if (enchant.stunTicks > 0 && !victim.Dead)
                {
                    victim.stances?.stunner?.StunFor(enchant.stunTicks, attacker, addBattleLog: false);
                }
                if (enchant.onHitHediff != null && !victim.Dead)
                {
                    Hediff dose = victim.health.hediffSet.GetFirstHediffOfDef(enchant.onHitHediff)
                        ?? victim.health.AddHediff(enchant.onHitHediff);
                    if (enchant.onHitSeverity < 1f)
                    {
                        dose.Severity += enchant.onHitSeverity;
                    }
                }
            }
            finally
            {
                applyingRider = false;
            }
        }

        public static void Prefix(ref DamageInfo dinfo)
        {
            if (!TSC_RpgMode.Active || dinfo.Def == null || !dinfo.Def.harmsHealth)
            {
                return;
            }
            if (!(dinfo.Instigator is Pawn attacker) || attacker.apparel == null)
            {
                return;
            }
            float factor = TSC_Enchanter.WornDamageFactor(attacker);
            if (!Mathf.Approximately(factor, 1f))
            {
                dinfo.SetAmount(dinfo.Amount * factor);
            }
        }
    }
}
