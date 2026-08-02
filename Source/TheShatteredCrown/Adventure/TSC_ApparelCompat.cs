using System.Collections.Generic;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Deference for the hand/foot armor slots. Vanilla has no apparel that
    /// covers Hands or Feet, so this mod fills the gap with plate gauntlets
    /// and sabatons - but big medieval mods (Medieval Overhaul and friends)
    /// fill it too, usually better and craftably. Two competing gauntlet
    /// items in one catalogue is clutter, so: if ANY other mod ships apparel
    /// covering a slot, this mod's item for that slot stands down (hidden
    /// from the guild store; already-owned pieces are never confiscated).
    ///
    /// Detection is by CONTENT, not by mod name: a hardcoded packageId list
    /// would go stale the day some new armor mod releases. Checked once at
    /// startup, per slot - a mod that adds only gloves defers only the
    /// gauntlets.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TSC_ApparelCompat
    {
        public static readonly bool HandsCoveredByOtherMod;
        public static readonly bool FeetCoveredByOtherMod;

        static TSC_ApparelCompat()
        {
            HashSet<BodyPartGroupDef> handGroups = Groups("Hands", "LeftHand", "RightHand");
            HashSet<BodyPartGroupDef> footGroups = Groups("Feet");
            string handsSource = null;
            string feetSource = null;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!def.IsApparel || def.apparel?.bodyPartGroups == null
                    || def.defName.StartsWith("TSC_"))
                {
                    continue;
                }
                foreach (BodyPartGroupDef group in def.apparel.bodyPartGroups)
                {
                    if (handsSource == null && handGroups.Contains(group))
                    {
                        handsSource = Describe(def);
                    }
                    if (feetSource == null && footGroups.Contains(group))
                    {
                        feetSource = Describe(def);
                    }
                }
                if (handsSource != null && feetSource != null)
                {
                    break;
                }
            }
            HandsCoveredByOtherMod = handsSource != null;
            FeetCoveredByOtherMod = feetSource != null;
            if (HandsCoveredByOtherMod)
            {
                Log.Message($"[The Shattered Crown] Hand coverage found on {handsSource}; the guild store's gauntlets stand down.");
            }
            if (FeetCoveredByOtherMod)
            {
                Log.Message($"[The Shattered Crown] Foot coverage found on {feetSource}; the guild store's sabatons stand down.");
            }
        }

        /// <summary>
        /// Name the DEF, then its pack. Reporting only the pack is actively
        /// misleading when one mod patches another's content: Combat Extended
        /// adds Hands and Feet to vanilla plate armor, and the old message
        /// read "detected from Core", which points the reader at the wrong mod
        /// and hides the fact that the two pieces now collide on the same
        /// layer.
        /// </summary>
        private static string Describe(ThingDef def)
        {
            string pack = def.modContentPack?.Name;
            return pack.NullOrEmpty() ? def.defName : $"{def.defName} ({pack})";
        }

        private static HashSet<BodyPartGroupDef> Groups(params string[] names)
        {
            HashSet<BodyPartGroupDef> set = new HashSet<BodyPartGroupDef>();
            foreach (string name in names)
            {
                BodyPartGroupDef def = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail(name);
                if (def != null)
                {
                    set.Add(def);
                }
            }
            return set;
        }

        /// <summary>True when this def is one of ours whose slot another mod already serves.</summary>
        public static bool Deferred(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }
            if (def.defName == "TSC_Apparel_Gauntlets")
            {
                return HandsCoveredByOtherMod;
            }
            if (def.defName == "TSC_Apparel_Sabatons")
            {
                return FeetCoveredByOtherMod;
            }
            return false;
        }
    }
}
