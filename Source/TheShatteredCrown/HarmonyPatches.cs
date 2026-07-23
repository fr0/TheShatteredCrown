using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    [StaticConstructorOnStartup]
    public static class TSC_HarmonyInit
    {
        static TSC_HarmonyInit()
        {
            new Harmony("cfrolik.theshatteredcrown").PatchAll();
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
        private static readonly Dictionary<Pawn, int> lastNotifyTick = new Dictionary<Pawn, int>();

        public static bool Protects(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
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
            if (!lastNotifyTick.TryGetValue(pawn, out int last) || now - last > 2500)
            {
                lastNotifyTick[pawn] = now;
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
}
