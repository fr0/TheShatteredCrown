using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Needs-tab mirror of the spell energy pool. The pool itself lives in
    /// TSC_ProgressionManager; this need just displays it (0..1 of max), so the
    /// setter is a no-op and NeedInterval does nothing.
    /// </summary>
    public class Need_TSC_Energy : Need
    {
        public Need_TSC_Energy(Pawn newPawn) : base(newPawn)
        {
        }

        public override float CurLevel
        {
            get
            {
                TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
                float max = progression.MaxEnergy(pawn);
                return max <= 0f ? 0f : progression.EnergyOf(pawn) / max;
            }
            set
            {
            }
        }

        public override int GUIChangeArrow => pawn.Spawned && !pawn.Awake() ? 1 : 0;

        public override void NeedInterval()
        {
        }

        public override string GetTipString()
        {
            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            return $"{def.LabelCap}: {progression.EnergyOf(pawn):F0} / {progression.MaxEnergy(pawn):F0}\n\n{def.description}";
        }
    }
}
