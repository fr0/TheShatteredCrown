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
        /// <summary>Dev-only: forces the RPG layer on in any game this session
        /// (toggled by a debug action; static, never saved).</summary>
        public static bool debugOverride;

        // Cached per Game: this gate sits in front of nearly every tick sweep,
        // damage patch, and per-frame UI hook in the mod, and the scenario of
        // a running game never changes - but walking scenario.AllParts (an
        // enumerator allocation plus type checks) on every call meant the
        // mod's cheapest question was one of its most-computed answers.
        private static Game cachedGame;
        private static bool cachedActive;

        public static bool Active
        {
            get
            {
                if (debugOverride)
                {
                    return true;
                }
                Game game = Current.Game;
                // No caching until the scenario is assigned: a Game exists for
                // a moment before its Scenario does (new-game setup), and a
                // false locked in during that window would shut the mod off
                // for the whole session.
                if (game?.Scenario == null)
                {
                    return false;
                }
                if (!ReferenceEquals(game, cachedGame))
                {
                    cachedGame = game;
                    cachedActive = ComputeActive(game);
                }
                return cachedActive;
            }
        }

        private static bool ComputeActive(Game game)
        {
            Scenario scenario = game.Scenario;
            if (scenario == null)
            {
                return false;
            }
            foreach (ScenPart part in scenario.AllParts)
            {
                // Either of the mod's scenarios: the hand-crafted campaign
                // (Lone Adventurer) or procedural Adventure Mode.
                if (part is ScenPart_TSC_IntroSetup || part is ScenPart_TSC_AdventureSetup)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
