using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A data-driven dialogue tree, authored in XML. Nodes are looked up by name;
    /// options link nodes together, optionally gated by a skill check and carrying
    /// effects that fire when chosen.
    /// </summary>
    public class DialogueDef : Def
    {
        public string startNode = "start";

        /// <summary>Flag auto-set the first time this conversation is opened.</summary>
        public string TalkedFlag => "TSC_Talked_" + defName;

        /// <summary>
        /// Optional conditional entry points, evaluated in order; the first whose
        /// conditions all pass wins. Falls back to startNode. Lets a conversation
        /// open differently on a second meeting, while a quest is active, etc.
        /// </summary>
        public List<DialogueStart> starts = new List<DialogueStart>();

        public List<DialogueNode> nodes = new List<DialogueNode>();

        public DialogueNode GetStartNode(DialogueContext context)
        {
            foreach (DialogueStart start in starts)
            {
                bool met = true;
                foreach (DialogueCondition condition in start.conditions)
                {
                    if (!condition.Met(context))
                    {
                        met = false;
                        break;
                    }
                }
                if (met)
                {
                    return GetNode(start.node);
                }
            }
            return GetNode(startNode);
        }

        public DialogueNode GetNode(string name)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].name == name)
                {
                    return nodes[i];
                }
            }
            return null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (GetNode(startNode) == null)
            {
                yield return $"start node '{startNode}' not found";
            }
            foreach (DialogueStart start in starts)
            {
                if (GetNode(start.node) == null)
                {
                    yield return $"conditional start references missing node '{start.node}'";
                }
            }
            foreach (DialogueNode node in nodes)
            {
                if (node.name.NullOrEmpty())
                {
                    yield return "node with null or empty name";
                    continue;
                }
                if (nodes.Count(n => n.name == node.name) > 1)
                {
                    yield return $"duplicate node name '{node.name}'";
                }
                foreach (DialogueOption option in node.options)
                {
                    if (!option.linkTo.NullOrEmpty() && GetNode(option.linkTo) == null)
                    {
                        yield return $"node '{node.name}': option links to missing node '{option.linkTo}'";
                    }
                    if (option.check != null)
                    {
                        if (option.check.proficiency == null)
                        {
                            yield return $"node '{node.name}': check needs a proficiency";
                        }
                        if (!option.check.successLink.NullOrEmpty() && GetNode(option.check.successLink) == null)
                        {
                            yield return $"node '{node.name}': successLink to missing node '{option.check.successLink}'";
                        }
                        if (!option.check.failLink.NullOrEmpty() && GetNode(option.check.failLink) == null)
                        {
                            yield return $"node '{node.name}': failLink to missing node '{option.check.failLink}'";
                        }
                    }
                }
            }
        }
    }

    public class DialogueStart
    {
        public string node;
        public List<DialogueCondition> conditions = new List<DialogueCondition>();
    }

    public class DialogueNode
    {
        public string name;
        public string text;
        public List<DialogueOption> options = new List<DialogueOption>();
    }

    public class DialogueOption
    {
        public string text;

        /// <summary>Next node when chosen. Null or empty ends the conversation (unless a check redirects).</summary>
        public string linkTo;

        /// <summary>Optional d10 proficiency check; on success/fail follows its own links and effects.</summary>
        public DialogueSkillCheck check;

        /// <summary>Applied whenever this option is chosen, before any check resolves.</summary>
        public List<DialogueEffect> effects = new List<DialogueEffect>();

        /// <summary>All must pass for the option to be shown.</summary>
        public List<DialogueCondition> conditions = new List<DialogueCondition>();

        public bool Available(DialogueContext context)
        {
            // A once-per-save check that has already been rolled stays hidden,
            // pass or fail: no re-rolling until it lands, no reward farming.
            // (Per-NPC checks scope that to the character being talked to.)
            string onceKey = check?.OnceKeyFor(context.npc);
            if (!onceKey.NullOrEmpty() && DialogueStateManager.Current.IsSet(onceKey))
            {
                return false;
            }
            // A retryable check that recently failed is cooling down: hidden
            // until the retry time passes (the fail node's text carries the
            // "come back later" fiction).
            if (check != null && !check.retryKey.NullOrEmpty() && DialogueStateManager.Current.IsCoolingDown(check.retryKey))
            {
                return false;
            }
            foreach (DialogueCondition condition in conditions)
            {
                if (!condition.Met(context))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public class DialogueSkillCheck
    {
        /// <summary>Adventure proficiency to check (Lore, Thievery...). Vanilla skills feed in via synergy.</summary>
        public TSC_ProficiencyDef proficiency;

        /// <summary>d10 + effective proficiency bonus must meet or beat this.</summary>
        public int difficulty = 10;

        public string successLink;
        public string failLink;
        public List<DialogueEffect> successEffects = new List<DialogueEffect>();
        public List<DialogueEffect> failEffects = new List<DialogueEffect>();

        /// <summary>
        /// Set when the check is rolled (regardless of outcome); the option hides
        /// once it is set, making checks once-per-save. Compiler-generated; empty
        /// means the check is retryable (DSL keyword 'retryable').
        /// </summary>
        [NoTranslate]
        public string onceKey;

        /// <summary>
        /// Scope the once key to the NAMED CHARACTER being talked to instead
        /// of the whole save. For shared scenes ({NPC} carries the name, one
        /// DialogueDef for many companions - part_ways_ask), a save-global key
        /// means the first companion's roll consumes everyone else's: Oswin
        /// gets talked into staying and Serra's first ask offers no chance at
        /// all. DSL keyword 'per_npc'.
        /// </summary>
        public bool oncePerNpc;

        /// <summary>The once key as it applies to THIS conversation partner.</summary>
        public string OnceKeyFor(Verse.Pawn npc)
        {
            if (onceKey.NullOrEmpty() || !oncePerNpc)
            {
                return onceKey;
            }
            NamedNpcDef def = DialogueStateManager.Current?.NpcDefFor(npc);
            return def == null ? onceKey : onceKey + "_" + def.defName;
        }

        /// <summary>
        /// Retryable checks: a FAILED roll hides the option for retryHours of
        /// in-game time ("the moment has passed - come back later") instead of
        /// allowing an immediate re-click re-roll. DSL: 'retryable' (8h) or
        /// 'retryable(N)' for N hours.
        /// </summary>
        [NoTranslate]
        public string retryKey;
        public float retryHours = 4f;
    }

    /// <summary>Attach to a PawnKindDef to make pawns of that kind conversable.</summary>
    public class DialogueExtension : DefModExtension
    {
        public DialogueDef dialogue;

        /// <summary>Conversations this pawn may start on their own (companions seeking out the player).</summary>
        public List<DialogueInitiation> initiations = new List<DialogueInitiation>();
    }

    /// <summary>
    /// A conversation an NPC can initiate themselves. Checked hourly while the
    /// pawn is a spawned free colonist on a calm player map; when conditions and
    /// the mtb roll pass, the pawn walks to the protagonist (or, if they're dead,
    /// the nearest living colonist) and the window opens on arrival.
    /// </summary>
    public class DialogueInitiation
    {
        public DialogueDef dialogue;
        public List<DialogueCondition> conditions = new List<DialogueCondition>();

        /// <summary>Mean time between fires, in days, once conditions hold.</summary>
        public float mtbDays = 1f;

        /// <summary>Fire at most once per save.</summary>
        public bool once = true;

        public string Key(PawnKindDef kind)
        {
            return kind.defName + ":" + (dialogue != null ? dialogue.defName : "null");
        }
    }
}
