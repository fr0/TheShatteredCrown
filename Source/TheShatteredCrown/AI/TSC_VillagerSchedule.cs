using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// The villager routine: wander near the WORK spot through the day
    /// (field, forge yard, village square) and go home at night (an indoors
    /// cell; the small wander radius plus the walls keeps them inside).
    /// Night is 21:00-06:00 local time. Replaces LordJob_DefendPoint for all
    /// village-genstep residents, so camp and grove NPCs get the routine too.
    /// </summary>
    public class LordJob_TSC_Villager : LordJob
    {
        private IntVec3 homeSpot;
        private IntVec3 workSpot;
        private bool stayHome;

        public LordJob_TSC_Villager()
        {
        }

        public LordJob_TSC_Villager(IntVec3 homeSpot, IntVec3 workSpot, bool stayHome = false)
        {
            this.homeSpot = homeSpot;
            this.workSpot = workSpot;
            this.stayHome = stayHome;
        }

        private bool IsNight
        {
            get
            {
                int hour = GenLocalDate.HourOfDay(lord.Map);
                return hour >= 21 || hour < 6;
            }
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            // Day: vanilla WanderClose (naps only as a VeryTired safety net).
            // Night: our variant that beds down at merely Tired.
            DutyDef nightDuty = DefDatabase<DutyDef>.GetNamedSilentFail("TSC_Duty_VillagerNight") ?? DutyDefOf.WanderClose;
            // Homebodies keep to the hearth by day too: home spot, tight
            // radius so the walls hold them (vanilla duty = day rules,
            // emergency naps only).
            IntVec3 daySpot = stayHome ? homeSpot : workSpot;
            float dayRadius = stayHome ? 2.5f : 7f;
            LordToil_TSC_WanderNear day = new LordToil_TSC_WanderNear(daySpot, dayRadius, DutyDefOf.WanderClose);
            LordToil_TSC_WanderNear night = new LordToil_TSC_WanderNear(homeSpot, 2f, nightDuty);
            graph.AddToil(day);
            graph.AddToil(night);

            Transition toNight = new Transition(day, night);
            toNight.AddTrigger(new Trigger_TickCondition(() => IsNight));
            graph.AddTransition(toNight);

            Transition toDay = new Transition(night, day);
            toDay.AddTrigger(new Trigger_TickCondition(() => !IsNight));
            graph.AddTransition(toDay);
            return graph;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref homeSpot, "homeSpot");
            Scribe_Values.Look(ref workSpot, "workSpot");
            Scribe_Values.Look(ref stayHome, "stayHome", defaultValue: false);
        }
    }

    public class LordToil_TSC_WanderNear : LordToil
    {
        private readonly IntVec3 spot;
        private readonly float radius;
        private readonly DutyDef duty;

        public LordToil_TSC_WanderNear(IntVec3 spot, float radius, DutyDef duty)
        {
            this.spot = spot;
            this.radius = radius;
            this.duty = duty;
        }

        public override void UpdateAllDuties()
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                lord.ownedPawns[i].mindState.duty = new PawnDuty(duty, spot)
                {
                    wanderRadius = radius,
                };
            }
        }
    }
}
