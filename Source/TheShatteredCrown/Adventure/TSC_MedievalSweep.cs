using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Taking the future back out of the ruins.
    ///
    /// This mod's own shops and chests have been filtered for a while
    /// (TSC_MedievalGear), and they hold the line. The world does not: half
    /// this mod's sites are built on vanilla's ancient-ruins gensteps, and
    /// those scatter vanilla's ancient loot, which is where a charge rifle
    /// and a suit of recon armor walked into a medieval campaign and turned
    /// up on the caravan re-form screen.
    ///
    /// Rather than fork every layout, the map gets swept once when it is
    /// generated: weapons and armor from a later age are replaced with
    /// salvage of comparable worth from the same catalogue the shops use, so
    /// the room that was meant to hold something good still does. Only
    /// unowned things, only weapons and apparel, only in RPG mode, and only
    /// at generation - a colony game is untouched, and so is anything the
    /// player has since put down.
    ///
    /// Everything else vanilla buried stays buried. Components, glitterworld
    /// medicine and the rest are strange finds rather than wrong ones, and a
    /// party that hauls an unusable marvel back to a trader is having the
    /// right kind of adventure.
    /// </summary>
    public class MapComponent_TSC_MedievalSweep : MapComponent
    {
        private bool swept;

        /// <summary>A little over half an hour of game time between passes.</summary>
        private const int Interval = 2000;

        public MapComponent_TSC_MedievalSweep(Map map) : base(map)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (swept)
            {
                return; // already done for this map, in some earlier session
            }
            swept = true;
            if (!TSC_RpgMode.Active)
            {
                return;
            }
            int replaced = Sweep();
            if (replaced > 0)
            {
                Log.Message($"[The Shattered Crown] Swept {replaced} out-of-period piece(s) of gear from "
                    + $"{map.Parent?.Label ?? "a map"} and left medieval salvage in their place.");
            }
        }

        /// <summary>
        /// The generation sweep only ran once, and the world kept making
        /// more: a CE-armed raider dies an hour after the map is built and
        /// his minigun hits the floor unswept, which is how modern guns
        /// kept turning up on the caravan reform screen after the map-gen
        /// pass was already in. Loose weapons and apparel get re-checked
        /// on a slow clock - group listers only, so the pass is cheap.
        /// </summary>
        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            int replaced = 0;
            foreach (ThingRequestGroup group in new[] { ThingRequestGroup.Weapon, ThingRequestGroup.Apparel })
            {
                foreach (Thing thing in new List<Thing>(map.listerThings.ThingsInGroup(group)))
                {
                    // Loose and unowned: a dead raider's drop qualifies (a
                    // corpse's gear falls forbidden and factionless); a
                    // living pawn's kit does not.
                    if (thing == null || thing.Destroyed || !thing.Spawned || thing.Faction != null)
                    {
                        continue;
                    }
                    if (Offending(thing))
                    {
                        replaced += Replace(thing, thing.Position, null) ? 1 : 0;
                    }
                }
            }
            if (replaced > 0)
            {
                Log.Message($"[The Shattered Crown] Swept {replaced} dropped out-of-period piece(s) on "
                    + $"{map.Parent?.Label ?? "a map"}; medieval salvage left in their place.");
            }
        }

        private int Sweep()
        {
            int replaced = 0;
            foreach (Thing thing in new List<Thing>(map.listerThings.AllThings))
            {
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                // Anything with an owner is somebody's property, not scenery:
                // a faction's gear, a pawn's kit, the party's own baggage.
                if (thing.Faction != null)
                {
                    continue;
                }
                if (Offending(thing))
                {
                    replaced += Replace(thing, thing.Position, null) ? 1 : 0;
                    continue;
                }
                // Vanilla's ancient loot mostly sits INSIDE something: a
                // locker bank, a crate, a sealed cache.
                if (thing is IThingHolder holder)
                {
                    replaced += SweepContainer(holder, thing.Position);
                }
            }
            return replaced;
        }

        private int SweepContainer(IThingHolder holder, IntVec3 at)
        {
            ThingOwner contents = holder.GetDirectlyHeldThings();
            if (contents == null)
            {
                return 0;
            }
            int replaced = 0;
            for (int i = contents.Count - 1; i >= 0; i--)
            {
                Thing inside = contents[i];
                if (Offending(inside) && Replace(inside, at, contents))
                {
                    replaced++;
                }
            }
            return replaced;
        }

        private static bool Offending(Thing thing)
        {
            ThingDef def = thing?.def;
            if (def == null)
            {
                return false;
            }
            // TSC_Gear.IsGear, not def.IsWeapon: IsWeapon is true of anything
            // carrying tools, and vanilla WOOD carries them (a plank is a
            // club in a pinch). Wood also declares no tech level, so it
            // failed the period test and this sweep cheerfully "replaced"
            // the party's lumber with same-value items out of the weapon
            // catalogue - which under Medieval Overhaul meant bottles of
            // drink, because those carry tools too. Resources are not gear.
            if (!TSC_Gear.IsGear(thing))
            {
                return false;
            }
            // The same test the shops and the chests use, so what survives a
            // sweep is exactly what a smith would have been willing to sell.
            return !TSC_MedievalGear.AgeAppropriate(def);
        }

        /// <summary>
        /// Swap it for something of this age and roughly this worth. A room
        /// that was written to hold a prize still holds one; it is simply a
        /// prize somebody in this century could use.
        /// </summary>
        private bool Replace(Thing thing, IntVec3 at, ThingOwner container)
        {
            float value = Mathf.Max(20f, thing.MarketValue);
            List<ThingDef> pool = TSC_MedievalGear.Salvage(value * 0.55f, value * 1.6f,
                thing.def.IsApparel, allowRanged: true);
            if (pool.Count == 0)
            {
                // Nothing in band in this load order: the thing still cannot
                // stay, but silver keeps the find worth finding.
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = Mathf.Clamp(Mathf.RoundToInt(value * 0.5f), 10, 500);
                return Put(thing, silver, at, container);
            }
            ThingDef def = pool.RandomElement();
            ThingDef stuff = def.MadeFromStuff ? GenStuff.RandomStuffByCommonalityFor(def) : null;
            Thing swapped = ThingMaker.MakeThing(def, stuff);
            CompQuality quality = swapped.TryGetComp<CompQuality>();
            if (quality != null)
            {
                quality.SetQuality(thing.TryGetQuality(out QualityCategory had) ? had : QualityCategory.Normal,
                    ArtGenerationContext.Outsider);
            }
            if (swapped.def.useHitPoints && thing.def.useHitPoints && thing.MaxHitPoints > 0)
            {
                swapped.HitPoints = Mathf.Clamp(
                    Mathf.RoundToInt(swapped.MaxHitPoints * ((float)thing.HitPoints / thing.MaxHitPoints)),
                    1, swapped.MaxHitPoints);
            }
            return Put(thing, swapped, at, container);
        }

        private bool Put(Thing original, Thing replacement, IntVec3 at, ThingOwner container)
        {
            if (container != null)
            {
                original.Destroy();
                return container.TryAdd(replacement);
            }
            original.Destroy();
            return GenPlace.TryPlaceThing(replacement, at, map, ThingPlaceMode.Near);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref swept, "tscMedievalSwept");
        }
    }
}
