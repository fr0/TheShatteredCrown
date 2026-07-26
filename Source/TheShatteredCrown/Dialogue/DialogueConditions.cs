using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A visibility gate for dialogue options and entry points. Subclass and use
    /// Class="..." in XML. All conditions on an element must pass for it to show.
    /// </summary>
    public abstract class DialogueCondition
    {
        public abstract bool Met(DialogueContext context);
    }

    public class DialogueCondition_FlagSet : DialogueCondition
    {
        public string flag;

        public override bool Met(DialogueContext context)
        {
            return DialogueStateManager.Current.IsSet(flag);
        }
    }

    public class DialogueCondition_FlagNotSet : DialogueCondition
    {
        public string flag;

        public override bool Met(DialogueContext context)
        {
            return !DialogueStateManager.Current.IsSet(flag);
        }
    }

    /// <summary>
    /// True when the named character's affinity lies within [min, max]. With no
    /// npc set, checks the pawn being talked to. Gate warm confidences behind
    /// min, cold brush-offs behind max.
    /// </summary>
    public class DialogueCondition_Affinity : DialogueCondition
    {
        public NamedNpcDef npc;
        public int min = int.MinValue;
        public int max = int.MaxValue;

        public override bool Met(DialogueContext context)
        {
            NamedNpcDef def = npc ?? DialogueStateManager.Current.NpcDefFor(context.npc);
            int value = DialogueStateManager.Current.AffinityOf(def);
            return value >= min && value <= max;
        }
    }

    /// <summary>
    /// True when a companion is in the player's faction. With no npc set, it
    /// checks the pawn currently being talked to.
    /// </summary>
    public class DialogueCondition_InParty : DialogueCondition
    {
        public NamedNpcDef npc;

        public override bool Met(DialogueContext context)
        {
            Pawn pawn = npc != null ? DialogueStateManager.Current.GetNamedNpcIfExists(npc) : context.npc;
            return pawn != null && !pawn.Dead && pawn.Faction == Faction.OfPlayer;
        }
    }

    /// <summary>
    /// True when the party's pooled goods on the talking colonist's map
    /// (inventories, carried stacks, loose spawned items) hold at least
    /// count of the def. Gates hand-over options so take_item can't fire
    /// short. DSL: has_item(Def[, count]); "not has_item" sets invert.
    /// </summary>
    public class DialogueCondition_HasItem : DialogueCondition
    {
        public ThingDef def;
        public int count = 1;
        public bool invert;

        public override bool Met(DialogueContext context)
        {
            bool has = TSC_PartyItems.Count(context.interactor?.MapHeld, def) >= count;
            return invert ? !has : has;
        }
    }

    /// <summary>
    /// Passive check: true when 5 (the d10 midpoint) + the party's best effective
    /// proficiency meets the DC. Deterministic (no die), so option visibility is
    /// stable - use for noticing things mid-conversation ("[Insight] She's lying.").
    /// </summary>
    public class DialogueCondition_Passive : DialogueCondition
    {
        public TSC_ProficiencyDef proficiency;
        public int difficulty = 12;

        public override bool Met(DialogueContext context)
        {
            if (proficiency == null)
            {
                return false;
            }
            return TSC_ProgressionManager.Current.PassivePartyCheck(context.interactor, context.npc, proficiency, difficulty);
        }
    }

    /// <summary>
    /// True when a named character is spawned on the same map as this conversation
    /// and within radius tiles of the NPC being talked to. Pair with in_party() for
    /// "your companion is standing right here" reactions.
    /// </summary>
    public class DialogueCondition_Nearby : DialogueCondition
    {
        public NamedNpcDef npc;
        public float radius = 20f;

        public override bool Met(DialogueContext context)
        {
            if (npc == null || context.npc == null || !context.npc.Spawned)
            {
                return false;
            }
            Pawn pawn = DialogueStateManager.Current.GetNamedNpcIfExists(npc);
            return pawn != null && !pawn.Dead && pawn.Spawned
                && pawn.Map == context.npc.Map
                && pawn.Position.InHorDistOf(context.npc.Position, radius);
        }
    }

    /// <summary>
    /// True when a named character has been generated and is dead. Destroyed-but-
    /// not-dead (site despawned under them) does NOT count: narratively they are
    /// alive, just elsewhere.
    /// </summary>
    public class DialogueCondition_NpcDead : DialogueCondition
    {
        public NamedNpcDef npc;

        public override bool Met(DialogueContext context)
        {
            Pawn pawn = npc != null ? DialogueStateManager.Current.GetNamedNpcIfExists(npc) : null;
            return pawn != null && pawn.Dead;
        }
    }

    /// <summary>Logical negation of NpcDead: never generated, or generated and living.</summary>
    public class DialogueCondition_NpcNotDead : DialogueCondition
    {
        public NamedNpcDef npc;

        public override bool Met(DialogueContext context)
        {
            Pawn pawn = npc != null ? DialogueStateManager.Current.GetNamedNpcIfExists(npc) : null;
            return pawn == null || !pawn.Dead;
        }
    }

    /// <summary>True while any quest generated from this script is ongoing or awaiting acceptance.</summary>
    public class DialogueCondition_QuestActive : DialogueCondition
    {
        public QuestScriptDef quest;

        public override bool Met(DialogueContext context)
        {
            if (quest == null)
            {
                return false;
            }
            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.root == quest && (q.State == QuestState.Ongoing || q.State == QuestState.NotYetAccepted))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>True when the NPC being talked to is asleep - route to a groggy night node (pair with the wake() effect).</summary>
    public class DialogueCondition_Sleeping : DialogueCondition
    {
        public override bool Met(DialogueContext context)
        {
            return context.npc != null && !context.npc.Awake();
        }
    }

    /// <summary>Negation of Sleeping ('not sleeping()').</summary>
    public class DialogueCondition_Awake : DialogueCondition
    {
        public override bool Met(DialogueContext context)
        {
            return context.npc == null || context.npc.Awake();
        }
    }

    /// <summary>
    /// True when a living, player-owned pawn of the given kind is spawned on
    /// the same map as the NPC being talked to. Gates dialogue on a creature
    /// physically being present - e.g. Oswin's rites need the ettersnap AT the
    /// camp, not merely owned on some other map.
    /// </summary>
    public class DialogueCondition_KindOnMap : DialogueCondition
    {
        public PawnKindDef kind;

        public override bool Met(DialogueContext context)
        {
            if (kind == null || context.npc == null || !context.npc.Spawned)
            {
                return false;
            }
            foreach (Pawn pawn in context.npc.Map.mapPawns.PawnsInFaction(Faction.OfPlayer))
            {
                if (pawn.kindDef == kind && !pawn.Dead)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// True when NO quest from this script is ongoing or awaiting acceptance -
    /// the "the trail went cold" gate for re-offering a lost objective
    /// (Serra re-marking the ettersnap cave).
    /// </summary>
    public class DialogueCondition_QuestNotActive : DialogueCondition
    {
        public QuestScriptDef quest;

        public override bool Met(DialogueContext context)
        {
            if (quest == null)
            {
                return false;
            }
            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.root == quest && (q.State == QuestState.Ongoing || q.State == QuestState.NotYetAccepted))
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>True once any quest generated from this script ended in success.</summary>
    public class DialogueCondition_QuestSucceeded : DialogueCondition
    {
        public QuestScriptDef quest;

        public override bool Met(DialogueContext context)
        {
            if (quest == null)
            {
                return false;
            }
            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.root == quest && q.State == QuestState.EndedSuccess)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// True when at least <c>min</c> of the listed quest scripts have ended in
    /// success. The "earn the village's trust" gate: Old Wick talks about the
    /// crypt only after the courier has helped three of his neighbors.
    /// </summary>
    public class DialogueCondition_MinQuestsSucceeded : DialogueCondition
    {
        public System.Collections.Generic.List<QuestScriptDef> quests;
        public int min = 1;

        public override bool Met(DialogueContext context)
        {
            if (quests.NullOrEmpty())
            {
                return false;
            }
            int succeeded = 0;
            foreach (QuestScriptDef script in quests)
            {
                foreach (Quest q in Find.QuestManager.QuestsListForReading)
                {
                    if (q.root == script && q.State == QuestState.EndedSuccess)
                    {
                        succeeded++;
                        break;
                    }
                }
            }
            return succeeded >= min;
        }
    }

    /// <summary>Negation of MinQuestsSucceeded ('not min_quests_succeeded(...)') - the "not yet trusted" branch.</summary>
    public class DialogueCondition_NotMinQuestsSucceeded : DialogueCondition_MinQuestsSucceeded
    {
        public override bool Met(DialogueContext context)
        {
            return !base.Met(context);
        }
    }

    /// <summary>True while NO quest from this script has ended in success - pairs with quest-not-active for re-offer gates.</summary>
    public class DialogueCondition_QuestNotSucceeded : DialogueCondition
    {
        public QuestScriptDef quest;

        public override bool Met(DialogueContext context)
        {
            if (quest == null)
            {
                return false;
            }
            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.root == quest && q.State == QuestState.EndedSuccess)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
