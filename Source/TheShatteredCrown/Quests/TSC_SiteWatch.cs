using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// "All enemies defeated" should mean there were enemies.
    ///
    /// Vanilla's Site fires that signal the moment nothing hostile is
    /// standing, and asks no questions about whether anything hostile ever
    /// was. Every clear-the-site contract ends on it, so any failure that
    /// leaves a contract map empty - a spawn signal wired to the wrong
    /// moment, a genstep that threw halfway, a faction that would not
    /// resolve - does not read as a bug. It reads as a contract completing
    /// itself the instant the party walks in, and paying full.
    ///
    /// That has now happened twice with two different causes (the pack, then
    /// the warren), so the guard belongs here rather than in each quest
    /// script: hold the signal until this map has actually held an enemy.
    ///
    /// The hold is not forever. If a minute passes with the map still empty,
    /// the signal goes through - a player must never be stranded on a
    /// contract that cannot end - but it goes through with an error in the
    /// log naming the site, which is the thing that was missing both times.
    /// </summary>
    public class MapComponent_TSC_SiteWatch : MapComponent
    {
        /// <summary>How long an empty contract map is given to prove itself before we give up on it.</summary>
        private const int GraceTicks = 3600;

        private bool everSawHostile;
        private bool complained;
        private int startTick = -1;

        public MapComponent_TSC_SiteWatch(Map map) : base(map)
        {
        }

        public bool EnemiesEverSeen => everSawHostile;

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (startTick < 0)
            {
                startTick = Find.TickManager.TicksGame;
            }
            if (!(map.Parent is Site site))
            {
                return;
            }
            Recheck();
            if (!everSawHostile)
            {
                // Said at generation, when the cause is still nearby in the
                // log: a genstep that threw is printed a few lines above.
                Log.Warning($"[The Shattered Crown] {Describe(site)} generated with no hostile pawns on it. "
                    + "Any contract that ends on 'all enemies defeated' will hold that signal for a minute "
                    + "and then complete anyway.");
            }
        }

        public override void MapComponentTick()
        {
            if (everSawHostile || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            Recheck();
        }

        private void Recheck()
        {
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!pawn.Dead && pawn.HostileTo(Faction.OfPlayer))
                {
                    everSawHostile = true;
                    return;
                }
            }
        }

        /// <summary>
        /// Whether the "all enemies defeated" check may run. False holds it;
        /// true lets vanilla decide as usual.
        /// </summary>
        public bool AllowCompletion()
        {
            if (everSawHostile)
            {
                return true;
            }
            if (startTick >= 0 && Find.TickManager.TicksGame - startTick < GraceTicks)
            {
                return false;
            }
            if (!complained)
            {
                complained = true;
                Log.Error($"[The Shattered Crown] {Describe(map.Parent as Site)} still has no hostiles after a "
                    + "minute. Completing the contract rather than stranding it, but the site's occupants "
                    + "never spawned and that is the bug to chase.");
            }
            return true;
        }

        private static string Describe(Site site)
        {
            if (site == null)
            {
                return "A site";
            }
            StringBuilder parts = new StringBuilder();
            for (int i = 0; i < site.parts.Count; i++)
            {
                parts.Append(i > 0 ? ", " : "").Append(site.parts[i].def?.defName);
            }
            return $"Site [{parts}]";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref everSawHostile, "tscSiteSawHostile");
            Scribe_Values.Look(ref complained, "tscSiteComplained");
            Scribe_Values.Look(ref startTick, "tscSiteStartTick", -1);
        }
    }

    [HarmonyPatch(typeof(Site), "CheckAllEnemiesDefeated")]
    public static class Patch_Site_CheckAllEnemiesDefeated
    {
        public static bool Prefix(Site __instance)
        {
            Map map = __instance?.Map;
            MapComponent_TSC_SiteWatch watch = map?.GetComponent<MapComponent_TSC_SiteWatch>();
            return watch == null || watch.AllowCompletion();
        }
    }
}
