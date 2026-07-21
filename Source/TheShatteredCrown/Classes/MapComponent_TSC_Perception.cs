using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Passive Perception on the map: when a colonist comes near a hostile trap,
    /// the party's best passive Perception (5 + bonus) is checked against the
    /// trap's DC - success announces it with a camera ping; failure stays silent.
    /// One check per trap, using the best-qualified nearby colonist, so the party
    /// scout matters. (Informational only: it does not alter trap behavior.)
    /// </summary>
    public class MapComponent_TSC_Perception : MapComponent
    {
        private HashSet<Thing> checkedTraps = new HashSet<Thing>();

        private const int CheckIntervalTicks = 150;
        private const float NoticeRange = 9.9f;
        private const int TrapDc = 9;

        public MapComponent_TSC_Perception(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            TSC_ProficiencyDef perception = DefDatabase<TSC_ProficiencyDef>.GetNamedSilentFail("TSC_Prof_Perception");
            if (perception == null)
            {
                return;
            }
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            if (colonists.Count == 0)
            {
                return;
            }
            List<Building> traps = map.listerBuildings.allBuildingsNonColonist.FindAll(IsHostileTrap);
            if (traps.Count == 0)
            {
                return;
            }
            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            foreach (Building trap in traps)
            {
                if (checkedTraps.Contains(trap) || trap.Destroyed)
                {
                    continue;
                }
                Pawn best = null;
                int bestBonus = -1;
                foreach (Pawn colonist in colonists)
                {
                    if (colonist.Downed || !colonist.Position.InHorDistOf(trap.Position, NoticeRange))
                    {
                        continue;
                    }
                    if (!GenSight.LineOfSight(colonist.Position, trap.Position, map, skipFirstCell: true))
                    {
                        continue;
                    }
                    int bonus = progression.EffectiveProficiency(colonist, perception);
                    if (bonus > bestBonus)
                    {
                        bestBonus = bonus;
                        best = colonist;
                    }
                }
                if (best == null)
                {
                    continue;
                }
                checkedTraps.Add(trap);
                if (5 + bestBonus >= TrapDc)
                {
                    Messages.Message(
                        $"{best.LabelShortCap} notices {trap.LabelShort} ahead! (passive perception {5 + bestBonus} vs {TrapDc})",
                        new LookTargets(trap), MessageTypeDefOf.CautionInput, historical: false);
                }
            }
        }

        private static bool IsHostileTrap(Building building)
        {
            if (building.Faction == Faction.OfPlayer)
            {
                return false;
            }
            return building.def.building != null && building.def.building.isTrap;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref checkedTraps, "checkedTraps", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                checkedTraps = checkedTraps ?? new HashSet<Thing>();
                checkedTraps.RemoveWhere(t => t == null);
            }
        }
    }
}
