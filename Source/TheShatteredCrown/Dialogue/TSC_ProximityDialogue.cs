using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A thing that speaks up when first SEEN: any free colonist coming
    /// within radius with line of sight opens the dialogue, once per save.
    ///
    /// This is the set-piece introduction problem: the hollow choir is a
    /// wall of seated skulls with a story, but nothing explained it unless
    /// the player happened to right-click it. Now walking into the room is
    /// the trigger, the same way a person notices the thing before deciding
    /// what to do about it. The check-spot options remain the follow-up.
    /// </summary>
    public class CompProperties_TSC_ProximityDialogue : CompProperties
    {
        public DialogueDef dialogue;
        public float radius = 7.9f;

        public CompProperties_TSC_ProximityDialogue()
        {
            compClass = typeof(Comp_TSC_ProximityDialogue);
        }
    }

    public class Comp_TSC_ProximityDialogue : ThingComp
    {
        public CompProperties_TSC_ProximityDialogue Props => (CompProperties_TSC_ProximityDialogue)props;

        /// <summary>Once per save, tracked in the dialogue flag store like every other one-shot.</summary>
        public string SeenFlag => $"TSC_Seen_{Props.dialogue?.defName}";

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Map?.GetComponent<MapComponent_TSC_ProximityDialogues>()?.Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            map?.GetComponent<MapComponent_TSC_ProximityDialogues>()?.Deregister(this);
        }
    }

    /// <summary>
    /// The watcher. Comps register themselves on spawn (buildings never tick
    /// - the Kingsblade lesson), so each pulse only looks at the handful of
    /// registered set pieces rather than sweeping the map's thing list.
    /// </summary>
    public class MapComponent_TSC_ProximityDialogues : MapComponent
    {
        private const int Interval = 60;

        private readonly List<Comp_TSC_ProximityDialogue> spots = new List<Comp_TSC_ProximityDialogue>();

        public MapComponent_TSC_ProximityDialogues(Map map) : base(map)
        {
        }

        public void Register(Comp_TSC_ProximityDialogue comp)
        {
            if (!spots.Contains(comp))
            {
                spots.Add(comp);
            }
        }

        public void Deregister(Comp_TSC_ProximityDialogue comp)
        {
            spots.Remove(comp);
        }

        public override void MapComponentTick()
        {
            if (spots.Count == 0 || Find.TickManager.TicksGame % Interval != 0)
            {
                return;
            }
            for (int i = spots.Count - 1; i >= 0; i--)
            {
                Comp_TSC_ProximityDialogue spot = spots[i];
                if (spot?.parent == null || spot.parent.Destroyed || !spot.parent.Spawned
                    || spot.Props.dialogue == null)
                {
                    spots.RemoveAt(i);
                    continue;
                }
                if (DialogueStateManager.Current.IsSet(spot.SeenFlag))
                {
                    spots.RemoveAt(i); // already introduced; stop watching
                    continue;
                }
                Pawn witness = FindWitness(spot);
                if (witness == null)
                {
                    continue;
                }
                DialogueStateManager.Current.Set(spot.SeenFlag);
                spots.RemoveAt(i);
                Find.WindowStack.Add(new Dialog_Conversation(spot.Props.dialogue, witness, witness));
                // One introduction per pulse: two set pieces seen in the same
                // instant should not stack two pause windows.
                return;
            }
        }

        private Pawn FindWitness(Comp_TSC_ProximityDialogue spot)
        {
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn.Dead || pawn.Downed
                    || !pawn.Position.InHorDistOf(spot.parent.Position, spot.Props.radius))
                {
                    continue;
                }
                if (GenSight.LineOfSight(pawn.Position, spot.parent.Position, map, skipFirstCell: true))
                {
                    return pawn;
                }
            }
            return null;
        }
    }
}
