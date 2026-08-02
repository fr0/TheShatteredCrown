using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// The tavern: buy the company a night, and the road comes off them.
    ///
    /// Clears the SHORT memories - the day's accumulated grievances, the
    /// corpse someone walked past, the meal eaten off a rock - and leaves a
    /// good evening behind it. Deliberately does NOT touch long memories:
    /// grief, betrayal and the things that take a season to fade are not
    /// drinkable, and a tavern that erased them would make every death in
    /// the campaign cheap.
    /// </summary>
    public static class TSC_Tavern
    {
        /// <summary>Memories longer than this are life, not a bad day; ale does not touch them.</summary>
        public const float MaxDurationDays = 5f;
        public const int SilverPerHead = 45;

        public static int PriceFor(Map map)
        {
            // The house rate, adjusted for how the village feels about the
            // company drinking in it (TSC_VillageStanding).
            return TSC_VillageStanding.Apply(Heads(map).Count * SilverPerHead, map);
        }

        public static List<Pawn> Heads(Map map)
        {
            List<Pawn> drinkers = new List<Pawn>();
            if (map == null)
            {
                return drinkers;
            }
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.needs?.mood?.thoughts?.memories != null)
                {
                    drinkers.Add(pawn);
                }
            }
            return drinkers;
        }

        /// <summary>Short-lived grievances this pawn is carrying right now.</summary>
        public static int GrievanceCount(Pawn pawn)
        {
            int count = 0;
            List<Thought_Memory> memories = pawn?.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null)
            {
                return 0;
            }
            for (int i = 0; i < memories.Count; i++)
            {
                if (Washable(memories[i]))
                {
                    count++;
                }
            }
            return count;
        }

        private static bool Washable(Thought_Memory memory)
        {
            return memory != null
                && memory.MoodOffset() < 0f
                && memory.def != null
                && memory.def.durationDays > 0f
                && memory.def.durationDays <= MaxDurationDays;
        }

        /// <summary>A night at the tavern: the short grievances go, and a good evening replaces them.</summary>
        public static void Carouse(Pawn pawn)
        {
            ThoughtHandler thoughts = pawn?.needs?.mood?.thoughts;
            if (thoughts?.memories == null)
            {
                return;
            }
            List<Thought_Memory> doomed = new List<Thought_Memory>();
            foreach (Thought_Memory memory in thoughts.memories.Memories)
            {
                if (Washable(memory))
                {
                    doomed.Add(memory);
                }
            }
            foreach (Thought_Memory memory in doomed)
            {
                thoughts.memories.RemoveMemory(memory);
            }
            ThoughtDef night = DefDatabase<ThoughtDef>.GetNamedSilentFail("TSC_Thought_TavernNight");
            if (night != null)
            {
                thoughts.memories.TryGainMemory(night);
            }
        }
    }

    /// <summary>DSL effect tavern_round(): the innkeeper opens the slate.</summary>
    public class DialogueEffect_TSC_Tavern : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Find.WindowStack.Add(new Window_TSC_Tavern(context.interactor));
        }
    }

    public class Window_TSC_Tavern : Window
    {
        private readonly Pawn visitor;
        private Vector2 scroll;

        public Window_TSC_Tavern(Pawn visitor)
        {
            this.visitor = visitor;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(620f, 520f);

        public override void DoWindowContents(Rect inRect)
        {
            Map map = visitor?.MapHeld;
            int silver = TSC_Temple.SilverOnHand(map);
            List<Pawn> drinkers = TSC_Tavern.Heads(map);
            int price = TSC_Tavern.PriceFor(map);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 200f, 34f), "The common room");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.85f, 0.85f, 0.9f);
            Widgets.Label(new Rect(inRect.width - 200f, 4f, 200f, 26f), $"Silver: {silver}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            TSC_StandingNote.Draw(new Rect(0f, 34f, inRect.width, 22f), map);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            Widgets.Label(new Rect(0f, 32f, inRect.width, 40f),
                "\"Hot food, a bench, and enough noise that nobody has to think. "
                + "It won't fix what's really eating them, and I'd not trust a house that promised it would.\"");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect body = new Rect(0f, 78f, inRect.width, inRect.height - 78f - CloseButSize.y - 44f);
            if (drinkers.Count == 0)
            {
                Widgets.Label(body, "\"Nobody here to serve.\"");
                return;
            }
            Rect view = new Rect(0f, 0f, body.width - 16f, drinkers.Count * 30f);
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            foreach (Pawn pawn in drinkers)
            {
                int grievances = TSC_Tavern.GrievanceCount(pawn);
                Widgets.Label(new Rect(0f, y, view.width - 200f, 24f), pawn.LabelShortCap);
                GUI.color = grievances > 0 ? new Color(0.9f, 0.75f, 0.55f) : new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(view.width - 200f, y, 200f, 24f),
                    grievances == 0 ? "nothing to wash off"
                        : grievances == 1 ? "1 grievance" : $"{grievances} grievances");
                GUI.color = Color.white;
                y += 30f;
            }
            Widgets.EndScrollView();

            Rect button = new Rect(inRect.width / 2f - 160f, inRect.height - CloseButSize.y - 40f, 320f, 32f);
            bool afford = silver >= price;
            if (!afford)
            {
                GUI.color = Color.gray;
            }
            if (Widgets.ButtonText(button, $"Buy the house a round ({price})") && afford
                && TSC_Temple.TakeSilver(map, price))
            {
                foreach (Pawn pawn in drinkers)
                {
                    TSC_Tavern.Carouse(pawn);
                }
                SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
                Messages.Message("The company drinks, eats, and argues about nothing until the candles burn down.",
                    MessageTypeDefOf.PositiveEvent, historical: false);
                Close();
            }
            GUI.color = Color.white;
        }
    }
}
