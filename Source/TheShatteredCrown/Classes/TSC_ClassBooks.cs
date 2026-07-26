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
    /// Class manuals: studying the book UNLOCKS the class for the whole
    /// company - it becomes a "(new class)" choice in the level-up dialog,
    /// where beginning it costs a class level. It does NOT grant a level
    /// directly (user decision), and neither do mentors' in-story teachings.
    /// Consumed on study; a second copy of an unlocked manual is just paper.
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
            if (TSC_ProgressionManager.Current.IsClassUnlocked(Props.classDef))
            {
                return $"The company already keeps the {Props.classDef.label}'s teachings.";
            }
            return true;
        }

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            TSC_ProgressionManager.Current.UnlockClass(Props.classDef, usedBy);
        }
    }
}
