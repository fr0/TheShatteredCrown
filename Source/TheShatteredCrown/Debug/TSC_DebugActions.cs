using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

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

        /// <summary>
        /// "Why can't I reform the caravan?" - vanilla blocks reform while ANY
        /// hostile counts as an active threat, INCLUDING dormant/sleeping and
        /// fogged ones the player has never seen, plus aggro-mental-state
        /// colonists. This names every blocker and jumps to the first.
        /// </summary>
        [DebugAction("The Shattered Crown", "List caravan reform blockers", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ListReformBlockers()
        {
            Map map = Find.CurrentMap;
            int found = 0;
            Thing first = null;
            foreach (IAttackTarget target in map.attackTargetsCache.TargetsHostileToColony)
            {
                Thing thing = target.Thing;
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }
                bool dormant = thing.TryGetComp<CompCanBeDormant>()?.Awake == false;
                bool active = GenHostility.IsActiveThreatTo(target, Faction.OfPlayer);
                if (!active && !dormant)
                {
                    continue;
                }
                found++;
                first = first ?? thing;
                Log.Message($"[TSC] Reform blocker: {thing.LabelCap} at {thing.Position} "
                    + $"(fogged={thing.Position.Fogged(map)}, dormant={dormant}, activeThreat={active})");
            }
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.MentalState != null && colonist.MentalState.def.category == MentalStateCategory.Aggro)
                {
                    found++;
                    first = first ?? colonist;
                    Log.Message($"[TSC] Reform blocker: {colonist.LabelShortCap} is in an aggro mental state ({colonist.MentalState.def.defName}).");
                }
            }
            if (found == 0)
            {
                Messages.Message("No reform blockers on this map. If reforming still fails, the blocker is off-map machinery - send the log.",
                    RimWorld.MessageTypeDefOf.NeutralEvent, historical: false);
                return;
            }
            Messages.Message($"{found} reform blocker(s) found - names and positions are in the dev log.",
                first, RimWorld.MessageTypeDefOf.NeutralEvent, historical: false);
            CameraJumper.TryJump(first);
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

        [DebugAction("The Shattered Crown", "Add level in class...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AddClassLevelTool()
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
                            TSC_ProgressionManager.Current.DebugAddClassLevel(pawn, local);
                            break;
                        }
                    }
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        /// <summary>
        /// One entry per contract template, generated exactly the way the
        /// board generates them - same points, same letter - so a spawned
        /// contract IS the real thing and not a lookalike. Playtesting a
        /// specific template used to mean rerolling the board until it came
        /// up, which for twelve templates is a long evening.
        /// </summary>
        [DebugAction("The Shattered Crown", "Spawn contract...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnContract()
        {
            if (!RpgGate())
            {
                return;
            }
            System.Collections.Generic.List<DebugMenuOption> options =
                new System.Collections.Generic.List<DebugMenuOption>();
            System.Collections.Generic.List<QuestScriptDef> contracts =
                new System.Collections.Generic.List<QuestScriptDef>();
            foreach (QuestScriptDef def in DefDatabase<QuestScriptDef>.AllDefsListForReading)
            {
                if (TSC_ContractManager.IsContract(def))
                {
                    contracts.Add(def);
                }
            }
            contracts.SortBy(d => d.defName);
            foreach (QuestScriptDef def in contracts)
            {
                QuestScriptDef local = def;
                options.Add(new DebugMenuOption(local.defName.Substring("TSC_Contract_".Length),
                    DebugMenuOptionMode.Action, delegate
                    {
                        Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(
                            local, TSC_ContractManager.ContractPoints());
                        if (quest == null)
                        {
                            Messages.Message($"{local.defName}: generation failed (its TestRun said no - "
                                + "usually no valid site tile or destination from here).",
                                MessageTypeDefOf.RejectInput, historical: false);
                            return;
                        }
                        if (!quest.hidden)
                        {
                            QuestUtility.SendLetterQuestAvailable(quest);
                        }
                    }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("The Shattered Crown", "Homeward: report status", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void HomewardReport()
        {
            GameComponent_TSC_Homeward homeward = Verse.Current.Game?.GetComponent<GameComponent_TSC_Homeward>();
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            report.AppendLine($"component = {(homeward != null ? "alive" : "MISSING")}, "
                + $"rpgMode = {TSC_RpgMode.Active}");
            homeward?.Sweep(report);
            Log.Message("[The Shattered Crown] Homeward status:" + System.Environment.NewLine + report);
            Messages.Message("Homeward status written to the log.", MessageTypeDefOf.NeutralEvent, historical: false);
        }

        [DebugAction("The Shattered Crown", "Homeward: sweep now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void HomewardSweep()
        {
            Verse.Current.Game?.GetComponent<GameComponent_TSC_Homeward>()?.Sweep(null);
        }

        /// <summary>
        /// Every link in the kill-mood chain, printed. The setting, the
        /// live def value the setting is supposed to patch, whether the
        /// thought can be gained at all, and what a real grant does to a
        /// real colonist - so a report of "still not working" can name the
        /// broken link instead of the symptom.
        /// </summary>
        [DebugAction("The Shattered Crown", "Mood: test kill thought", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TestKillMood()
        {
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            float setting = TSC_Mod.Settings?.killMoodBonus ?? -1f;
            report.AppendLine($"setting killMoodBonus = {setting} (hours {TSC_Mod.Settings?.killMoodHours})");
            ThoughtDef def = TSC_MoodOptions.Bloodied;
            report.AppendLine($"def TSC_Thought_Bloodied = {(def == null ? "MISSING" : "found")}");
            if (def != null)
            {
                TSC_MoodOptions.ApplySettings();
                report.AppendLine($"  after ApplySettings: baseMoodEffect = {def.stages[0].baseMoodEffect}, "
                    + $"durationDays = {def.durationDays}, stackLimit = {def.stackLimit}");
            }
            Pawn subject = null;
            foreach (Pawn pawn in Find.CurrentMap.mapPawns.FreeColonistsSpawned)
            {
                subject = pawn;
                break;
            }
            if (subject == null || def == null)
            {
                report.AppendLine("no colonist on this map to test with");
            }
            else
            {
                report.AppendLine($"subject = {subject.LabelShort}, "
                    + $"CanGetThought = {ThoughtUtility.CanGetThought(subject, def, checkIfNullified: true)}");
                int before = CountMemories(subject, def);
                TSC_MoodOptions.NoteKill(subject, subject.Map);
                int after = CountMemories(subject, def);
                report.AppendLine($"NoteKill: memories {before} -> {after}");
                Thought_Memory got = subject.needs?.mood?.thoughts?.memories?.Memories
                    ?.Find(m => m.def == def);
                report.AppendLine(got == null
                    ? "  no memory present after the grant"
                    : $"  memory present, MoodOffset = {got.MoodOffset()}");
            }
            Log.Message("[The Shattered Crown] Kill-mood test:" + System.Environment.NewLine + report);
            Messages.Message("Kill-mood test written to the log.", MessageTypeDefOf.NeutralEvent, historical: false);
        }

        private static int CountMemories(Pawn pawn, ThoughtDef def)
        {
            int n = 0;
            List<Thought_Memory> memories = pawn.needs?.mood?.thoughts?.memories?.Memories;
            for (int i = 0; memories != null && i < memories.Count; i++)
            {
                if (memories[i].def == def)
                {
                    n++;
                }
            }
            return n;
        }

        [DebugAction("The Shattered Crown", "Show act title card", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ShowTitleCard()
        {
            TSC_TitleCardManager.Show("Act 1", "Distilled Memory");
        }

        [DebugAction("The Shattered Crown", "Spawn bandit hexer", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnBanditHexer()
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_BanditHexer");
            RimWorld.Faction faction = Verse.Find.FactionManager.RandomEnemyFaction(allowHidden: false, allowDefeated: false, allowNonHumanlike: false);
            if (kind == null || faction == null)
            {
                Messages.Message("No hexer kind or enemy faction available.", RimWorld.MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Pawn hexer = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, mustBeCapableOfViolence: true));
            GenSpawn.Spawn(hexer, UI.MouseCell(), Verse.Find.CurrentMap);
        }

        [DebugAction("The Shattered Crown", "Fire initiated talk (pawn)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FireInitiatedTalk(Pawn pawn)
        {
            DialogueExtension ext = pawn.kindDef?.GetModExtension<DialogueExtension>();
            if (ext == null || ext.initiations.Count == 0)
            {
                Messages.Message($"{pawn.LabelShortCap} ({pawn.kindDef?.defName}) has no initiations. If this is a companion, their pawn kind may have drifted; it self-heals hourly.",
                    RimWorld.MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            // Skips the MTB roll and the once-per-save guard; reports (instead
            // of skipping) unmet conditions, so gating problems are visible.
            // A PICKER, not first-in-list: most companions lead with the
            // part-ways ask, so "first" fired the leaving talk at whoever the
            // tester actually wanted the quest talk from.
            Pawn talker = pawn.Map?.mapPawns?.FreeColonists?.FirstOrDefault(c => c != pawn);
            if (talker == null)
            {
                Messages.Message("No other free colonist on the map to talk to.", RimWorld.MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (DialogueInitiation init in ext.initiations)
            {
                DialogueInitiation localInit = init;
                options.Add(new DebugMenuOption(localInit.dialogue?.defName ?? "(no dialogue)", DebugMenuOptionMode.Action, delegate
                {
                    DialogueContext context = new DialogueContext(pawn, talker);
                    foreach (DialogueCondition condition in localInit.conditions)
                    {
                        if (!condition.Met(context))
                        {
                            Messages.Message($"{localInit.dialogue?.defName}: condition {condition.GetType().Name} not met (firing anyway).",
                                RimWorld.MessageTypeDefOf.NeutralEvent, historical: false);
                        }
                    }
                    Find.WindowStack.Add(new Dialog_Conversation(localInit.dialogue, pawn, talker));
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("The Shattered Crown", "Fire caravan camp talk...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void FireCaravanCampTalk()
        {
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (RimWorld.Planet.Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled)
                {
                    continue;
                }
                foreach (Pawn npc in caravan.PawnsListForReading)
                {
                    DialogueExtension ext = npc.kindDef?.GetModExtension<DialogueExtension>();
                    if (ext == null || ext.initiations.Count == 0)
                    {
                        continue;
                    }
                    Pawn talker = caravan.PawnsListForReading.FirstOrDefault(p => p != npc && p.IsFreeColonist && !p.Dead && !p.Downed);
                    if (talker == null)
                    {
                        continue;
                    }
                    foreach (DialogueInitiation init in ext.initiations)
                    {
                        DialogueInitiation localInit = init;
                        Pawn localNpc = npc;
                        Pawn localTalker = talker;
                        options.Add(new DebugMenuOption($"{npc.LabelShortCap}: {init.dialogue?.defName}", DebugMenuOptionMode.Action, delegate
                        {
                            // Skips the MTB roll, the once guard, and NightResting;
                            // reports unmet conditions instead of skipping.
                            DialogueContext context = new DialogueContext(localNpc, localTalker);
                            foreach (DialogueCondition condition in localInit.conditions)
                            {
                                if (!condition.Met(context))
                                {
                                    Messages.Message($"{localInit.dialogue?.defName}: condition {condition.GetType().Name} not met (firing anyway).",
                                        RimWorld.MessageTypeDefOf.NeutralEvent, historical: false);
                                }
                            }
                            Find.WindowStack.Add(new Dialog_Conversation(localInit.dialogue, localNpc, localTalker));
                        }));
                    }
                }
            }
            if (options.Count == 0)
            {
                Messages.Message("No caravan pawns with initiations found.", RimWorld.MessageTypeDefOf.RejectInput, historical: false);
                return;
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
