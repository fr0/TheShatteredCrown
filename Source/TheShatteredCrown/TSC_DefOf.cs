using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    [DefOf]
    public static class TSC_DefOf
    {
        public static JobDef TSC_TalkTo;
        public static JobDef TSC_InitiateTalk;
        public static JobDef TSC_LureEttersnap;
        public static JobDef TSC_PickChest;
        public static JobDef TSC_ForceChest;
        public static JobDef TSC_Perform;
        public static JobDef TSC_UseCheckSpot;
        public static JobDef TSC_BeatFlames;
        public static HediffDef TSC_Hediff_AdventurerLevel;
        public static HediffDef TSC_Hediff_Performed;
        public static HediffDef TSC_Hediff_Summoned;
        public static PawnKindDef TSC_Ettersnap;
        public static ThingDef TSC_RemembranceMoss;
        public static ThingDef TSC_MossCache;
        public static ThingDef TSC_CellarHatch;
        public static ThingDef TSC_CampCrate_Provisions;
        public static ThingDef TSC_CampCrate_Sundries;
        public static NeedDef TSC_Need_Energy;
        public static TSC_ProficiencyDef TSC_Prof_Thievery;
        public static TSC_ProficiencyDef TSC_Prof_Athletics;
        public static TSC_ProficiencyDef TSC_Prof_Performance;

        static TSC_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(TSC_DefOf));
        }
    }
}
