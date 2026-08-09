using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// Reform-caravan sanity: vanilla blocks reforming while ANY hostile is
    /// an active threat, counting dormant pawns and (on most world objects)
    /// enemies sitting in fog the player has never revealed. A sleeping
    /// guard in an unexplored corner holding the whole caravan hostage is a
    /// scavenger-hunt, not a threat. With the setting on (default), only
    /// hostiles the player can actually SEE block reforming.
    /// </summary>
    [HarmonyPatch(typeof(FormCaravanComp), "AnyActiveThreatNow", MethodType.Getter)]
    public static class Patch_Reform_IgnoreHiddenEnemies
    {
        public static void Postfix(FormCaravanComp __instance, ref bool __result)
        {
            if (!__result || !(TSC_Mod.Settings?.reformIgnoresHiddenEnemies ?? false))
            {
                return;
            }
            Map map = (__instance.parent as MapParent)?.Map;
            if (map == null)
            {
                return;
            }
            foreach (IAttackTarget target in map.attackTargetsCache.TargetsHostileToColony)
            {
                Thing thing = target.Thing;
                if (thing == null || thing.Destroyed || !thing.Spawned || thing.Position.Fogged(map))
                {
                    continue; // hidden: does not block
                }
                bool dormant = thing.TryGetComp<CompCanBeDormant>()?.Awake == false;
                if (dormant || GenHostility.IsActiveThreatTo(target, Faction.OfPlayer))
                {
                    return; // a VISIBLE threat still blocks - vanilla verdict stands
                }
            }
            __result = false;
        }
    }

    [StaticConstructorOnStartup]
    public static class TSC_HarmonyInit
    {
        static TSC_HarmonyInit()
        {
            Harmony harmony = new Harmony("fr0.theshatteredcrown");
            harmony.PatchAll();
            // Compat probes and the engine's own patches moved with the turn
            // engine (0TSC.TurnBased.dll), which bootstraps itself.
            // Push the configured quest-completion mood into its thought def
            // (defs load before settings are read).
            TSC_QuestMood.ApplySetting();
        }
    }

    /// <summary>
    /// Plot armor: story-critical named characters (NamedNpcDef.plotArmor) are
    /// downed by lethal events instead of dying. Scenario-gated like the rest of
    /// the RPG layer; the quest death fail-safes remain as a second net for
    /// anything that slips past (dev tools, hard destruction).
    /// </summary>
    public static class TSC_PlotArmor
    {
        // Keyed by thingIDNumber, not Pawn: a static dictionary holding Pawn
        // references keeps every pawn it ever noted alive for the whole
        // session - a slow leak of dead story characters. The int key
        // dedupes the message just as well and holds nothing.
        private static readonly Dictionary<int, int> lastNotifyTick = new Dictionary<int, int>();

        /// <summary>
        /// The one pawn the story is currently killing on purpose.
        ///
        /// Plot armor cannot tell an authored death from a stray arrow, so a
        /// scene that ENDS a protected character (Aled paying eight centuries
        /// of arrears the moment he hands the shard over) was simply downing
        /// them instead, alive, which reads as a bug and blocks the scene.
        /// Narrow and scoped: set for the duration of one Kill call, cleared
        /// in a finally, never a standing exemption.
        /// </summary>
        private static Pawn scriptedDeath;

        /// <summary>Kills a protected character because the story says so.</summary>
        public static void ScriptedKill(Pawn pawn, DamageInfo? dinfo = null)
        {
            if (pawn == null || pawn.Dead)
            {
                return;
            }
            Pawn previous = scriptedDeath;
            scriptedDeath = pawn;
            try
            {
                pawn.Kill(dinfo);
            }
            finally
            {
                scriptedDeath = previous;
            }
        }

        public static bool Protects(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn == scriptedDeath)
            {
                return false;
            }
            if (Verse.Current.Game == null || Find.World == null || !TSC_RpgMode.Active)
            {
                return false;
            }
            // Joining the party is signing up for the same risks as everyone
            // else: plot armor protects story characters only until recruited.
            if (pawn.Faction == Faction.OfPlayer)
            {
                return false;
            }
            // The quarry is protected only until the player reaches its map:
            // no predator or site hostile may end the hunt before the player
            // even gets a say. Once colonists are present (or it's the
            // player's own creature), it can die - that failure is authored.
            if (pawn.kindDef == TSC_DefOf.TSC_Ettersnap)
            {
                Map map = pawn.MapHeld;
                return map == null || map.mapPawns.FreeColonistsSpawnedCount == 0;
            }
            NamedNpcDef def = DialogueStateManager.Current.NpcDefFor(pawn);
            return def != null && def.plotArmor;
        }

        public static void SurviveInstead(Pawn pawn)
        {
            if (!pawn.Downed)
            {
                HealthUtility.DamageUntilDowned(pawn, allowBleedingWounds: false);
            }
            // No message when nobody is there to see it (e.g. the ettersnap
            // shrugging off a predator on a map the player hasn't reached).
            if (pawn.MapHeld == null || pawn.MapHeld.mapPawns.FreeColonistsSpawnedCount == 0)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (!lastNotifyTick.TryGetValue(pawn.thingIDNumber, out int last) || now - last > 2500)
            {
                lastNotifyTick[pawn.thingIDNumber] = now;
                Messages.Message(
                    $"{pawn.LabelShortCap} is struck down, but clings to life. This story is not done with them.",
                    pawn, MessageTypeDefOf.NegativeEvent, historical: false);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_PlotArmor
    {
        public static bool Prefix(Pawn __instance)
        {
            if (!TSC_PlotArmor.Protects(__instance))
            {
                return true;
            }
            TSC_PlotArmor.SurviveInstead(__instance);
            return false;
        }
    }

    /// <summary>
    /// Energy strip on colonist-bar portraits, drawn inside the drawer itself so
    /// it works on maps AND in world view (caravan groups), with correct z-order.
    /// </summary>
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
    public static class Patch_DrawColonist_EnergyStrip
    {
        private static readonly Color BackColor = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color EnergyColor = new Color(0.38f, 0.58f, 0.95f, 0.95f);

        public static void Postfix(Rect rect, Pawn colonist)
        {
            if (Event.current.type != EventType.Repaint || !TSC_RpgMode.Active || colonist == null)
            {
                return;
            }
            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            float max = progression.MaxEnergy(colonist);
            if (max <= 0f)
            {
                return;
            }
            float fraction = Mathf.Clamp01(progression.EnergyOf(colonist) / max);
            float stripHeight = Mathf.Max(3f, rect.height * 0.08f);
            Rect back = new Rect(rect.x, rect.yMax - stripHeight, rect.width, stripHeight);
            GUI.color = BackColor;
            GUI.DrawTexture(back, BaseContent.WhiteTex);
            if (fraction > 0f)
            {
                GUI.color = EnergyColor;
                GUI.DrawTexture(new Rect(back.x, back.y, back.width * fraction, back.height), BaseContent.WhiteTex);
            }
            GUI.color = Color.white;
        }
    }

    /// <summary>
    /// Gives the Energy need to exactly the pawns who should have it: player
    /// humanlikes with at least one class, in the mod's scenario. Vanilla has no
    /// hook for conditional needs, hence the patch.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    public static class Patch_ShouldHaveNeed_Energy
    {
        public static void Postfix(NeedDef nd, Pawn ___pawn, ref bool __result)
        {
            if (nd != TSC_DefOf.TSC_Need_Energy)
            {
                return;
            }
            // ___pawn.Faction.IsPlayer instead of == Faction.OfPlayer: during
            // world generation the player faction does not exist yet, and the
            // OfPlayer lookup errors on every faction-leader pawn generated.
            __result = Verse.Current.Game != null && Find.World != null
                && TSC_RpgMode.Active
                && ___pawn != null && ___pawn.RaceProps.Humanlike
                && ___pawn.Faction != null && ___pawn.Faction.IsPlayer
                && TSC_ProgressionManager.Current.MaxEnergy(___pawn) > 0f;
        }
    }

    /// <summary>
    /// A thing that leaves its map without the despawn notification stays in
    /// that map's tooltip giver list forever, and the list NREs on it every
    /// OnGUI repaint (thing.Map is null by then) - the map interface dies
    /// wholesale. Seen live after the keep-drain map moves; the leaker was
    /// not identified from the stack, so this prunes stale entries before the
    /// walk and NAMES them in the log, turning the next occurrence into a
    /// one-line diagnosis instead of a broken UI.
    /// </summary>
    [HarmonyPatch(typeof(TooltipGiverList), nameof(TooltipGiverList.DispenseAllThingTooltips))]
    public static class Patch_TooltipGiverList_PruneStale
    {
        private static readonly AccessTools.FieldRef<TooltipGiverList, List<Thing>> GiversField =
            AccessTools.FieldRefAccess<TooltipGiverList, List<Thing>>("givers");

        /// <summary>
        /// Real-time throttle: the giver list holds every tooltip-bearing
        /// thing on the map and vanilla is about to walk it anyway - doubling
        /// that walk every repaint was a per-frame tax paid against a leak
        /// seen once. Real time rather than ticks so a paused game still
        /// heals; half a second bounds how long a stale entry can NRE the
        /// map UI before this catches it.
        /// </summary>
        private const float PruneEverySeconds = 0.5f;
        private static float nextPruneRealTime;

        public static void Prefix(TooltipGiverList __instance)
        {
            if (UnityEngine.Time.realtimeSinceStartup < nextPruneRealTime)
            {
                return;
            }
            nextPruneRealTime = UnityEngine.Time.realtimeSinceStartup + PruneEverySeconds;
            List<Thing> givers = GiversField(__instance);
            for (int i = givers.Count - 1; i >= 0; i--)
            {
                Thing thing = givers[i];
                if (thing == null || !thing.Spawned || thing.Map == null)
                {
                    givers.RemoveAt(i);
                    Log.Warning("[The Shattered Crown] Pruned stale tooltip giver: "
                        + (thing == null ? "null" : thing.def?.defName + " \"" + thing.LabelCap + "\"")
                        + " - it left the map without a despawn notification. Please report what preceded this.");
                }
            }
        }
    }
}
