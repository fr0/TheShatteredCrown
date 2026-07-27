using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    public class CompProperties_TSC_Kingsblade : CompProperties
    {
        public HediffDef hediff;
        /// <summary>The crown it belongs to: severity tracks how much of this the company carries.</summary>
        public ThingDef shardDef;
        public int maxShards = 9;

        public CompProperties_TSC_Kingsblade()
        {
            compClass = typeof(Comp_TSC_Kingsblade);
        }
    }

    /// <summary>
    /// The Kingsblade remembers what it belonged to. It is a good sword in
    /// anyone's hand and nothing more - until the crown starts coming back
    /// together, and then it wakes a piece at a time: severity on the
    /// wielder's hediff equals the number of crown shards the company is
    /// carrying, so the blade earned in Act 2 is still getting stronger in
    /// Act 5. Drop it, or lose the shards, and it goes quiet again.
    /// </summary>
    public class Comp_TSC_Kingsblade : ThingComp
    {
        private const int CheckIntervalTicks = 120;
        private int lastShards = -1;

        public CompProperties_TSC_Kingsblade Props => (CompProperties_TSC_Kingsblade)props;

        public override void CompTick()
        {
            base.CompTick();
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }
            Pawn wielder = (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            if (wielder == null || Props.hediff == null)
            {
                lastShards = -1;
                return;
            }
            int shards = CountShards(wielder);
            if (shards == lastShards)
            {
                return;
            }
            lastShards = shards;
            Hediff existing = wielder.health?.hediffSet?.GetFirstHediffOfDef(Props.hediff);
            if (shards <= 0)
            {
                if (existing != null)
                {
                    wielder.health.RemoveHediff(existing);
                }
                return;
            }
            if (existing == null)
            {
                existing = HediffMaker.MakeHediff(Props.hediff, wielder);
                existing.Severity = shards;
                wielder.health.AddHediff(existing);
                return;
            }
            existing.Severity = shards;
        }

        /// <summary>
        /// Shards the COMPANY holds, not just the wielder: the sword answers
        /// to the crown being gathered, and who happens to have which piece
        /// in their pack is bookkeeping.
        /// </summary>
        private int CountShards(Pawn wielder)
        {
            ThingDef shardDef = Props.shardDef;
            if (shardDef == null)
            {
                return 0;
            }
            int count = 0;
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (!SameCompany(wielder, pawn))
                {
                    continue;
                }
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        if (thing.def == shardDef)
                        {
                            count += thing.stackCount;
                        }
                    }
                }
                if (pawn.carryTracker?.CarriedThing?.def == shardDef)
                {
                    count += pawn.carryTracker.CarriedThing.stackCount;
                }
            }
            // Shards stashed on the ground where the wielder stands count too
            // (a party that empties its packs at camp does not un-crown the
            // sword), but only on the wielder's own map.
            Map map = wielder.MapHeld;
            if (map != null)
            {
                List<Thing> onMap = map.listerThings.ThingsOfDef(shardDef);
                for (int i = 0; i < onMap.Count; i++)
                {
                    if (onMap[i].Position.InHorDistOf(wielder.PositionHeld, 30f))
                    {
                        count += onMap[i].stackCount;
                    }
                }
            }
            return UnityEngine.Mathf.Clamp(count, 0, Props.maxShards);
        }

        private static bool SameCompany(Pawn a, Pawn b)
        {
            if (a == b)
            {
                return true;
            }
            Caravan caravanA = a.GetCaravan();
            if (caravanA != null)
            {
                return b.GetCaravan() == caravanA;
            }
            return a.MapHeld != null && a.MapHeld == b.MapHeld;
        }

        public override string CompInspectStringExtra()
        {
            if (lastShards <= 0)
            {
                return "Quiet: the crown is scattered.";
            }
            return $"Waking: {lastShards} of {Props.maxShards} shards near.";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastShards, "lastShards", -1);
        }
    }
}
