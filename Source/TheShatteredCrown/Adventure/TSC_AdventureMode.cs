using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Adventure Mode: the procedural counterpart to the hand-crafted Act
    /// campaign. Same RPG layer (classes, spells, proficiencies, turn-based
    /// encounters), but the content is generated: guild contracts arrive on
    /// a cadence (TSC_ContractManager), and the long goal is the shard hunt.
    /// This ScenPart is the mode's marker - its presence in the scenario is
    /// what switches the systems on.
    /// </summary>
    public class ScenPart_TSC_AdventureSetup : ScenPart
    {
        public override void PostGameStart()
        {
            base.PostGameStart();
            // The first contract should be waiting before the first campfire:
            // an adventurer with nothing to do is a colonist.
            Find.World?.GetComponent<TSC_ContractManager>()?.KickstartFirstContract();
        }
    }

    /// <summary>
    /// True when the running game was started from the Adventure Mode
    /// scenario. Mirrors TSC_RpgMode.Active (which accepts EITHER scenario);
    /// this gate is for the systems only Adventure Mode runs - the contract
    /// generator and the shard hunt.
    /// </summary>
    public static class TSC_AdventureModeGate
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
                    if (part is ScenPart_TSC_AdventureSetup)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
