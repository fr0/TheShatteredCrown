using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// XP for killing things, scaled to what was killed.
    ///
    /// Quests were the only XP source, which made a long fight on the way to
    /// an objective worth nothing at all. This adds a small, steady trickle
    /// so clearing a camp advances the party in its own right - roughly a
    /// third to a half of a contract's payout for a full camp, never enough
    /// to replace taking contracts.
    ///
    /// Party-wide, like every other XP award in this mod: the cleric who
    /// spent the fight healing earns the same as the one who landed the
    /// blow. That is what keeps a party levelling together.
    /// </summary>
    public static class TSC_KillXp
    {
        /// <summary>XP per point of the victim's combat power.</summary>
        private const float XpPerCombatPower = 0.2f;
        private const int MinXp = 2;
        private const int MaxXp = 50;

        /// <summary>Quiet time after the last kill before the summary posts.</summary>
        private const int SummaryQuietTicks = 240;

        private static int pendingXp;
        private static int pendingKills;
        private static int lastKillTick;

        /// <summary>
        /// What a prefix captured about a kill BEFORE it happened. Hostility
        /// has to be read while the victim is alive: a manhunter animal is
        /// hostile through its mental state, and that state is gone by the
        /// time a postfix runs - which would have silently excluded every
        /// hunt-contract beast.
        /// </summary>
        public struct Pending
        {
            public bool eligible;
            public int xp;
            /// <summary>Whoever struck the blow: the per-kill mood memory is theirs.</summary>
            public Pawn killer;
        }

        public static Pending Evaluate(Pawn victim, DamageInfo? dinfo)
        {
            Pending pending = default;
            if (!TSC_RpgMode.Active || victim == null || victim.Dead)
            {
                return pending;
            }
            // Only things that were fighting us, and only when one of ours
            // did it. Rules out hunted game, slaughtered livestock, prisoners,
            // executions, and anything a trap or a third faction killed.
            if (!victim.HostileTo(Faction.OfPlayer))
            {
                return pending;
            }
            if (dinfo?.Instigator is Pawn killer)
            {
                if (killer.Faction != Faction.OfPlayer || killer == victim)
                {
                    return pending;
                }
                pending.killer = killer;
            }
            else if (!DiedAmongUs(victim))
            {
                return pending;
            }
            pending.eligible = true;
            pending.xp = XpFor(victim);
            return pending;
        }

        /// <summary>
        /// No pawn on the killing blow, but it happened in our fight.
        ///
        /// Most enemies are downed and then bleed out, and that final Kill
        /// arrives from a hediff with no instigator - so requiring a pawn
        /// killer would quietly pay nothing for a large share of real kills.
        /// A hostile expiring on a map the party is standing on is our doing
        /// closely enough. The occasional body a third faction dropped in the
        /// same battle is a fair price for not shorting the player.
        /// </summary>
        private static bool DiedAmongUs(Pawn victim)
        {
            Map map = victim.MapHeld;
            return map != null && map.mapPawns.FreeColonistsSpawnedCount > 0;
        }

        public static int XpFor(Pawn victim)
        {
            float power = victim?.kindDef?.combatPower ?? 0f;
            return Mathf.Clamp(Mathf.RoundToInt(power * XpPerCombatPower), MinXp, MaxXp);
        }

        public static void Award(Pending pending)
        {
            if (!pending.eligible || pending.xp <= 0 || TSC_ProgressionManager.Current == null)
            {
                return;
            }
            // Granted now, announced later: one message per kill would bury
            // the combat log in a firefight.
            TSC_ProgressionManager.Current.GrantXpToParty(pending.xp, "kills", announce: false);
            // Optional, off by default, and stacking: one memory per kill.
            TSC_MoodOptions.NoteKill(pending.killer);
            pendingXp += pending.xp;
            pendingKills++;
            lastKillTick = Find.TickManager.TicksGame;
        }

        /// <summary>Ticked by TSC_ProgressionManager: posts one summary once the shooting stops.</summary>
        public static void Tick()
        {
            if (pendingKills <= 0 || Find.TickManager.TicksGame - lastKillTick < SummaryQuietTicks)
            {
                return;
            }
            Messages.Message(
                $"The party gains {pendingXp} XP ({pendingKills} {(pendingKills == 1 ? "kill" : "kills")}).",
                MessageTypeDefOf.PositiveEvent, historical: false);
            pendingXp = 0;
            pendingKills = 0;
        }

        /// <summary>Dropped on load: a half-counted fight from another save must not carry over.</summary>
        public static void Reset()
        {
            pendingXp = 0;
            pendingKills = 0;
            lastKillTick = 0;
        }
    }

    /// <summary>
    /// Prefix captures, postfix pays. Split because the victim must still be
    /// alive to read hostility from, and because a kill can be CANCELLED
    /// after the prefixes run - plot armor does exactly that - so the payout
    /// waits until the pawn is actually dead.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_XP
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, out TSC_KillXp.Pending __state)
        {
            __state = TSC_KillXp.Evaluate(__instance, dinfo);
        }

        public static void Postfix(Pawn __instance, TSC_KillXp.Pending __state)
        {
            if (__instance != null && __instance.Dead)
            {
                TSC_KillXp.Award(__state);
            }
        }
    }
}
