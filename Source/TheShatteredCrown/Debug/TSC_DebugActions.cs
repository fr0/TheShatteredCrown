using System.Linq;
using LudeonTK;
using Verse;

namespace TheShatteredCrown
{
    public static class TSC_DebugActions
    {
        /// <summary>The RPG layer is scenario-gated; XP/class debug tools warn instead of leaking it into other saves.</summary>
        private static bool RpgGate()
        {
            if (TSC_RpgMode.Active)
            {
                return true;
            }
            Messages.Message("The Shattered Crown RPG systems are only active in games started from its scenario.", RimWorld.MessageTypeDefOf.RejectInput, historical: false);
            return false;
        }

        [DebugAction("The Shattered Crown", "Toggle RPG mode override", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ToggleRpgOverride()
        {
            TSC_RpgMode.debugOverride = !TSC_RpgMode.debugOverride;
            Messages.Message(
                TSC_RpgMode.debugOverride
                    ? "RPG systems FORCED ON for this session (any scenario). Dev testing only."
                    : "RPG systems override off (scenario gating restored).",
                RimWorld.MessageTypeDefOf.NeutralEvent, historical: false);
        }

        [DebugAction("The Shattered Crown", "Add XP to party...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AddXpToParty()
        {
            if (!RpgGate())
            {
                return;
            }
            System.Collections.Generic.List<DebugMenuOption> options = new System.Collections.Generic.List<DebugMenuOption>();
            foreach (int amount in new[] { 50, 100, 250, 500, 1000, 5000 })
            {
                int local = amount;
                options.Add(new DebugMenuOption($"{local} XP", DebugMenuOptionMode.Action, delegate
                {
                    TSC_ProgressionManager.Current.GrantXpToParty(local, "debug");
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("The Shattered Crown", "Add 100 XP to pawn", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AddXpToPawn(Pawn pawn)
        {
            if (!RpgGate())
            {
                return;
            }
            TSC_ProgressionManager.Current.GrantXpToPawn(pawn, 100, "debug");
        }

        [DebugAction("The Shattered Crown", "Learn class...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LearnClassTool()
        {
            if (!RpgGate())
            {
                return;
            }
            System.Collections.Generic.List<DebugMenuOption> options = new System.Collections.Generic.List<DebugMenuOption>();
            foreach (TSC_ClassDef classDef in DefDatabase<TSC_ClassDef>.AllDefsListForReading)
            {
                TSC_ClassDef local = classDef;
                options.Add(new DebugMenuOption(local.defName, DebugMenuOptionMode.Tool, delegate
                {
                    foreach (Verse.Thing thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
                    {
                        if (thing is Pawn pawn)
                        {
                            TSC_ProgressionManager.Current.LearnClass(pawn, local);
                            break;
                        }
                    }
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("The Shattered Crown", "Open test dialogue...", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void OpenTestDialogue(Pawn pawn)
        {
            DialogueDef def = DefDatabase<DialogueDef>.AllDefsListForReading.FirstOrDefault();
            if (def == null)
            {
                Log.Warning("[The Shattered Crown] No DialogueDefs loaded.");
                return;
            }
            Pawn talker = pawn.Map?.mapPawns?.FreeColonists?.FirstOrDefault(c => c != pawn) ?? pawn;
            Find.WindowStack.Add(new Dialog_Conversation(def, pawn, talker));
        }
    }
}
