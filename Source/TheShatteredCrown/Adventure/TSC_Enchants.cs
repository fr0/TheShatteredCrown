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
            sb.Append($"Enchanted ({enchant.label}), while worn:");
            if (enchant.statOffsets != null)
            {
                foreach (StatModifier offset in enchant.statOffsets)
                {
                    sb.Append($"\n  {offset.stat.LabelCap} {offset.stat.Worker.ValueToString(offset.value, false, ToStringNumberSense.Offset)}");
                }
            }
            if (!Mathf.Approximately(enchant.damageDealtFactor, 1f))
            {
                sb.Append($"\n  Damage dealt x{enchant.damageDealtFactor.ToStringPercent()}");
            }
            if (!enchant.flavor.NullOrEmpty())
            {
                sb.Append($"\n{enchant.flavor}");
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
                if (def.IsApparel && def.comps != null
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

        public static void MaybeEnchant(Thing thing, float chance)
        {
            if (thing == null || !Rand.Chance(chance) || !IsArmor(thing))
            {
                return;
            }
            Comp_TSC_Enchant comp = thing.TryGetComp<Comp_TSC_Enchant>();
            if (comp == null || comp.enchant != null)
            {
                return;
            }
            comp.enchant = DefDatabase<TSC_EnchantDef>.AllDefsListForReading.RandomElementWithFallback();
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
