using System.Collections.Generic;
using RimWorld;
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
            if (conversable.Count == 0 || Find.CurrentMap != map)
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
                Vector3 pos = pawn.DrawPos + new Vector3(0.4f, 0f, 1.05f + bob);
                pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                Graphics.DrawMesh(MeshPool.plane10,
                    Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(0.75f, 1f, 0.75f)),
                    BubbleMat, 0);
            }
        }
    }
}
