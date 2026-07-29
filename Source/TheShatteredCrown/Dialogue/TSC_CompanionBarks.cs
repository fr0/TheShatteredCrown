using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Companion combat barks: while a fight is ENGAGED on the map, named
    /// companions occasionally throw a one-liner over their heads - the same
    /// floating-text pattern as Aldis's song, in a cooler color so chatter
    /// never reads as plot. Lines live on the NamedNpcDef (combatBarks);
    /// timing is per-pawn and jittered so the party never barks in chorus.
    /// Transient by design: schedules are not saved, and the first bark of
    /// a fight arrives a few seconds in, not on contact.
    /// </summary>
    public class MapComponent_TSC_CompanionBarks : MapComponent
    {
        private static readonly Color BarkColor = new Color(0.82f, 0.86f, 0.95f);
        private static readonly IntRange FirstBarkDelay = new IntRange(300, 900);
        private static readonly IntRange BetweenBarks = new IntRange(1200, 3000);

        private readonly Dictionary<Pawn, int> nextBark = new Dictionary<Pawn, int>();
        private bool fighting;

        public MapComponent_TSC_CompanionBarks(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 60 != 0)
            {
                return;
            }
            if (!TSC_EncounterController.AnyEngagedHostileOn(map))
            {
                if (fighting)
                {
                    fighting = false;
                    nextBark.Clear(); // quiet again; next fight starts fresh
                }
                return;
            }
            fighting = true;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Dead || pawn.Downed)
                {
                    continue;
                }
                NamedNpcDef def = DialogueStateManager.Current.NpcDefFor(pawn);
                if (def?.combatBarks == null || def.combatBarks.Count == 0)
                {
                    continue;
                }
                if (!nextBark.TryGetValue(pawn, out int due))
                {
                    nextBark[pawn] = now + FirstBarkDelay.RandomInRange;
                    continue;
                }
                if (now < due)
                {
                    continue;
                }
                nextBark[pawn] = now + BetweenBarks.RandomInRange;
                string line = PickLine(pawn, def);
                if (line != null)
                {
                    MoteMaker.ThrowText(pawn.DrawPos, map, line, BarkColor);
                }
            }
        }

        /// <summary>
        /// A bark that names the companion's animal ("Corvus. Eyes.") only
        /// fires while that animal is alive and bonded - a dead raven does
        /// not get orders. Everything else stays in the pool.
        /// </summary>
        private static string PickLine(Pawn pawn, NamedNpcDef def)
        {
            List<string> pool = def.combatBarks;
            if (!def.petName.NullOrEmpty() && !PetAlive(pawn, def.petName))
            {
                List<string> filtered = null;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (!pool[i].Contains(def.petName))
                    {
                        (filtered ?? (filtered = new List<string>())).Add(pool[i]);
                    }
                }
                pool = filtered;
            }
            return pool.NullOrEmpty() ? null : pool.RandomElement();
        }

        private static bool PetAlive(Pawn owner, string petName)
        {
            if (owner.relations == null)
            {
                return false;
            }
            foreach (DirectPawnRelation relation in owner.relations.DirectRelations)
            {
                if (relation.def == PawnRelationDefOf.Bond && relation.otherPawn != null
                    && !relation.otherPawn.Dead
                    && relation.otherPawn.Name?.ToStringShort == petName)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
