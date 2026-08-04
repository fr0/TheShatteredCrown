using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Keeping the century's own troubles.
    ///
    /// The storyteller casts its incidents from every faction the load
    /// order provides, which in a big list means a medieval company gets
    /// shaken down by pollution psykers and raided by mech cults. It also
    /// misfires: a faction whose cheapest pawn group costs more than the
    /// rolled points generates nobody, and vanilla logs "could not generate
    /// any enemies even though min points have been checked" and drops the
    /// incident (seen in play, PirateWaster at 68 points).
    ///
    /// So: in RPG mode, an incident aimed at the player by an out-of-period
    /// faction is vetoed before it runs. The storyteller simply tries again
    /// later, and what turns up is bandits, brigands, and the Iron Brand.
    ///
    /// Deliberately narrow. Only incidents that NAME a faction are judged -
    /// weather, wildlife, disease and every faction-less event run exactly
    /// as vanilla intends - and the test is the faction's own tech level, so
    /// a medieval faction from any mod passes without being listed.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class Patch_TSC_IncidentPeriod
    {
        public static bool Prefix(IncidentParms parms, ref bool __result)
        {
            if (!TSC_RpgMode.Active || parms?.faction == null || InPeriod(parms.faction))
            {
                return true;
            }
            // RECAST before refusing. Vetoing alone would make the roads
            // safer the more sci-fi factions a load order had, which is the
            // opposite of the point: the ambush should still happen, with
            // brigands in it. Only when this world has nobody period-
            // appropriate to send is the incident dropped.
            Faction stand = PeriodStandIn();
            if (stand != null)
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[The Shattered Crown] Recast {parms.faction.Name} "
                        + $"({parms.faction.def.techLevel}) as {stand.Name}: out of period.");
                }
                parms.faction = stand;
                return true;
            }
            __result = false;
            if (Prefs.DevMode)
            {
                Log.Message($"[The Shattered Crown] Held back {parms.faction.Name} "
                    + $"({parms.faction.def.techLevel}): out of period, and no period faction to stand in.");
            }
            return false;
        }

        /// <summary>
        /// Somebody this century could send: the mod's own brigands first
        /// (they exist in every TSC world and are always hostile), then any
        /// hostile faction at or below medieval tech.
        /// </summary>
        private static Faction PeriodStandIn()
        {
            Faction bandits = TSC_BanditFactionUtility.Get();
            if (bandits != null && !bandits.defeated && bandits.HostileTo(Faction.OfPlayer))
            {
                return bandits;
            }
            List<Faction> candidates = new List<Faction>();
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (!faction.IsPlayer && !faction.defeated && faction.HostileTo(Faction.OfPlayer)
                    && faction.def != null && !faction.def.pawnGroupMakers.NullOrEmpty()
                    && faction.def.techLevel != TechLevel.Undefined
                    && faction.def.techLevel <= TechLevel.Medieval)
                {
                    candidates.Add(faction);
                }
            }
            return candidates.Count > 0 ? candidates.RandomElement() : null;
        }

        /// <summary>
        /// A faction this century could actually meet. Player and permanent
        /// -ally factions are never judged: the company's own guild, and
        /// anyone the story has made a friend, belong here whatever their
        /// def says.
        /// </summary>
        private static bool InPeriod(Faction faction)
        {
            if (faction.IsPlayer || faction.def == null)
            {
                return true;
            }
            if (!faction.HostileTo(Faction.OfPlayer))
            {
                return true; // traders and visitors are somebody else's problem
            }
            TechLevel tech = faction.def.techLevel;
            // Undefined means the faction never said, and a faction that
            // never said is not evidence of gunpowder: let it through rather
            // than silently muting a whole mod's content.
            return tech == TechLevel.Undefined || tech <= TechLevel.Medieval;
        }
    }
}
