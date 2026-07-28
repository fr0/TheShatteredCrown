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
        public int maxShards = 5;

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
        private int lastShards = -1;

        public CompProperties_TSC_Kingsblade Props => (CompProperties_TSC_Kingsblade)props;

        /// <summary>
        /// Called by MapComponent_TSC_CarriedGear on its own interval, NOT by
        /// CompTick. Comps on equipped weapons are never ticked by the game
        /// (BaseWeapon is tickerType Never and the equipment tracker only
        /// ticks verbs), so this ran zero times as a CompTick override and
        /// the blade never woke as shards were gathered.
        /// </summary>
        public void CarriedTick()
        {
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
        /// in their pack is bookkeeping. EVERY shard def counts (each act's
        /// shard is its own item since the def split); Props.shardDef is the
        /// pre-split fallback in case the registry ever comes up empty.
        /// </summary>
        private int CountShards(Pawn wielder)
        {
            List<ThingDef> shardDefs = TSC_Shards.AllDefs;
            if (shardDefs.Count == 0)
            {
                if (Props.shardDef == null)
                {
                    return 0;
                }
                shardDefs = new List<ThingDef> { Props.shardDef };
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
                        if (shardDefs.Contains(thing.def))
                        {
                            count += thing.stackCount;
                        }
                    }
                }
                Thing carried = pawn.carryTracker?.CarriedThing;
                if (carried != null && shardDefs.Contains(carried.def))
                {
                    count += carried.stackCount;
                }
            }
            // Shards stashed on the ground where the wielder stands count too
            // (a party that empties its packs at camp does not un-crown the
            // sword), but only on the wielder's own map.
            Map map = wielder.MapHeld;
            if (map != null)
            {
                foreach (ThingDef shardDef in shardDefs)
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
