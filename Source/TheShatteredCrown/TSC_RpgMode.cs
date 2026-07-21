using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The RPG layer (classes, XP, proficiencies, the Adventurer tab, physical
    /// proficiency interactions) exists only in games started from this mod's
    /// scenario, detected by its ScenPart. Quest/NPC content is inherently
    /// scenario-gated already; these are the systems that would otherwise leak
    /// into every save with the mod installed.
    /// </summary>
    public static class TSC_RpgMode
    {
        public static bool Active
        {
            get
            {
                Scenario scenario = Current.Game?.Scenario;
                if (scenario == null)
                {
                    return false;
                }
                foreach (ScenPart part in scenario.AllParts)
                {
                    if (part is ScenPart_TSC_IntroSetup)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
