using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Points at the strongbox once the floor is clear.
    ///
    /// A delve contract asks the party to recover a guild strongbox from a
    /// dungeon, and the box is one small iron thing on a forty-by-forty map
    /// of rooms. Killing everything is the hard part; then the objective
    /// turns into a search of empty corridors, which is the least
    /// interesting minute in the contract. The moment nothing hostile is
    /// left standing, the guild seal is close enough to see: this sends a
    /// letter whose LookTargets is the box itself, so one click puts the
    /// camera on it and the arrow marks it.
    ///
    /// Every map gets one of these; the box check is the gate, and it costs
    /// one lister lookup every few seconds until it fires once.
    /// </summary>
    public class MapComponent_TSC_StrongboxBeacon : MapComponent
    {
        private const int Interval = 120;

        private bool announced;

        public MapComponent_TSC_StrongboxBeacon(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (announced || Find.TickManager.TicksGame % Interval != 0)
            {
                return;
            }
            if (map.mapPawns.FreeColonistsSpawnedCount == 0)
            {
                return; // nobody here to be told
            }
            Thing box = FindBox();
            if (box == null)
            {
                return;
            }
            // Downed enemies do not count: a floor of unconscious bandits is
            // won, and vanilla's own "any hostile active threat" answers the
            // same question the player is asking ("is it over?").
            if (GenHostility.AnyHostileActiveThreatToPlayer(map, countDormantPawnsAsHostile: true))
            {
                return;
            }
            announced = true;
            Find.LetterStack.ReceiveLetter(
                "The seal is here",
                "Nothing on this floor is still fighting. In the quiet, the guild's own mark turns up where "
                + "it has been the whole time: iron bands, brass, and a wax seal nobody down here had any "
                + "business breaking.\n\nThe strongbox is marked.",
                LetterDefOf.PositiveEvent, box);
        }

        /// <summary>
        /// The box, wherever it is: on the floor, or inside the chest a
        /// layout dropped it in. A letter that points at a container the box
        /// is sitting inside is still a letter that ends the search.
        /// </summary>
        private Thing FindBox()
        {
            ThingDef boxDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_GuildStrongbox");
            if (boxDef == null)
            {
                return null;
            }
            List<Thing> loose = map.listerThings.ThingsOfDef(boxDef);
            if (loose.Count > 0)
            {
                return loose[0];
            }
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (!(thing is IThingHolder holder) || thing.Faction == Faction.OfPlayer)
                {
                    continue;
                }
                ThingOwner contents = holder.GetDirectlyHeldThings();
                for (int i = 0; contents != null && i < contents.Count; i++)
                {
                    if (contents[i]?.def == boxDef)
                    {
                        return thing;
                    }
                }
            }
            return null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref announced, "strongboxBeaconAnnounced");
        }
    }
}
