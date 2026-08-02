using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// What a settlement thinks of the company, and what that costs.
    ///
    /// Affinity was very nearly write-only outside the companions: eighty
    /// grants across the dialogue fed eight option gates and a word on a
    /// portrait, and every kind word said to a villager - Old Wick has
    /// seven, Haldor five, and one scene in village_bryn fans out to nine
    /// people at once - changed nothing whatsoever. This reads all of it.
    ///
    /// Standing is the AVERAGE affinity of the named locals standing on the
    /// map, not the sum: a big village is not automatically friendlier than
    /// a hamlet, and helping four people in a village of twelve does not
    /// buy the whole place. It moves prices at the smith and at every
    /// service the settlement sells.
    /// </summary>
    public static class TSC_VillageStanding
    {
        /// <summary>Beyond this, more goodwill stops paying. A village is not a bank.</summary>
        public const float MaxDiscount = 0.25f;

        /// <summary>And a village that hates you overcharges, but will still deal.</summary>
        public const float MaxSurcharge = 0.25f;

        /// <summary>Affinity at which the discount is fully earned (devoted).</summary>
        private const float FullAt = 25f;

        /// <summary>
        /// The average affinity of every named local spawned here. Returns 0
        /// for maps with nobody named on them, which is most of them.
        /// </summary>
        public static float StandingOn(Map map)
        {
            if (map == null || Verse.Current.Game == null)
            {
                return 0f;
            }
            DialogueStateManager state = DialogueStateManager.Current;
            if (state == null)
            {
                return 0f;
            }
            int total = 0;
            int counted = 0;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || pawn.Faction == Faction.OfPlayer)
                {
                    continue;
                }
                NamedNpcDef def = state.NpcDefFor(pawn);
                if (def == null)
                {
                    continue;
                }
                total += state.AffinityOf(def);
                counted++;
            }
            return counted == 0 ? 0f : (float)total / counted;
        }

        /// <summary>
        /// Price multiplier for this map: 0.75 where they love the company,
        /// 1.25 where they do not, 1 where they have no opinion. Linear
        /// between, so the first kind word is worth something and the
        /// twentieth is worth less.
        /// </summary>
        public static float PriceFactorOn(Map map)
        {
            float standing = StandingOn(map);
            if (Mathf.Approximately(standing, 0f))
            {
                return 1f;
            }
            float t = Mathf.Clamp(standing / FullAt, -1f, 1f);
            return t >= 0f ? 1f - t * MaxDiscount : 1f + -t * MaxSurcharge;
        }

        /// <summary>Applies the settlement's opinion to a price in silver, never below 1.</summary>
        public static int Apply(int price, Map map)
        {
            if (price <= 0)
            {
                return price;
            }
            return Mathf.Max(1, Mathf.RoundToInt(price * PriceFactorOn(map)));
        }

        /// <summary>
        /// One line for the player, so the number is never mysterious. Null
        /// when the settlement has no opinion worth mentioning.
        /// </summary>
        public static string Line(Map map)
        {
            float standing = StandingOn(map);
            int percent = Mathf.RoundToInt((1f - PriceFactorOn(map)) * 100f);
            if (percent == 0)
            {
                return null;
            }
            return percent > 0
                ? $"They know the company here. Prices are {percent}% kinder than they would be for strangers."
                : $"The company is not popular here. Prices are {-percent}% worse than they would be for strangers.";
        }

        /// <summary>Every named local on the map, warmest first: the tooltip behind the number.</summary>
        public static List<string> Ledger(Map map)
        {
            List<string> lines = new List<string>();
            if (map == null)
            {
                return lines;
            }
            DialogueStateManager state = DialogueStateManager.Current;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                NamedNpcDef def = pawn == null || pawn.Dead ? null : state.NpcDefFor(pawn);
                if (def != null)
                {
                    lines.Add($"{pawn.LabelShortCap}: {DialogueStateManager.AffinityTier(state.AffinityOf(def))}");
                }
            }
            lines.Sort();
            return lines;
        }
    }
}
