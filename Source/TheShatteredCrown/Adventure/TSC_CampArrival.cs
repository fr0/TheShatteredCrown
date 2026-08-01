using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Making camp on the world map should produce a camp.
    ///
    /// Vanilla's "set up camp" drops the caravan onto a bare 200x200 map
    /// and leaves the party standing in a field, which is a strange result
    /// for an action called making camp - especially in a mod where the
    /// company carries bedrolls and a bard wants a fire to perform at.
    /// This lights the fire and puts the party to work pitching their own
    /// bedrolls around it.
    ///
    /// NOT a GenStep, which is where this obviously belongs and cannot go:
    /// gensteps run while the map is being built, and the caravan's pawns -
    /// and therefore the bedrolls, which live in their inventories - do not
    /// arrive until afterwards. So it waits for the party to actually be
    /// standing there.
    ///
    /// The bedrolls are the party's OWN, deployed through the existing camp
    /// system: nothing is conjured, a company that packed none sleeps on
    /// the ground, and packing up when the caravan reforms already works.
    /// </summary>
    public class MapComponent_TSC_CampArrival : MapComponent
    {
        private const int Interval = 60;
        private bool checkedMap;
        private bool isCamp;
        private bool done;

        public MapComponent_TSC_CampArrival(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (done || Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            if (!checkedMap)
            {
                checkedMap = true;
                isCamp = map.Parent != null && map.Parent.def?.defName == "Camp";
            }
            if (!isCamp)
            {
                done = true;
                return;
            }
            // Wait for the party to be on the ground, and for the arrival
            // not to be an ambush: a camp interrupted by a fight sets
            // itself up later, by hand, like anyone would.
            if (map.mapPawns.FreeColonistsSpawnedCount == 0 || !TSC_CampDeploy.CombatQuiet(map))
            {
                return;
            }
            done = true;
            IntVec3 center = CampCenter();
            Thing fire = LightFire(center);
            // Bedrolls go AROUND the fire, never on it: the fire's footprint
            // (plus a cell of breathing room, so nobody sleeps with their
            // head in the coals) is excluded from the ring.
            CellRect hearth = fire != null
                ? fire.OccupiedRect().ExpandedBy(1)
                : CellRect.SingleCell(center);
            TSC_CampDeploy.OrderDeploy(map, fire?.Position ?? center, hearth);
        }

        /// <summary>Where the party is standing, near enough: the middle of the arrivals.</summary>
        private IntVec3 CampCenter()
        {
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            if (colonists.Count == 0)
            {
                return map.Center;
            }
            int x = 0;
            int z = 0;
            foreach (Pawn pawn in colonists)
            {
                x += pawn.Position.x;
                z += pawn.Position.z;
            }
            IntVec3 middle = new IntVec3(x / colonists.Count, 0, z / colonists.Count);
            return middle.InBounds(map) ? middle : map.Center;
        }

        /// <summary>
        /// A real campfire with a real fuel load: it burns down, it can be
        /// refuelled, and it is gone by morning if nobody feeds it. A
        /// permanent free fire on every camp would be a small exploit and a
        /// worse piece of fiction.
        /// </summary>
        private Thing LightFire(IntVec3 near)
        {
            ThingDef fireDef = ThingDefOf.Campfire;
            if (fireDef == null)
            {
                return null;
            }
            IntVec3 cell = IntVec3.Invalid;
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(near, 12f, useCenter: true))
            {
                if (!candidate.InBounds(map) || !candidate.Standable(map)
                    || candidate.GetEdifice(map) != null || candidate.GetTerrain(map).IsWater
                    || candidate.Roofed(map))
                {
                    continue;
                }
                bool clear = true;
                foreach (IntVec3 part in GenAdj.OccupiedRect(candidate, Rot4.North, fireDef.size))
                {
                    if (!part.InBounds(map) || !part.Standable(map) || part.GetEdifice(map) != null)
                    {
                        clear = false;
                        break;
                    }
                }
                if (clear)
                {
                    cell = candidate;
                    break;
                }
            }
            if (!cell.IsValid)
            {
                return null;
            }
            Thing fire = GenSpawn.Spawn(ThingMaker.MakeThing(fireDef), cell, map);
            fire.SetFaction(Faction.OfPlayer);
            // Enough wood for an evening, taken as read: the party gathered
            // it on the way in.
            CompRefuelable refuel = fire.TryGetComp<CompRefuelable>();
            refuel?.Refuel(refuel.Props.fuelCapacity * 0.5f);
            Messages.Message("Camp: a fire is lit, and the company sets about pitching bedrolls.",
                fire, MessageTypeDefOf.NeutralEvent, historical: false);
            return fire;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref done, "campArrivalDone");
        }
    }
}
