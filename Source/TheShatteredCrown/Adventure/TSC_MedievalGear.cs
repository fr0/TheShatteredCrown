using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Every piece of age-appropriate war gear in the load order, whoever
    /// shipped it.
    ///
    /// The smith's stock and the guild shelf were hand-listed vanilla
    /// defNames, which meant a player running Medieval Overhaul had a
    /// catalogue of arms and armour the mod's own shops would never sell -
    /// the shops were the least medieval thing in a medieval game.
    ///
    /// Selection is by CONTENT, exactly like TSC_ApparelCompat: tech level,
    /// tradeability, market value. Naming packageIds would mean shipping a
    /// patch per armour mod and going stale the day the next one releases;
    /// this way Medieval Overhaul, Vanilla Expanded and anything else all
    /// arrive on the shelf for free, and a load order with none of them
    /// still gets the vanilla list it always had.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TSC_MedievalGear
    {
        /// <summary>
        /// Ceiling on what a village smith will have in the back. Medieval
        /// Overhaul's NAMED weapons sit far above this (a named greatsword
        /// out-damages the Kingsblade two to one), and a campaign artifact
        /// that can be bought over a counter is not an artifact.
        /// </summary>
        public const float MaxShopValue = 900f;

        public static readonly List<ThingDef> Weapons = new List<ThingDef>();
        public static readonly List<ThingDef> Armor = new List<ThingDef>();

        static TSC_MedievalGear()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!Sellable(def))
                {
                    continue;
                }
                if (def.IsWeapon && !def.IsApparel)
                {
                    Weapons.Add(def);
                }
                else if (def.IsApparel && Protective(def))
                {
                    Armor.Add(def);
                }
            }
            Log.Message($"[The Shattered Crown] Medieval catalogue: {Weapons.Count} weapons, "
                + $"{Armor.Count} pieces of armour available to shops.");
        }

        private static bool Sellable(ThingDef def)
        {
            if (def == null || def.destroyOnDrop || (int)def.techLevel > (int)TechLevel.Medieval)
            {
                return false;
            }
            if (def.tradeability == Tradeability.None || !def.tradeability.TraderCanSell())
            {
                return false;
            }
            // Quest artifacts and one-of-a-kind pieces are excluded by value,
            // which also keeps modded legendaries off a village shelf.
            float value = def.BaseMarketValue;
            return value > 5f && value <= MaxShopValue;
        }

        /// <summary>
        /// Armour, not clothing: it has to actually stop something.
        ///
        /// Reading ArmorRating off the DEF is not enough, and getting that
        /// wrong found zero armour in a full load order. Most real armour is
        /// stuff-based: its protection comes from the material at spawn time
        /// via StuffEffectMultiplierArmor, so the def's own abstract rating is
        /// nearly zero. A piece qualifies if it either states a rating itself
        /// or is built to gain one from what it is made of.
        /// </summary>
        private static bool Protective(ThingDef def)
        {
            if (def.apparel == null)
            {
                return false;
            }
            if (def.GetStatValueAbstract(StatDefOf.ArmorRating_Sharp) >= 0.15f
                || def.GetStatValueAbstract(StatDefOf.ArmorRating_Blunt) >= 0.15f)
            {
                return true;
            }
            // 0.5 is where vanilla's own numbers separate armour from
            // clothing, with nothing sitting on the line: plate 0.9, advanced
            // helmet 0.7, simple helmet 0.5 - then a gap down to war mask,
            // jacket, duster and cape at 0.3 and robes and tribalwear at 0.2.
            //
            // The first cut of this used 1.0, which NOTHING in vanilla
            // reaches. That went unnoticed because the test load had Combat
            // Extended, and CE writes explicit ArmorRating values onto defs,
            // so the clause above carried it. On a vanilla or Medieval
            // Overhaul load order the shops would have had no armour at all.
            StatDef stuffArmor = DefDatabase<StatDef>.GetNamedSilentFail("StuffEffectMultiplierArmor");
            return stuffArmor != null && def.GetStatValueAbstract(stuffArmor) >= 0.5f;
        }

        /// <summary>
        /// The guild locker's modded section, built once at startup.
        ///
        /// The hand-authored shelf (TSC_GuildStoreDef) is a curated list with
        /// written notes, and it cannot name a def that may not exist. This
        /// fills the gap: anything medieval the load order added, priced in
        /// coin off its market value, and only gear the curated shelf does
        /// not already carry - the quartermaster should not offer two
        /// different longswords at two different prices.
        /// </summary>
        public static List<TSC_GuildStoreEntry> GuildShelf()
        {
            if (guildShelf != null)
            {
                return guildShelf;
            }
            HashSet<ThingDef> curated = new HashSet<ThingDef>();
            foreach (TSC_GuildStoreDef def in DefDatabase<TSC_GuildStoreDef>.AllDefsListForReading)
            {
                if (def.entries == null)
                {
                    continue;
                }
                foreach (TSC_GuildStoreEntry entry in def.entries)
                {
                    if (entry.thing != null)
                    {
                        curated.Add(entry.thing);
                    }
                }
            }
            guildShelf = new List<TSC_GuildStoreEntry>();
            foreach (ThingDef def in Weapons.Concat(Armor))
            {
                // The guild's own kit stays curated; this is for what other
                // mods brought, so vanilla gear does not get listed twice
                // under two different prices.
                if (curated.Contains(def) || def.modContentPack == null
                    || def.modContentPack.IsCoreMod || def.modContentPack.IsOfficialMod
                    || def.defName.StartsWith("TSC_"))
                {
                    continue;
                }
                guildShelf.Add(new TSC_GuildStoreEntry
                {
                    thing = def,
                    cost = CoinPrice(def),
                    note = "Guild stock, bought in from wherever the roads go.",
                });
            }
            return guildShelf;
        }

        private static List<TSC_GuildStoreEntry> guildShelf;

        /// <summary>Coin is worth roughly forty silver on the guild's books.</summary>
        private static int CoinPrice(ThingDef def)
        {
            return Mathf.Max(1, Mathf.RoundToInt(def.BaseMarketValue / 40f));
        }
    }

    /// <summary>
    /// Stock generator for the town smith and any other shop that should
    /// carry whatever the load order calls medieval. Pairs with the hand-
    /// listed vanilla generators rather than replacing them, so the staple
    /// stock is guaranteed and the modded gear is the variety on top.
    /// </summary>
    public class StockGenerator_TSC_MedievalGear : StockGenerator
    {
        public bool weapons = true;
        public bool armor = true;
        /// <summary>Only stock gear at or above this value: shops sell kit, not tat.</summary>
        public float minValue = 40f;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            List<ThingDef> pool = new List<ThingDef>();
            if (weapons)
            {
                pool.AddRange(TSC_MedievalGear.Weapons);
            }
            if (armor)
            {
                pool.AddRange(TSC_MedievalGear.Armor);
            }
            pool.RemoveAll(d => d.BaseMarketValue < minValue);
            if (pool.Count == 0)
            {
                yield break;
            }
            int count = countRange.RandomInRange;
            for (int i = 0; i < count; i++)
            {
                ThingDef def = pool.RandomElement();
                ThingDef stuff = def.MadeFromStuff ? GenStuff.RandomStuffByCommonalityFor(def) : null;
                Thing thing = ThingMaker.MakeThing(def, stuff);
                if (thing.def.HasComp(typeof(CompQuality)))
                {
                    thing.TryGetComp<CompQuality>()?.SetQuality(
                        QualityUtility.GenerateQualityTraderItem(), ArtGenerationContext.Outsider);
                }
                yield return thing;
            }
        }

        public override bool HandlesThingDef(ThingDef thingDef)
        {
            return TSC_MedievalGear.Weapons.Contains(thingDef)
                || TSC_MedievalGear.Armor.Contains(thingDef);
        }
    }
}
