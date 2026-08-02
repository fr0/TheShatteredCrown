using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Loot drawn from the medieval catalogue instead of a hand-written list.
    ///
    /// Every chest in the game used to draw from three weapon defs and one
    /// armor def, so a player could not find a helmet, gloves, boots or leg
    /// armor ANYWHERE - including the five pieces this mod ships itself -
    /// and the twenty-three weapons the shops stock never turned up in a
    /// dungeon. Meanwhile TSC_MedievalGear was already scanning the whole
    /// load order by content, which is how the smith ends up selling
    /// Medieval Overhaul and Combat Extended gear without a compatibility
    /// patch. This points the chests at that same catalogue.
    ///
    /// Everything about a draw is per-site: the value band, the mix of arms
    /// to armor, the quality, and how beaten up it is. A bandit camp yields
    /// used kit; a king's grave yields something nobody has swung in nine
    /// hundred years.
    /// </summary>
    public class ThingSetMaker_TSC_Salvage : ThingSetMaker
    {
        /// <summary>How many pieces.</summary>
        public IntRange count = new IntRange(1, 1);

        /// <summary>Market-value band. Deep sites may exceed the shop ceiling; villages may not.</summary>
        public FloatRange value = new FloatRange(20f, 400f);

        /// <summary>Odds a given piece is armor rather than a weapon.</summary>
        public float armorChance = 0.4f;

        /// <summary>Odds it fires at all, for tables where gear is a bonus rather than the point.</summary>
        public float chance = 1f;

        public QualityCategory minQuality = QualityCategory.Poor;

        public QualityCategory maxQuality = QualityCategory.Good;

        /// <summary>Condition as a fraction of max HP: worn kit off a corpse, or grave-fresh.</summary>
        public FloatRange hitPoints = new FloatRange(0.55f, 1f);

        /// <summary>Grave goods are blades and mail; nobody buries a crossbow with a king.</summary>
        public bool allowRanged = true;

        protected override void Generate(ThingSetMakerParams parms, List<Thing> outThings)
        {
            if (chance < 1f && !Rand.Chance(chance))
            {
                return;
            }
            int wanted = count.RandomInRange;
            for (int i = 0; i < wanted; i++)
            {
                bool armor = Rand.Chance(armorChance);
                ThingDef def = Pick(armor) ?? Pick(!armor);
                if (def == null)
                {
                    continue; // a load order with nothing in band; better empty than an error
                }
                ThingDef stuff = def.MadeFromStuff ? GenStuff.RandomStuffByCommonalityFor(def) : null;
                Thing thing = ThingMaker.MakeThing(def, stuff);
                // Rolled inside the band this site is meant to produce,
                // rather than through vanilla's curve and then clamped: a
                // cellar should never be one lucky roll away from a
                // masterwork, and a king's grave should never hand out awful.
                CompQuality quality = thing.TryGetComp<CompQuality>();
                if (quality != null)
                {
                    int low = (int)minQuality;
                    int high = Mathf.Max(low, (int)maxQuality);
                    quality.SetQuality((QualityCategory)Rand.RangeInclusive(low, high),
                        ArtGenerationContext.Outsider);
                }
                if (thing.def.useHitPoints)
                {
                    thing.HitPoints = Mathf.Clamp(
                        Mathf.RoundToInt(thing.MaxHitPoints * hitPoints.RandomInRange), 1, thing.MaxHitPoints);
                }
                outThings.Add(thing);
            }
        }

        private ThingDef Pick(bool armor)
        {
            List<ThingDef> pool = TSC_MedievalGear.Salvage(value.min, value.max, armor, allowRanged);
            return pool.Count == 0 ? null : pool.RandomElement();
        }

        protected override bool CanGenerateSub(ThingSetMakerParams parms) => true;

        protected override IEnumerable<ThingDef> AllGeneratableThingsDebugSub(ThingSetMakerParams parms)
        {
            foreach (ThingDef def in TSC_MedievalGear.Salvage(value.min, value.max, true, allowRanged))
            {
                yield return def;
            }
            foreach (ThingDef def in TSC_MedievalGear.Salvage(value.min, value.max, false, allowRanged))
            {
                yield return def;
            }
        }
    }
}
