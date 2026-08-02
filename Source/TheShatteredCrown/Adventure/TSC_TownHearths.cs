using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Keeping the town's fires in.
    ///
    /// A village smith with a cold forge is a man standing next to a
    /// decoration. Nobody in a settlement is simulated well enough to go and
    /// fetch wood - the residents are spawned to stand at their posts and
    /// talk, not to run a household - so anything that burns fuel burns out,
    /// and a town the party comes back to a season later is a dark street of
    /// dead torches and an unlit smithy.
    ///
    /// So the town keeps its own fires: every refuelable thing that belongs
    /// to somebody other than the player is topped up while the party is on
    /// the map. It is a cosmetic lie in the same spirit as the smith having
    /// stock without anyone hauling it in.
    ///
    /// Pointedly NOT the player's: a colony still buys its own fuel, and a
    /// campfire the party lights on a village map still burns down.
    /// </summary>
    public class MapComponent_TSC_TownHearths : MapComponent
    {
        /// <summary>Twice a game hour is plenty: fuel burns slowly and nobody watches it drain.</summary>
        private const int Interval = 1250;

        /// <summary>Below this fraction it gets a fill, so the flame never gutters out.</summary>
        private const float Floor = 0.35f;

        public MapComponent_TSC_TownHearths(Map map) : base(map)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Stoke(); // lit on arrival, not a minute after the party walks in
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval == 0)
            {
                Stoke();
            }
        }

        private void Stoke()
        {
            if (!TSC_RpgMode.Active)
            {
                return;
            }
            // Non-colonist buildings only, which is the town's own property by
            // definition and cheap to walk: a settlement map has a few dozen.
            foreach (Building building in map.listerBuildings.allBuildingsNonColonist)
            {
                if (building.Faction == Faction.OfPlayer)
                {
                    continue;
                }
                CompRefuelable fuel = building.TryGetComp<CompRefuelable>();
                if (fuel == null || fuel.Props == null || fuel.FuelPercentOfMax >= Floor)
                {
                    continue;
                }
                fuel.Refuel(fuel.Props.fuelCapacity);
                // Some fuelled things (a forge, a lamp) also want switching
                // on; a refuelled thing that is flicked off is still dark.
                CompFlickable switchable = building.TryGetComp<CompFlickable>();
                if (switchable != null && !switchable.SwitchIsOn)
                {
                    switchable.SwitchIsOn = true;
                }
            }
        }
    }
}
