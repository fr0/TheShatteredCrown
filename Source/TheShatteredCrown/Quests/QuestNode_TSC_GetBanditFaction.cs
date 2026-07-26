using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Stores the mod's hidden medieval bandit faction in the slate,
    /// OVERRIDING whatever faction the vanilla site nodes rolled - quest-site
    /// defenders then generate exclusively from medieval pawn kinds (no
    /// gun-toting pirates in a medieval story). Fails the test run (quest
    /// won't fire) if the faction is absent, e.g. a world created before the
    /// faction existed.
    /// </summary>
    public class QuestNode_TSC_GetBanditFaction : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> storeAs;

        protected override bool TestRunInt(Slate slate)
        {
            return TryStore(slate);
        }

        protected override void RunInt()
        {
            TryStore(QuestGen.slate);
        }

        private bool TryStore(Slate slate)
        {
            // Find-or-create: a missing faction instance no longer kills the
            // quest (it used to fail the test run, which silently starved
            // Adventure Mode of bandit-camp contracts on affected worlds).
            Faction faction = TSC_BanditFactionUtility.Get();
            if (faction == null)
            {
                return false;
            }
            slate.Set(storeAs.GetValue(slate), faction);
            return true;
        }
    }
}
