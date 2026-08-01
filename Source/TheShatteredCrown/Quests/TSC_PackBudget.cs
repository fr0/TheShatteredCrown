using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The dungeon pack budget, shared by every generator whose layout can
    /// roll dormant insect clusters.
    ///
    /// Cluster rooms roll independently (each crypt gallery has its own
    /// chance), so a floor's nest count is unbounded in the tail - the
    /// cellars once came out wall-to-wall. This makes the budget a hard
    /// guarantee: group dormant insects and their hives into packs by
    /// proximity, keep the packs closest to the focus (the objective they
    /// should be guarding), remove the rest.
    /// </summary>
    public static class TSC_PackBudget
    {
        public static void Prune(Map map, int maxPacks, IntVec3 focus)
        {
            if (map == null || maxPacks < 0)
            {
                return;
            }
            List<Thing> members = new List<Thing>();
            foreach (Pawn pawn in new List<Pawn>(map.mapPawns.AllPawnsSpawned))
            {
                if (pawn.Faction == Faction.OfInsects
                    && pawn.GetComp<CompCanBeDormant>() is CompCanBeDormant dormant && !dormant.Awake)
                {
                    members.Add(pawn);
                }
            }
            // Hives go, every time, whatever the budget allows. A hive is not
            // an encounter, it is a tap: left standing it keeps making
            // megascarabs for as long as the map exists, which is why a floor
            // budgeted for two insect packs was reading as an infestation by
            // the time the party walked back through it. A dungeon is a fixed
            // thing to be dealt with, not a spawner to be outrun.
            foreach (Thing thing in new List<Thing>(map.listerThings.AllThings))
            {
                if (thing.def.defName == "Hive" && !thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
            if (members.Count == 0)
            {
                return;
            }
            // Greedy proximity grouping: anything within 9 cells of a pack
            // joins it. Cluster spawns are tight, so this is unambiguous.
            List<List<Thing>> packs = new List<List<Thing>>();
            foreach (Thing member in members)
            {
                List<Thing> home = null;
                foreach (List<Thing> pack in packs)
                {
                    foreach (Thing other in pack)
                    {
                        if (member.Position.InHorDistOf(other.Position, 9f))
                        {
                            home = pack;
                            break;
                        }
                    }
                    if (home != null)
                    {
                        break;
                    }
                }
                if (home == null)
                {
                    home = new List<Thing>();
                    packs.Add(home);
                }
                home.Add(member);
            }
            if (packs.Count <= maxPacks)
            {
                return;
            }
            packs.SortBy(pack => pack[0].Position.DistanceTo(focus));
            for (int i = maxPacks; i < packs.Count; i++)
            {
                foreach (Thing extra in packs[i])
                {
                    if (!extra.Destroyed)
                    {
                        extra.Destroy(DestroyMode.Vanish);
                    }
                }
            }
        }
    }

    /// <summary>
    /// XML-usable cap for contract sites whose layout carries cluster rooms
    /// (the shrine's moss-barrow galleries). Runs after the structure
    /// genstep; keeps the packs nearest map centre, where the objective is.
    /// </summary>
    public class GenStep_TSC_PackCap : GenStep
    {
        public int maxPacks = 2;

        public override int SeedPart => 493170258;

        public override void Generate(Map map, GenStepParams parms)
        {
            TSC_PackBudget.Prune(map, maxPacks, map.Center);
        }
    }
}
