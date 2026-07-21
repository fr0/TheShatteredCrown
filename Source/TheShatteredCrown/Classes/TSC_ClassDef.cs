using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A companion class: abilities and proficiency points unlocked at CLASS
    /// level thresholds (bard, cleric, warden...). Applied when the character's
    /// pawn is created and re-checked on every class level gained. Ability
    /// grants are naturally idempotent; proficiency grants are tracked in the
    /// pawn's class record so they apply exactly once.
    /// </summary>
    public class TSC_ClassDef : Def
    {
        public List<TSC_ClassUnlock> unlocks = new List<TSC_ClassUnlock>();

        /// <summary>
        /// Proficiencies this class is trained in (three per class). Members get
        /// a bonus of 1 + classLevel/4 on these, on top of trained points and
        /// vanilla-skill synergy.
        /// </summary>
        public List<TSC_ProficiencyDef> proficiencies = new List<TSC_ProficiencyDef>();

        /// <summary>Grants every unlock at or below the given class level; returns human-readable gains.</summary>
        public List<string> ApplyTo(Pawn pawn, int level, TSC_ClassRecord record)
        {
            List<string> gained = new List<string>();
            if (pawn == null)
            {
                return gained;
            }
            for (int i = 0; i < unlocks.Count; i++)
            {
                TSC_ClassUnlock unlock = unlocks[i];
                if (level < unlock.level)
                {
                    continue;
                }
                if (unlock.ability != null && pawn.abilities != null && pawn.abilities.GetAbility(unlock.ability) == null)
                {
                    pawn.abilities.GainAbility(unlock.ability);
                    gained.Add($"New ability: {unlock.ability.LabelCap}");
                }
                if (unlock.proficiency != null && record != null)
                {
                    string key = $"{defName}:{i}";
                    if (!record.appliedGrants.Contains(key))
                    {
                        record.appliedGrants.Add(key);
                        TSC_ProgressionManager.Current.GrantProficiency(pawn, unlock.proficiency, unlock.proficiencyPoints, announce: false);
                        gained.Add($"{unlock.proficiency.LabelCap} +{unlock.proficiencyPoints}");
                    }
                }
            }
            return gained;
        }
    }

    public class TSC_ClassUnlock
    {
        public AbilityDef ability;
        public TSC_ProficiencyDef proficiency;
        public int proficiencyPoints = 1;
        public int level = 1;
    }
}
