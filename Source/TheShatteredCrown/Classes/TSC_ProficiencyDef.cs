using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// An adventure proficiency (Lore, Thievery, Nature...) - deliberately
    /// separate from RimWorld's labor skills. Points come from class levels and
    /// story moments; dialogue checks and quest interactions consume them.
    /// A related vanilla skill (if set) contributes a synergy bonus of
    /// skill level / 5, so a silver-tongued Social pawn is never Persuasion 0.
    /// </summary>
    public class TSC_ProficiencyDef : Def
    {
        public SkillDef relatedSkill;

        public int SynergyBonus(Pawn pawn)
        {
            if (relatedSkill == null || pawn?.skills == null)
            {
                return 0;
            }
            return pawn.skills.GetSkill(relatedSkill).Level / 5;
        }
    }

    /// <summary>Per-pawn proficiency points, saved alongside the class record.</summary>
    public class TSC_ProficiencySet : IExposable
    {
        public List<TSC_ProficiencyDef> defs = new List<TSC_ProficiencyDef>();
        public List<int> values = new List<int>();

        public int PointsIn(TSC_ProficiencyDef def)
        {
            int index = defs.IndexOf(def);
            return index >= 0 ? values[index] : 0;
        }

        public void Add(TSC_ProficiencyDef def, int points)
        {
            int index = defs.IndexOf(def);
            if (index >= 0)
            {
                values[index] += points;
            }
            else
            {
                defs.Add(def);
                values.Add(points);
            }
        }

        public string Summary(Pawn pawn)
        {
            if (defs.Count == 0)
            {
                return null;
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < defs.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                int synergy = defs[i].SynergyBonus(pawn);
                sb.Append($"{defs[i].label} {values[i]}");
                if (synergy > 0)
                {
                    sb.Append($" (+{synergy})");
                }
            }
            return sb.ToString();
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref defs, "defs", LookMode.Def);
            Scribe_Collections.Look(ref values, "values", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                defs = defs ?? new List<TSC_ProficiencyDef>();
                values = values ?? new List<int>();
            }
        }
    }
}
