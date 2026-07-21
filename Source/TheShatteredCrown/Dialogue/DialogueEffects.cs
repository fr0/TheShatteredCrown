using RimWorld;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    public class DialogueContext
    {
        public Pawn npc;
        public Pawn interactor;

        public DialogueContext(Pawn npc, Pawn interactor)
        {
            this.npc = npc;
            this.interactor = interactor;
        }
    }

    /// <summary>A consequence of a dialogue choice. Subclass and use Class="..." in XML.</summary>
    public abstract class DialogueEffect
    {
        public abstract void Apply(DialogueContext context);
    }

    /// <summary>Grants a quest, e.g. accepting a chapter through conversation.</summary>
    public class DialogueEffect_GiveQuest : DialogueEffect
    {
        public QuestScriptDef quest;
        public bool sendLetter;

        public override void Apply(DialogueContext context)
        {
            if (quest == null)
            {
                return;
            }
            float points = StorytellerUtility.DefaultThreatPointsNow(Find.World);
            Quest newQuest = QuestUtility.GenerateQuestAndMakeAvailable(quest, points);
            if (sendLetter && newQuest != null && !newQuest.hidden)
            {
                QuestUtility.SendLetterQuestAvailable(newQuest);
            }
        }
    }

    /// <summary>Shifts goodwill with the NPC's faction.</summary>
    public class DialogueEffect_Goodwill : DialogueEffect
    {
        public int amount;

        public override void Apply(DialogueContext context)
        {
            Faction faction = context.npc?.Faction;
            if (faction != null && faction != Faction.OfPlayer)
            {
                faction.TryAffectGoodwillWith(Faction.OfPlayer, amount);
            }
        }
    }

    /// <summary>
    /// Sends a signal into an ongoing quest generated from the given script, so a
    /// dialogue choice can drive quest logic. The quest listens with a plain
    /// &lt;inSignal&gt;YourSignal&lt;/inSignal&gt; on any QuestNode (quest-ID prefixing is
    /// handled here by deriving the prefix from the quest's own InitiateSignal).
    /// </summary>
    public class DialogueEffect_QuestSignal : DialogueEffect
    {
        public QuestScriptDef quest;
        public string signal;

        public override void Apply(DialogueContext context)
        {
            if (quest == null || signal.NullOrEmpty())
            {
                return;
            }
            foreach (Quest q in Find.QuestManager.QuestsListForReading)
            {
                if (q.root != quest || q.State != QuestState.Ongoing)
                {
                    continue;
                }
                string initiate = q.InitiateSignal;
                int dot = initiate.LastIndexOf('.');
                string prefix = dot >= 0 ? initiate.Substring(0, dot) : initiate;
                QuestUtility.SendQuestTargetSignals(new System.Collections.Generic.List<string> { prefix }, signal);
            }
        }
    }

    /// <summary>
    /// The NPC being talked to joins the player's faction - the companion-recruit
    /// effect. Clears their group AI and guest status so they become a normal
    /// colonist immediately.
    /// </summary>
    public class DialogueEffect_JoinParty : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Pawn npc = context.npc;
            if (npc == null || npc.Dead || npc.Faction == Faction.OfPlayer)
            {
                return;
            }
            npc.GetLord()?.Notify_PawnLost(npc, PawnLostCondition.ForcedByQuest);
            npc.guest?.SetGuestStatus(null);
            npc.SetFaction(Faction.OfPlayer);
            Find.LetterStack.ReceiveLetter(
                $"{npc.LabelShortCap} joins the party",
                $"{npc.LabelShortCap} has thrown in with your company and will follow you from here on.",
                LetterDefOf.PositiveEvent, npc);
        }
    }

    /// <summary>Sets (or clears) a persistent conversation flag.</summary>
    public class DialogueEffect_SetFlag : DialogueEffect
    {
        public string flag;
        public bool value = true;

        public override void Apply(DialogueContext context)
        {
            if (value)
            {
                DialogueStateManager.Current.Set(flag);
            }
            else
            {
                DialogueStateManager.Current.Clear(flag);
            }
        }
    }

    /// <summary>
    /// The talking colonist learns a class (level 1; their first class also
    /// absorbs banked level-ups). Set teachNpc to teach the NPC instead -
    /// this is how mentors grant multiclassing in-story.
    /// </summary>
    public class DialogueEffect_LearnClass : DialogueEffect
    {
        public TSC_ClassDef classDef;
        public bool teachNpc;

        public override void Apply(DialogueContext context)
        {
            Pawn learner = teachNpc ? context.npc : context.interactor;
            if (learner != null && classDef != null)
            {
                TSC_ProgressionManager.Current.LearnClass(learner, classDef);
            }
        }
    }

    /// <summary>Grants adventure-proficiency points to the talking colonist (or the NPC with teachNpc).</summary>
    public class DialogueEffect_GrantProficiency : DialogueEffect
    {
        public TSC_ProficiencyDef proficiency;
        public int points = 1;
        public bool teachNpc;

        public override void Apply(DialogueContext context)
        {
            Pawn learner = teachNpc ? context.npc : context.interactor;
            if (learner != null && proficiency != null)
            {
                TSC_ProgressionManager.Current.GrantProficiency(learner, proficiency, points);
            }
        }
    }

    /// <summary>Grants XP to the whole party - reward for meaningful conversations.</summary>
    public class DialogueEffect_GrantXp : DialogueEffect
    {
        public int xp;

        public override void Apply(DialogueContext context)
        {
            if (xp > 0)
            {
                TSC_ProgressionManager.Current.GrantXpToParty(xp, "conversation");
            }
        }
    }

    /// <summary>Shows a top-left message. Useful for feedback and placeholders.</summary>
    public class DialogueEffect_Message : DialogueEffect
    {
        public string text;
        public MessageTypeDef messageType;

        public override void Apply(DialogueContext context)
        {
            if (!text.NullOrEmpty())
            {
                Messages.Message(text, messageType ?? MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }
    }
}
