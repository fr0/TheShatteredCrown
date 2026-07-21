using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    public class CompProperties_TSC_LearnClassUse : CompProperties_UseEffect
    {
        public TSC_ClassDef classDef;

        public CompProperties_TSC_LearnClassUse()
        {
            compClass = typeof(CompUseEffect_TSC_LearnClass);
        }
    }

    /// <summary>
    /// Class manuals: studying the book teaches the class (level 1, like a
    /// mentor's teaching) and consumes it. Scenario-gated with a reason string,
    /// and refuses pawns who already know the class, so a duplicate book stays
    /// useful for another colonist.
    /// </summary>
    public class CompUseEffect_TSC_LearnClass : CompUseEffect
    {
        public CompProperties_TSC_LearnClassUse Props => (CompProperties_TSC_LearnClassUse)props;

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (Props.classDef == null)
            {
                return false;
            }
            if (!TSC_RpgMode.Active)
            {
                return "The old arts wake only on the crown's road.";
            }
            if (!p.IsFreeColonist)
            {
                return "Only a free colonist can study it.";
            }
            TSC_ClassRecord record = TSC_ProgressionManager.Current.RecordOf(p);
            if (record != null && record.Has(Props.classDef))
            {
                return $"{p.LabelShortCap} already knows the {Props.classDef.label}'s art.";
            }
            return true;
        }

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            TSC_ProgressionManager.Current.LearnClass(usedBy, Props.classDef);
        }
    }
}
