using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Two companions talking to each other, with nobody's hand on the wheel.
    ///
    /// Banter is authored as ordinary DSL dialogue and shown in the
    /// conversation window: the player is an eavesdropper with a portrait
    /// and a transcript, not a reader of floating text that scrolled away.
    /// Because a window cannot be missed, every exchange fires exactly once
    /// per save - no cooldowns, no repeats - and because a window needs no
    /// heads to hover over, the road counts: banter reaches the party in a
    /// caravan on the world map as readily as around a campfire.
    ///
    /// This def is only the TRIGGER: who must be present, what must be
    /// true, and which dialogue opens. The words live in Dialogues/*.agd
    /// with everything else the companions say.
    /// </summary>
    public class TSC_BanterDef : Def
    {
        /// <summary>Both must be in the party and together. [0] is the window's npc, [1] the partner.</summary>
        public List<NamedNpcDef> speakers = new List<NamedNpcDef>();

        /// <summary>The conversation that opens.</summary>
        public DialogueDef dialogue;

        /// <summary>Relative odds against the other eligible banters.</summary>
        public float weight = 1f;

        /// <summary>All must pass, evaluated with the two speakers as the context pair.</summary>
        public List<DialogueCondition> conditions;

        /// <summary>The flag that records this one has been heard.</summary>
        public string HeardFlag => "TSC_Banter_" + defName;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (speakers.Count < 2)
            {
                yield return "banter needs two speakers";
            }
            if (dialogue == null)
            {
                yield return "banter has no dialogue to open";
            }
        }
    }

    /// <summary>
    /// The one clock for all banter: waits out a silence, finds an exchange
    /// whose speakers are standing (or riding) together, and opens it.
    /// </summary>
    public class GameComponent_TSC_Banter : GameComponent
    {
        private const int CheckInterval = 120;

        /// <summary>How far apart two spawned companions can be and still be talking.</summary>
        private const float TalkingDistance = 10f;

        private static readonly IntRange BetweenBanters = new IntRange(4500, 12000);

        private int nextBanterTick = -1;

        public GameComponent_TSC_Banter(Game game)
        {
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % CheckInterval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            if (nextBanterTick < 0)
            {
                nextBanterTick = now + BetweenBanters.RandomInRange;
                return; // never on the first tick of a load; let them settle
            }
            if (now < nextBanterTick)
            {
                return;
            }
            nextBanterTick = now + BetweenBanters.RandomInRange;
            TryStart();
        }

        private void TryStart()
        {
            List<Pawn> company = Company();
            if (company.Count < 2)
            {
                return;
            }
            DialogueStateManager state = DialogueStateManager.Current;
            if (state == null)
            {
                return;
            }
            List<TSC_BanterDef> eligible = null;
            List<List<Pawn>> casts = null;
            float total = 0f;
            foreach (TSC_BanterDef def in DefDatabase<TSC_BanterDef>.AllDefsListForReading)
            {
                if (def.dialogue == null || state.IsSet(def.HeardFlag))
                {
                    continue;
                }
                List<Pawn> cast = ResolveCast(def, company);
                if (cast == null)
                {
                    continue;
                }
                (eligible ?? (eligible = new List<TSC_BanterDef>())).Add(def);
                (casts ?? (casts = new List<List<Pawn>>())).Add(cast);
                total += Mathf.Max(0.01f, def.weight);
            }
            if (eligible == null)
            {
                return;
            }
            float roll = Rand.Range(0f, total);
            for (int i = 0; i < eligible.Count; i++)
            {
                roll -= Mathf.Max(0.01f, eligible[i].weight);
                if (roll > 0f)
                {
                    continue;
                }
                Open(eligible[i], casts[i]);
                return;
            }
        }

        private static void Open(TSC_BanterDef def, List<Pawn> cast)
        {
            // Heard the moment it opens: a modal window cannot be missed,
            // and marking here is what makes once mean once even if the
            // save happens mid-conversation.
            DialogueStateManager.Current.Set(def.HeardFlag);
            Find.WindowStack.Add(new Dialog_Conversation(def.dialogue, cast[0], cast[1]));
        }

        /// <summary>
        /// The pawns this banter needs, or null: everyone present, awake,
        /// upright, and - when spawned - close enough to be talking rather
        /// than shouting across a map. A caravan is one campfire; riding
        /// together IS being together.
        /// </summary>
        private static List<Pawn> ResolveCast(TSC_BanterDef def, List<Pawn> company)
        {
            List<Pawn> found = new List<Pawn>();
            DialogueStateManager state = DialogueStateManager.Current;
            foreach (NamedNpcDef who in def.speakers)
            {
                Pawn pawn = state.GetNamedNpcIfExists(who);
                if (pawn == null || !company.Contains(pawn) || !CanSpeak(pawn))
                {
                    return null;
                }
                found.Add(pawn);
            }
            for (int i = 1; i < found.Count; i++)
            {
                if (found[i].MapHeld != found[0].MapHeld)
                {
                    return null;
                }
                if (found[0].Spawned && found[i].Position.DistanceTo(found[0].Position) > TalkingDistance)
                {
                    return null;
                }
            }
            if (!def.conditions.NullOrEmpty())
            {
                DialogueContext context = new DialogueContext(found[0], found[1]);
                foreach (DialogueCondition condition in def.conditions)
                {
                    if (!condition.Met(context))
                    {
                        return null;
                    }
                }
            }
            return found;
        }

        private static bool CanSpeak(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Downed && pawn.Awake()
                && !pawn.InMentalState
                && (pawn.Spawned || pawn.IsCaravanMember());
        }

        /// <summary>
        /// Everyone who could be in earshot of each other: one map's calm
        /// colonists, or one caravan's riders. A fight anywhere in the group
        /// ends the subject, and an open conversation matters more.
        /// </summary>
        private static List<Pawn> Company()
        {
            List<Pawn> company = new List<Pawn>();
            if (Find.WindowStack?.WindowOfType<Dialog_Conversation>() != null)
            {
                return company;
            }
            foreach (Map map in Find.Maps)
            {
                if (map.mapPawns.FreeColonistsSpawnedCount < 2
                    || GenHostility.AnyHostileActiveThreatToPlayer(map, countDormantPawnsAsHostile: false))
                {
                    continue;
                }
                company.AddRange(map.mapPawns.FreeColonistsSpawned);
                if (company.Count >= 2)
                {
                    return company;
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled)
                {
                    continue;
                }
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    if (pawn.IsFreeColonist)
                    {
                        company.Add(pawn);
                    }
                }
                if (company.Count >= 2)
                {
                    return company;
                }
                company.Clear();
            }
            return company;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextBanterTick, "tscNextBanterTick", -1);
        }
    }
}
