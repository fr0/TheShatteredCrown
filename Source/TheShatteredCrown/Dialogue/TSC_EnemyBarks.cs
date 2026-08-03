using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>A pool of enemy combat lines: taunts while fighting, jabs when hit, panic when fleeing.</summary>
    public class TSC_BarkSetDef : Def
    {
        public List<string> taunts;
        public List<string> hits;
        public List<string> flees;
    }

    /// <summary>
    /// Enemy combat barks, the counterpart of the companions': hostile
    /// humanlike pawns taunt on a jittered timer while a fight is engaged,
    /// yelp when they take a real hit, and panic aloud while fleeing. Same
    /// floating-text pattern as everything else, in hostile red so the two
    /// sides of the shouting match never blur.
    /// </summary>
    public class MapComponent_TSC_EnemyBarks : MapComponent
    {
        private static readonly Color EnemyColor = new Color(0.95f, 0.6f, 0.55f);
        // Halved frequency (user tuning): a crew of eight was keeping up a
        // constant jeer. Longer gaps between taunts, and hit yelps land
        // about half as often, so the shouting punctuates the fight rather
        // than scoring it.
        private static readonly IntRange FirstTaunt = new IntRange(480, 1800);
        private static readonly IntRange BetweenTaunts = new IntRange(3000, 7200);
        private const int HitBarkCooldownTicks = 1200;
        private const float HitBarkChance = 0.18f;
        /// <summary>How soon an unseen barker looks again for an audience.</summary>
        private static readonly IntRange UnseenRetry = new IntRange(300, 900);

        private readonly Dictionary<Pawn, int> nextTaunt = new Dictionary<Pawn, int>();
        private readonly Dictionary<Pawn, int> lastHitBark = new Dictionary<Pawn, int>();
        private bool fighting;

        public MapComponent_TSC_EnemyBarks(Map map) : base(map)
        {
        }

        private static TSC_BarkSetDef Set => DefDatabase<TSC_BarkSetDef>.GetNamedSilentFail("TSC_Barks_Bandits");

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 60 != 0)
            {
                return;
            }
            TSC_BarkSetDef set = Set;
            if (set == null)
            {
                return;
            }
            if (!TSC_EncounterController.AnyEngagedHostileOn(map))
            {
                if (fighting)
                {
                    fighting = false;
                    nextTaunt.Clear();
                }
                return;
            }
            fighting = true;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!IsBarker(pawn))
                {
                    continue;
                }
                if (!nextTaunt.TryGetValue(pawn, out int due))
                {
                    nextTaunt[pawn] = now + FirstTaunt.RandomInRange;
                    continue;
                }
                if (now < due)
                {
                    continue;
                }
                if (!Seen(pawn))
                {
                    // Nobody is impressed by a threat they cannot hear. Hold
                    // the line rather than spend it, and look again shortly -
                    // pushing the full gap would mean an enemy that spent the
                    // approach out of sight stays silent long after contact.
                    nextTaunt[pawn] = now + UnseenRetry.RandomInRange;
                    continue;
                }
                nextTaunt[pawn] = now + BetweenTaunts.RandomInRange;
                List<string> pool = Fleeing(pawn) ? set.flees : set.taunts;
                if (!pool.NullOrEmpty())
                {
                    MoteMaker.ThrowText(pawn.DrawPos, map, pool.RandomElement(), EnemyColor);
                }
            }
        }

        /// <summary>Called by the damage postfix: a solid hit gets a yelp, throttled per pawn.</summary>
        public void Notify_EnemyHit(Pawn pawn)
        {
            TSC_BarkSetDef set = Set;
            if (set == null || set.hits.NullOrEmpty() || !IsBarker(pawn))
            {
                return;
            }
            if (!Seen(pawn))
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (lastHitBark.TryGetValue(pawn, out int last) && now - last < HitBarkCooldownTicks)
            {
                return;
            }
            if (!Rand.Chance(HitBarkChance))
            {
                return;
            }
            lastHitBark[pawn] = now;
            MoteMaker.ThrowText(pawn.DrawPos, map, set.hits.RandomElement(), EnemyColor);
        }

        /// <summary>
        /// Somebody has to be there to hear it. Barks from an enemy the party
        /// cannot see are floating text over fog: they give away positions and
        /// they read as the map talking to itself.
        /// </summary>
        private bool Seen(Pawn pawn)
        {
            if (pawn.Position.Fogged(map))
            {
                return false;
            }
            foreach (Pawn watcher in map.mapPawns.FreeColonistsSpawned)
            {
                if (watcher.Dead || watcher.Downed)
                {
                    continue;
                }
                if (GenSight.LineOfSight(watcher.Position, pawn.Position, map, skipFirstCell: true))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsBarker(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Downed && pawn.Spawned
                && pawn.RaceProps.Humanlike && pawn.Faction != Faction.OfPlayer
                && pawn.HostileTo(Faction.OfPlayer)
                && CanSpeak(pawn);
        }

        /// <summary>
        /// The dead do not banter.
        ///
        /// Barrow shamblers and the first king's oath-keepers are humanlike
        /// and hostile, which was the whole test - so a crypt full of risen
        /// dead was jeering like a bandit camp. Anything mutant (vanilla's
        /// shambler and its kin) or wired shut by a hediff that removes
        /// speech now fights in silence, which is considerably worse to be
        /// on the wrong end of.
        /// </summary>
        public static bool CanSpeak(Pawn pawn)
        {
            if (pawn.IsMutant)
            {
                return false;
            }
            // The bark pool is a BANDIT CREW's ("Get 'em, lads!"), and a
            // crew is a faction. The first king's oath-keepers are dead men
            // posted alone with no faction at all: they have their own
            // scripted words and would never borrow a brigand's, so a
            // factionless hostile fights in silence too.
            if (pawn.Faction == null)
            {
                return false;
            }
            // No tongue, no taunt: covers the undead marked by hediff rather
            // than by mutant def, and anything else that lost the capacity.
            return pawn.health?.capacities == null
                || pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking);
        }

        private static bool Fleeing(Pawn pawn)
        {
            if (pawn.MentalStateDef != null && pawn.MentalStateDef == MentalStateDefOf.PanicFlee)
            {
                return true;
            }
            return pawn.GetLord()?.LordJob is LordJob_ExitMapBest;
        }
    }

    /// <summary>The yelp: real damage to a hostile humanlike sometimes gets words.</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_TakeDamage_EnemyBark
    {
        public static void Postfix(Thing __instance, DamageInfo dinfo)
        {
            if (!(__instance is Pawn pawn) || pawn.Map == null
                || dinfo.Def == null || !dinfo.Def.harmsHealth || dinfo.Amount <= 0f)
            {
                return;
            }
            pawn.Map.GetComponent<MapComponent_TSC_EnemyBarks>()?.Notify_EnemyHit(pawn);
        }
    }
}
