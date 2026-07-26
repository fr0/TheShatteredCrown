using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A gently bobbing chat-bubble icon over NPCs the player has never talked
    /// to. Uses the auto-set "TSC_Talked_&lt;dialogue&gt;" flag, so the bubble
    /// disappears forever the first time the conversation opens.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapComponent_TSC_DialogueIndicator : MapComponent
    {
        private static readonly Material BubbleMat =
            MaterialPool.MatFrom("Things/Mote/SpeechSymbols/Speech", ShaderDatabase.MetaOverlay);

        private const int RefreshIntervalTicks = 120;

        private readonly List<Pawn> conversable = new List<Pawn>();
        private int nextRefreshTick;

        public MapComponent_TSC_DialogueIndicator(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame < nextRefreshTick)
            {
                return;
            }
            nextRefreshTick = Find.TickManager.TicksGame + RefreshIntervalTicks;
            conversable.Clear();
            DialogueStateManager state = DialogueStateManager.Current;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.Dead || pawn.Downed || pawn.Faction == Faction.OfPlayer
                    || pawn.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }
                DialogueExtension ext = pawn.kindDef?.GetModExtension<DialogueExtension>();
                if (ext?.dialogue == null || state.IsSet(ext.dialogue.TalkedFlag))
                {
                    continue;
                }
                conversable.Add(pawn);
            }
        }

        public override void MapComponentUpdate()
        {
            if (conversable.Count == 0 || Find.CurrentMap != map
                || WorldRendererUtility.WorldRendered)
            {
                return;
            }
            DialogueStateManager state = DialogueStateManager.Current;
            float bob = 0.08f * Mathf.Sin(Time.realtimeSinceStartup * 2.4f);
            for (int i = 0; i < conversable.Count; i++)
            {
                Pawn pawn = conversable[i];
                if (pawn == null || !pawn.Spawned || pawn.Map != map || pawn.Dead)
                {
                    continue;
                }
                // Re-check the flag per frame: the talk can start while paused
                // (dialog force-pauses) and the tick refresh wouldn't run.
                DialogueExtension ext = pawn.kindDef?.GetModExtension<DialogueExtension>();
                if (ext?.dialogue == null || state.IsSet(ext.dialogue.TalkedFlag))
                {
                    continue;
                }
                // Never point at someone the player cannot see: the bubble
                // draws on the meta-overlay layer, above fog, so without this
                // it announces every unmet NPC through solid dark.
                if (!VisibleToPlayer(pawn))
                {
                    continue;
                }
                Vector3 pos = pawn.DrawPos + new Vector3(0.4f, 0f, 1.05f + bob);
                pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                Graphics.DrawMesh(MeshPool.plane10,
                    Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(0.75f, 1f, 0.75f)),
                    BubbleMat, 0);
            }
        }

        /// <summary>
        /// Vanilla fog plus, when the Real Fog of War mod is running, its own
        /// per-faction visibility grid (reached by reflection - the mod is
        /// optional and not referenced). Anything it hides, we hide.
        /// </summary>
        private bool VisibleToPlayer(Pawn pawn)
        {
            IntVec3 cell = pawn.Position;
            if (!cell.InBounds(map) || cell.Fogged(map))
            {
                return false;
            }
            if (seenFogState == FogModState.Absent)
            {
                return true;
            }
            if (seenFogState == FogModState.Unresolved)
            {
                ResolveFogMod();
                if (seenFogState == FogModState.Absent)
                {
                    return true;
                }
            }
            object shown = IsShownMethod.Invoke(seenFogComp, new object[] { Faction.OfPlayer, cell });
            return !(shown is bool visible) || visible;
        }

        private enum FogModState
        {
            Unresolved,
            Present,
            Absent,
        }

        private static MethodInfo IsShownMethod;
        private FogModState seenFogState = FogModState.Unresolved;
        private object seenFogComp;

        private void ResolveFogMod()
        {
            if (IsShownMethod == null)
            {
                System.Type type = AccessTools.TypeByName("RimWorldRealFoW.MapComponentSeenFog");
                IsShownMethod = type == null
                    ? null
                    : AccessTools.Method(type, "IsShown", new[] { typeof(Faction), typeof(IntVec3) });
                if (IsShownMethod == null)
                {
                    seenFogState = FogModState.Absent;
                    return;
                }
            }
            // The mod adds its component per map; a map generated before the
            // mod was enabled simply will not have one.
            for (int i = 0; i < map.components.Count; i++)
            {
                if (IsShownMethod.DeclaringType.IsInstanceOfType(map.components[i]))
                {
                    seenFogComp = map.components[i];
                    seenFogState = FogModState.Present;
                    return;
                }
            }
            seenFogState = FogModState.Absent;
        }
    }
}
