using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A feat: one permanent, chosen improvement.
    ///
    /// One feat at character level 3 and every third level after. Class
    /// points come at every level from 2 up, so a feat never arrives alone:
    /// each feat level hands the player both decisions at once - what the
    /// character IS (a class level) and what they have LEARNED TO DO (the
    /// feat). Six feats by level 20.
    ///
    /// Most feats are nothing but a permanent hediff carrying stat offsets,
    /// which is deliberate: that shape costs no code, works identically in
    /// turn-based and real time for free, and shows up in the health tab
    /// where the player can read what their character actually has.
    /// </summary>
    public class TSC_FeatDef : Def
    {
        /// <summary>The passive effects, applied for good when the feat is taken.</summary>
        public HediffDef hediff;

        /// <summary>
        /// Class levels required. ALL must be met. Empty = general feat,
        /// available to anyone.
        ///
        /// A feat that modifies an ability must require at least the class
        /// level that GRANTS that ability, or the player can own the feat
        /// before the thing it changes.
        /// </summary>
        public List<TSC_FeatRequirement> requirements = new List<TSC_FeatRequirement>();

        /// <summary>Changes this feat makes to specific abilities, consulted by the ability comps at cast time.</summary>
        public List<TSC_FeatAbilityMod> abilityMods;

        /// <summary>Multiplier on every spell's energy cost (Efficient Casting).</summary>
        public float energyCostFactor = 1f;

        /// <summary>Flat proficiency bonuses (Light Fingers, Trailcraft). Counts everywhere the proficiency does.</summary>
        public List<TSC_FeatProfBonus> proficiencyBonuses;

        /// <summary>Sort/group label in the picker. Left null this reads "General".</summary>
        public string category;

        /// <summary>Ordering within a category; lower first.</summary>
        public int order;

        public string CategoryLabel => category.NullOrEmpty() ? "General" : category;

        public bool AvailableTo(Pawn pawn)
        {
            if (requirements == null || requirements.Count == 0)
            {
                return true;
            }
            TSC_ClassRecord record = TSC_ProgressionManager.Current?.RecordOf(pawn);
            if (record == null)
            {
                return false;
            }
            foreach (TSC_FeatRequirement requirement in requirements)
            {
                if (requirement?.classDef == null || record.LevelIn(requirement.classDef) < requirement.level)
                {
                    return false;
                }
            }
            return true;
        }

        public string RequirementLine()
        {
            if (requirements == null || requirements.Count == 0)
            {
                return null;
            }
            List<string> parts = new List<string>();
            foreach (TSC_FeatRequirement requirement in requirements)
            {
                if (requirement?.classDef != null)
                {
                    parts.Add($"{requirement.classDef.LabelCap} {requirement.level}");
                }
            }
            return parts.Count == 0 ? null : "Requires " + string.Join(", ", parts);
        }
    }

    public class TSC_FeatRequirement
    {
        public TSC_ClassDef classDef;
        public int level = 1;
    }

    public static class TSC_Feats
    {
        /// <summary>One feat at character level 3, and every third level after.</summary>
        public const int LevelsPerFeat = 3;

        public static int FeatsEarnedAt(int characterLevel)
        {
            return Mathf.Max(0, characterLevel / LevelsPerFeat);
        }

        public static List<TSC_FeatDef> Taken(Pawn pawn)
        {
            List<TSC_FeatDef> taken = new List<TSC_FeatDef>();
            TSC_ClassRecord record = TSC_ProgressionManager.Current?.RecordOf(pawn);
            if (record?.feats == null)
            {
                return taken;
            }
            foreach (string defName in record.feats)
            {
                TSC_FeatDef def = DefDatabase<TSC_FeatDef>.GetNamedSilentFail(defName);
                if (def != null)
                {
                    taken.Add(def);
                }
            }
            return taken;
        }

        /// <summary>Cheap enough to call from stat hooks: string compare, no def lookup.</summary>
        public static bool Has(Pawn pawn, string featDefName)
        {
            TSC_ClassRecord record = TSC_ProgressionManager.Current?.RecordOf(pawn);
            return record?.feats != null && record.feats.Contains(featDefName);
        }

        public static int Pending(Pawn pawn)
        {
            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            if (pawn == null || progression == null)
            {
                return 0;
            }
            TSC_ClassRecord record = progression.RecordOf(pawn);
            int owed = FeatsEarnedAt(progression.LevelOf(pawn)) - (record.feats?.Count ?? 0);
            return Mathf.Max(0, owed);
        }

        /// <summary>Feats this pawn could take right now: qualified and not already held.</summary>
        public static List<TSC_FeatDef> ChoicesFor(Pawn pawn)
        {
            List<TSC_FeatDef> choices = new List<TSC_FeatDef>();
            TSC_ClassRecord record = TSC_ProgressionManager.Current?.RecordOf(pawn);
            foreach (TSC_FeatDef def in DefDatabase<TSC_FeatDef>.AllDefsListForReading)
            {
                if (record?.feats != null && record.feats.Contains(def.defName))
                {
                    continue;
                }
                if (def.AvailableTo(pawn))
                {
                    choices.Add(def);
                }
            }
            // Class feats first, General last: everything in this list is
            // already qualified-for, and the class options are the rarer,
            // build-defining picks - they should not sit below ten generics
            // the player has scrolled past five times before.
            choices.SortBy(d => d.requirements.NullOrEmpty() ? 1 : 0, d => d.CategoryLabel, d => d.order);
            return choices;
        }

        public static void Take(Pawn pawn, TSC_FeatDef def)
        {
            if (pawn == null || def == null || Pending(pawn) <= 0)
            {
                return;
            }
            TSC_ClassRecord record = TSC_ProgressionManager.Current.RecordOf(pawn);
            if (record.feats == null)
            {
                record.feats = new List<string>();
            }
            if (record.feats.Contains(def.defName))
            {
                return;
            }
            record.feats.Add(def.defName);
            ApplyHediffs(pawn);
            Messages.Message($"{pawn.LabelShortCap} takes the {def.label} feat.",
                pawn, MessageTypeDefOf.PositiveEvent, historical: false);
        }

        /// <summary>
        /// Make the pawn's hediffs match their feat list.
        ///
        /// Feat hediffs are permanent and saved with the pawn, so this is
        /// normally a no-op - it exists so a feat taken before its hediff def
        /// existed, or a pawn who lost hediffs some other way, repairs itself
        /// rather than silently losing the feat's effects.
        /// </summary>
        public static void ApplyHediffs(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return;
            }
            foreach (TSC_FeatDef def in Taken(pawn))
            {
                if (def.hediff != null && !pawn.health.hediffSet.HasHediff(def.hediff))
                {
                    pawn.health.AddHediff(def.hediff);
                }
            }
        }
    }

}
