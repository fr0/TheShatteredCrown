using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
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
        /// <summary>
        /// Taking the job in conversation IS accepting it: the player already
        /// said yes, and a second confirmation in the quest tab is not just
        /// redundant, it is unreachable. Vanilla refuses to accept a quest
        /// without a permanent colony ("Cannot accept this quest without a
        /// permanent colony"), and this mod's party never founds one - so an
        /// un-accepted quest never initiates and its site never spawns.
        /// </summary>
        public bool autoAccept = true;

        public override void Apply(DialogueContext context)
        {
            if (quest == null)
            {
                return;
            }
            float points = StorytellerUtility.DefaultThreatPointsNow(Find.World);
            Quest newQuest = QuestUtility.GenerateQuestAndMakeAvailable(quest, points);
            if (newQuest == null)
            {
                return;
            }
            if (sendLetter && !newQuest.hidden)
            {
                QuestUtility.SendLetterQuestAvailable(newQuest);
            }
            if (autoAccept && !newQuest.EverAccepted)
            {
                // The pawn doing the talking is the one who took the job.
                newQuest.Accept(context.interactor);
            }
        }
    }

    /// <summary>
    /// Shifts a named character's AFFINITY (BG3-style approval). With no npc
    /// set, targets the pawn being talked to. Fires the "X approves. (+5)"
    /// message; other named characters present can react to the same choice
    /// via additional effects with explicit npc.
    /// </summary>
    public class DialogueEffect_Affinity : DialogueEffect
    {
        public NamedNpcDef npc;
        public int amount;

        public override void Apply(DialogueContext context)
        {
            NamedNpcDef def = npc ?? DialogueStateManager.Current.NpcDefFor(context.npc);
            DialogueStateManager.Current.ChangeAffinity(def, amount);
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
            // SNAPSHOT FIRST. The signal can finish the quest it is aimed at,
            // and a finished quest hands out the next one - which mutates the
            // very list being walked ("Collection was modified", seen when
            // Madoc's recruit line completed the Road Fire contract).
            foreach (Quest q in new System.Collections.Generic.List<Quest>(Find.QuestManager.QuestsListForReading))
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
        // Optional: join this named NPC instead of the speaker (they must be
        // standing somewhere - a dead or absent companion is skipped).
        public NamedNpcDef npc;

        public override void Apply(DialogueContext context)
        {
            Pawn joiner = npc != null
                ? DialogueStateManager.Current.GetNamedNpcIfExists(npc)
                : context.npc;
            ApplyTo(joiner);
        }

        private static void ApplyTo(Pawn npc)
        {
            if (npc == null || npc.Dead || !npc.Spawned || npc.Faction == Faction.OfPlayer)
            {
                return;
            }
            npc.GetLord()?.Notify_PawnLost(npc, PawnLostCondition.ForcedByQuest);
            npc.guest?.SetGuestStatus(null);
            npc.SetFaction(Faction.OfPlayer);
            List<Pawn> animals = BringBondedAnimals(npc);
            string text = $"{npc.LabelShortCap} has thrown in with your company and will follow you from here on.";
            if (animals.Count > 0)
            {
                text += $"\n\n{animals.Select(a => a.LabelShortCap).ToCommaList(useAnd: true)} comes along, "
                    + $"bonded to {npc.LabelShort} and now yours to keep.";
            }
            Find.LetterStack.ReceiveLetter($"{npc.LabelShortCap} joins the party", text,
                LetterDefOf.PositiveEvent, npc);
        }

        /// <summary>
        /// A companion's bonded animal comes with them (Maewyn's Corvus): the
        /// bird is already tame to the grove's faction, so it hands over as a
        /// fully trained player pet mastered to its own bonded human rather
        /// than being left behind as someone else's property.
        /// </summary>
        private static List<Pawn> BringBondedAnimals(Pawn npc)
        {
            if (npc.relations == null || npc.MapHeld == null)
            {
                return new List<Pawn>();
            }
            // Snapshot first: SetFaction re-enters the relations tracker.
            List<Pawn> bonded = new List<Pawn>();
            foreach (DirectPawnRelation relation in npc.relations.DirectRelations)
            {
                Pawn other = relation.otherPawn;
                if (relation.def == PawnRelationDefOf.Bond && other != null && !other.Dead
                    && other.RaceProps.Animal && other.Faction != Faction.OfPlayer
                    && other.MapHeld == npc.MapHeld)
                {
                    bonded.Add(other);
                }
            }
            foreach (Pawn animal in bonded)
            {
                animal.GetLord()?.Notify_PawnLost(animal, PawnLostCondition.ForcedByQuest);
                animal.SetFaction(Faction.OfPlayer);
                TSC_CompanionPets.MakePlayerPet(animal, npc);
            }
            return bonded;
        }
    }

    /// <summary>
    /// Turning a story character's bonded animal into a player pet, shared by
    /// the recruit effect and the save-compat heal in TSC_StoryHubGuard.
    /// </summary>
    public static class TSC_CompanionPets
    {
        /// <summary>
        /// Trains the animal up and masters it to its bonded human. Obedience
        /// is a HARD requirement for mastering: vanilla's Master setter logs
        /// "Attempted to set master for non-obedient pawn" and refuses
        /// otherwise, so animals too dim to learn it (trainability None) are
        /// left tame but masterless rather than erroring every attempt.
        /// </summary>
        public static void MakePlayerPet(Pawn animal, Pawn owner)
        {
            if (animal?.training == null || owner == null)
            {
                return;
            }
            animal.training.Train(TrainableDefOf.Tameness, owner, complete: true);
            if (animal.training.CanBeTrained(TrainableDefOf.Obedience))
            {
                animal.training.Train(TrainableDefOf.Obedience, owner, complete: true);
            }
            if (animal.playerSettings == null || !animal.training.HasLearned(TrainableDefOf.Obedience))
            {
                return;
            }
            animal.playerSettings.Master = owner;
            animal.playerSettings.followDrafted = true;
            animal.playerSettings.followFieldwork = true;
        }
    }

    /// <summary>Sets (or clears) a persistent conversation flag.</summary>
    /// <summary>
    /// Opens the vanilla trade window with the NPC being talked to (they must
    /// be a standing trader - NamedNpcDef traderKind, e.g. Haldor). The
    /// talking colonist is the negotiator. DSL: trade().
    /// </summary>
    /// <summary>
    /// Hands the talking colonist an item (Haldor's commission, Mara's moss
    /// cutting): made from default stuff, optional quality, added to their
    /// inventory (falls to the ground beside them if full).
    /// DSL: give_item(Def[, count[, Quality]]).
    /// </summary>
    public class DialogueEffect_GiveThing : DialogueEffect
    {
        public ThingDef def;
        public int count = 1;
        public QualityCategory quality = QualityCategory.Normal;

        public override void Apply(DialogueContext context)
        {
            Pawn receiver = context.interactor;
            if (def == null || receiver == null || count <= 0)
            {
                return;
            }
            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            Thing thing = ThingMaker.MakeThing(def, stuff);
            thing.stackCount = count;
            thing.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
            bool taken = receiver.inventory != null && receiver.inventory.innerContainer.TryAdd(thing, false);
            if (!taken && receiver.SpawnedOrAnyParentSpawned)
            {
                GenPlace.TryPlaceThing(thing, receiver.PositionHeld, receiver.MapHeld, ThingPlaceMode.Near);
            }
            Messages.Message($"{receiver.LabelShortCap} receives {thing.LabelCap}.",
                receiver, MessageTypeDefOf.PositiveEvent, historical: false);
        }
    }

    /// <summary>
    /// The party's pooled goods on one map: every free colonist's inventory
    /// and carried stack, plus loose spawned items. What quest hand-overs
    /// (has_item / take_item) count and consume.
    /// </summary>
    public static class TSC_PartyItems
    {
        public static int Count(Map map, ThingDef def)
        {
            if (map == null || def == null)
            {
                return 0;
            }
            int total = 0;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.carryTracker?.CarriedThing?.def == def)
                {
                    total += pawn.carryTracker.CarriedThing.stackCount;
                }
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        if (thing.def == def)
                        {
                            total += thing.stackCount;
                        }
                    }
                }
            }
            foreach (Thing thing in map.listerThings.ThingsOfDef(def))
            {
                if (thing.Spawned)
                {
                    total += thing.stackCount;
                }
            }
            return total;
        }

        public static int Consume(Map map, ThingDef def, int count)
        {
            if (map == null || def == null || count <= 0)
            {
                return 0;
            }
            int remaining = count;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (remaining <= 0)
                {
                    break;
                }
                Thing carried = pawn.carryTracker?.CarriedThing;
                if (carried != null && carried.def == def)
                {
                    remaining -= Take(carried, remaining);
                }
                ThingOwner inventory = pawn.inventory?.innerContainer;
                if (inventory != null)
                {
                    for (int i = inventory.Count - 1; i >= 0 && remaining > 0; i--)
                    {
                        if (inventory[i].def == def)
                        {
                            remaining -= Take(inventory[i], remaining);
                        }
                    }
                }
            }
            List<Thing> loose = new List<Thing>(map.listerThings.ThingsOfDef(def));
            foreach (Thing thing in loose)
            {
                if (remaining <= 0)
                {
                    break;
                }
                if (thing.Spawned)
                {
                    remaining -= Take(thing, remaining);
                }
            }
            return count - remaining;
        }

        private static int Take(Thing thing, int wanted)
        {
            int taken = System.Math.Min(thing.stackCount, wanted);
            if (taken >= thing.stackCount)
            {
                thing.Destroy();
            }
            else
            {
                thing.stackCount -= taken;
            }
            return taken;
        }
    }

    /// <summary>
    /// Hands quest goods OVER: consumes items from the party's pooled goods
    /// on the talking colonist's map (Haldor's grave-iron leaves with
    /// Haldor). Gate the option with has_item so it can't fire short.
    /// DSL: take_item(Def[, count]).
    /// </summary>
    public class DialogueEffect_TakeThing : DialogueEffect
    {
        public ThingDef def;
        public int count = 1;

        public override void Apply(DialogueContext context)
        {
            Map map = context.interactor?.MapHeld;
            if (def == null || map == null)
            {
                return;
            }
            int taken = TSC_PartyItems.Consume(map, def, count);
            if (taken > 0 && context.interactor != null)
            {
                Messages.Message($"{context.interactor.LabelShortCap} hands over {def.label} x{taken}.",
                    context.interactor, MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }
    }

    /// <summary>
    /// Sends a named character walking OFF their current map - "they left"
    /// (the Root children heading for the hills when Hessa's quest begins,
    /// or marching home when found). Visible: they leave their lord and jog
    /// for the map edge under vanilla's exit lord, worldifying on arrival;
    /// they respawn wherever the story next places them. Factionless pawns
    /// fall back to an instant despawn. DSL: depart(Npc).
    /// </summary>
    public class DialogueEffect_DespawnNpc : DialogueEffect
    {
        public NamedNpcDef npc;

        public override void Apply(DialogueContext context)
        {
            Pawn pawn = npc != null ? DialogueStateManager.Current.GetNamedNpcIfExists(npc) : null;
            if (pawn == null || pawn.Dead || !pawn.Spawned)
            {
                return;
            }
            pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
            if (pawn.Faction != null)
            {
                LordMaker.MakeNewLord(pawn.Faction,
                    new LordJob_ExitMapBest(LocomotionUrgency.Jog), pawn.Map, Gen.YieldSingle(pawn));
                return;
            }
            pawn.DeSpawn();
            if (!Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
        }
    }

    /// <summary>Wakes the NPC being talked to (the "shake them awake" option in asleep nodes). DSL: wake().</summary>
    public class DialogueEffect_Wake : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            if (context.npc != null && !context.npc.Awake())
            {
                RestUtility.WakeUp(context.npc);
            }
        }
    }

    public class DialogueEffect_OpenTrade : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Pawn trader = context.npc;
            Pawn negotiator = context.interactor;
            if (trader == null || negotiator == null)
            {
                return;
            }
            // Self-heal at the counter: attach the tracker and roll stock if
            // this merchant somehow lacks them (generated pre-feature, sold
            // bare, save/load quirk...). The NamedNpcDef is found via the
            // pawn's kind - named NPCs have unique kinds.
            NamedNpcDef npcDef = null;
            foreach (NamedNpcDef candidate in DefDatabase<NamedNpcDef>.AllDefsListForReading)
            {
                if (candidate.kind == trader.kindDef)
                {
                    npcDef = candidate;
                    break;
                }
            }
            if (npcDef?.traderKind != null)
            {
                GenStep_TSC_Village.EnsureTrader(trader, npcDef, trader.MapHeld);
            }
            // Custom shopkeeper adapter: vanilla's Pawn_TraderTracker assumes
            // trade caravans and reports villagers as having no goods.
            if (trader.trader == null || trader.trader.traderKind == null
                || trader.inventory == null || !trader.inventory.innerContainer.Any)
            {
                Messages.Message($"{trader.LabelShortCap} has nothing to trade right now.",
                    trader, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            // The trade UI computes the negotiator's TradePriceImprovement
            // every frame; a social-incapable pawn has that stat DISABLED and
            // spams a consistency error. Same gate vanilla's trade option uses.
            if (StatDefOf.TradePriceImprovement.Worker.IsDisabledFor(negotiator))
            {
                Messages.Message($"{negotiator.LabelShortCap} is incapable of negotiating trades (social disabled). Talk to {trader.LabelShortCap} with another colonist to trade.",
                    negotiator, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Find.WindowStack.Add(new Dialog_Trade(negotiator, new TSC_ShopTrader(trader)));
        }
    }

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
            if (learner == null || classDef == null)
            {
                return;
            }
            if (teachNpc)
            {
                // An NPC picking up a class is a story fact: grant it outright.
                TSC_ProgressionManager.Current.LearnClass(learner, classDef);
                return;
            }
            // A mentor teaching the party opens the path rather than walking
            // it for you: the class becomes choosable at level-up, same as a
            // studied manual, and taking it still costs a class level.
            TSC_ProgressionManager.Current.UnlockClass(classDef, learner,
                $"The {classDef.label} class is now available at level-up.");
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
    /// <summary>
    /// Party XP from a conversation. ONE-SHOT BY DEFAULT.
    ///
    /// Dialogue hubs are re-enterable by design - you can ask a companion to
    /// sing the same song every night - so an unguarded XP grant on a hub
    /// branch is an XP faucet. Rather than trusting every future line to
    /// remember a flag, the compiler stamps each grant with a unique key
    /// (dialogue/node/option) and it pays exactly once per save.
    ///
    /// An author who WANTS a repeatable grant writes grant_xp(25, repeat),
    /// which emits no key. That is the rare case, and now it is the one that
    /// has to be spelled out.
    /// </summary>
    public class DialogueEffect_GrantXp : DialogueEffect
    {
        public int xp;

        /// <summary>Auto-generated by the compiler. Empty = deliberately repeatable.</summary>
        [NoTranslate]
        public string onceKey;

        public override void Apply(DialogueContext context)
        {
            if (xp <= 0)
            {
                return;
            }
            if (!onceKey.NullOrEmpty())
            {
                if (DialogueStateManager.Current.IsSet(onceKey))
                {
                    return;
                }
                DialogueStateManager.Current.Set(onceKey);
            }
            TSC_ProgressionManager.Current.GrantXpToParty(xp, "conversation");
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

    /// <summary>
    /// Send a signal into a live quest generated from a given script. Shared
    /// by the dialogue effect and by map components that need to report an
    /// event a quest is waiting on (a parley crew being beaten).
    /// </summary>
    public static class TSC_QuestSignals
    {
        public static void Send(string questDefName, string signal)
        {
            QuestScriptDef script = DefDatabase<QuestScriptDef>.GetNamedSilentFail(questDefName);
            if (script == null || signal.NullOrEmpty())
            {
                return;
            }
            // Snapshot: finishing a quest can grant the next one, which
            // mutates the list being walked.
            foreach (Quest quest in new System.Collections.Generic.List<Quest>(Find.QuestManager.QuestsListForReading))
            {
                if (quest.root != script || quest.State != QuestState.Ongoing)
                {
                    continue;
                }
                string initiate = quest.InitiateSignal;
                int dot = initiate.LastIndexOf('.');
                string prefix = dot >= 0 ? initiate.Substring(0, dot) : initiate;
                QuestUtility.SendQuestTargetSignals(new System.Collections.Generic.List<string> { prefix }, signal);
            }
        }

        /// <summary>
        /// Save repair for a quest whose script has been re-pointed at a
        /// different signal. Quest PARTS are baked when the quest is granted
        /// and then live in the save file, so fixing the script only helps
        /// quests granted afterwards: a quest already in flight keeps
        /// listening to the old signal for as long as it runs. This moves
        /// those listeners across. Returns how many were moved.
        /// </summary>
        public static int Retarget(string questDefName, string oldSuffix, string newSuffix)
        {
            QuestScriptDef script = DefDatabase<QuestScriptDef>.GetNamedSilentFail(questDefName);
            if (script == null || oldSuffix.NullOrEmpty() || newSuffix.NullOrEmpty())
            {
                return 0;
            }
            int moved = 0;
            foreach (Quest quest in new System.Collections.Generic.List<Quest>(Find.QuestManager.QuestsListForReading))
            {
                if (quest.root != script || quest.State != QuestState.Ongoing)
                {
                    continue;
                }
                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is QuestPart_Pass pass && pass.inSignal != null
                        && pass.inSignal.EndsWith(oldSuffix))
                    {
                        pass.inSignal = pass.inSignal.Substring(0, pass.inSignal.Length - oldSuffix.Length)
                            + newSuffix;
                        moved++;
                    }
                }
            }
            return moved;
        }
    }
    /// <summary>
    /// DSL effect npc_hediff(DefName): puts a hediff on the character being
    /// talked to.
    ///
    /// For conversations that change somebody permanently rather than
    /// changing the world - Madoc walking away from the still fire either
    /// released or kindled, and being a different caster afterward. The
    /// health tab is where a player looks to find out why, so that is where
    /// the record goes.
    /// </summary>
    public class DialogueEffect_TSC_NpcHediff : DialogueEffect
    {
        public HediffDef hediff;

        public override void Apply(DialogueContext context)
        {
            Pawn npc = context.npc;
            if (hediff == null || npc?.health?.hediffSet == null || npc.Dead)
            {
                return;
            }
            if (!npc.health.hediffSet.HasHediff(hediff))
            {
                npc.health.AddHediff(hediff);
            }
        }
    }

}
