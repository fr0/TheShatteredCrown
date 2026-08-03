using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// Parting ways with a story companion.
    ///
    /// Until now the only way out of the party was scripted - Wick's crypt
    /// finale, or the Wayfarers retiring at the city - so a companion the
    /// player was finished with rode along forever. A party RPG wants the
    /// door to open from the inside: a companion you cannot dismiss is a
    /// companion whose loyalty costs you nothing.
    ///
    /// One shared scene for every companion ({NPC} carries the name), with
    /// the guards in code rather than in the dialogue, because "can I do
    /// this here" is a question about the world and not about the
    /// conversation.
    /// </summary>
    public class FloatMenuOptionProvider_TSC_PartWays : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            if (!TSC_RpgMode.Active || clickedPawn == null || clickedPawn.Dead)
            {
                return null;
            }
            NamedNpcDef companion = TSC_PartWays.CompanionDefOf(clickedPawn);
            if (companion == null || clickedPawn.Faction != Faction.OfPlayer)
            {
                return null;
            }
            Pawn talker = context.FirstSelectedPawn;
            if (talker == null || talker == clickedPawn || !talker.IsColonistPlayerControlled)
            {
                return null;
            }
            string label = $"Part ways with {clickedPawn.LabelShortCap}";
            if (!TSC_PartWays.CanPartHere(clickedPawn, out string blocked))
            {
                return new FloatMenuOption($"{label} ({blocked})", null);
            }
            if (!talker.CanReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
            {
                return new FloatMenuOption($"{label} (cannot reach them)", null);
            }
            FloatMenuOption option = new FloatMenuOption(label, delegate
            {
                // Walk over and say it to their face, like every other
                // conversation in the mod.
                JobDef job = DefDatabase<JobDef>.GetNamedSilentFail("TSC_PartWays");
                if (job != null)
                {
                    talker.jobs.TryTakeOrderedJob(JobMaker.MakeJob(job, clickedPawn), JobTag.Misc);
                }
            });
            return FloatMenuUtility.DecoratePrioritizedTask(option, talker, clickedPawn);
        }
    }

    public class JobDriver_TSC_PartWays : JobDriver
    {
        private Pawn Companion => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil talk = ToilMaker.MakeToil("TSC_PartWays");
            talk.initAction = delegate
            {
                Pawn companion = Companion;
                DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_PartWays");
                if (companion == null || def == null
                    || !TSC_PartWays.CanPartHere(companion, out string _))
                {
                    return;
                }
                Find.WindowStack.Add(new Dialog_Conversation(def, companion, pawn));
            };
            talk.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return talk;
        }
    }

    public static class TSC_PartWays
    {
        /// <summary>
        /// The NamedNpcDef this pawn is, if they are a story companion at
        /// all. Delegates to NpcDefFor: one pass over the (small) name-to-
        /// pawn registry, instead of the old all-defs-times-lookup walk -
        /// this runs per party pawn in the carried-gear sweep.
        /// </summary>
        public static NamedNpcDef CompanionDefOf(Pawn pawn)
        {
            return pawn == null ? null : DialogueStateManager.Current?.NpcDefFor(pawn);
        }

        /// <summary>
        /// Where a parting is decent. Abandoning somebody in a barrow, or
        /// mid-fight, or while they are carrying a piece of the crown, is
        /// not a decision the game should quietly allow.
        /// </summary>
        public static bool CanPartHere(Pawn companion, out string blocked)
        {
            Map map = companion.MapHeld;
            if (map == null || !map.Tile.Valid)
            {
                blocked = "not down here";
                return false;
            }
            if (GenHostility.AnyHostileActiveThreatToPlayer(map))
            {
                blocked = "not in the middle of a fight";
                return false;
            }
            if (companion.Downed)
            {
                blocked = "they are down";
                return false;
            }
            if (CarriesShard(companion))
            {
                blocked = "they carry a piece of the crown";
                return false;
            }
            blocked = null;
            return true;
        }

        private static bool CarriesShard(Pawn pawn)
        {
            if (pawn.inventory?.innerContainer == null)
            {
                return false;
            }
            foreach (Thing thing in pawn.inventory.innerContainer)
            {
                if (TSC_Shards.IsShard(thing.def))
                {
                    return true;
                }
            }
            return TSC_Shards.IsShard(pawn.carryTracker?.CarriedThing?.def);
        }

        /// <summary>
        /// The parting itself. They keep what they are wearing and carrying
        /// (it was theirs), leave the player faction, and go back to the
        /// world - not deleted, so a later act can still refer to them, and
        /// so a story that wants them back has somebody to bring back.
        /// </summary>
        /// <summary>Flag prefix for a companion who has announced they are going but cannot go yet.</summary>
        public static string PendingKey(NamedNpcDef def) => "TSC_PartWaysPending_" + def.defName;

        /// <summary>
        /// Called every sweep from MapComponent_TSC_CarriedGear: finishes a
        /// departure that was announced somewhere it could not happen.
        ///
        /// The player-initiated dismissal is gated on CanPartHere before the
        /// float menu even offers it, but a companion who asks to leave of
        /// their own accord can do it anywhere - including three floors
        /// underground with a fight upstairs. They say their piece, and then
        /// they wait for ground they can actually walk off.
        /// </summary>
        public static void CheckPending(Pawn companion)
        {
            NamedNpcDef def = CompanionDefOf(companion);
            if (def == null || companion.Faction != Faction.OfPlayer)
            {
                return;
            }
            if (!DialogueStateManager.Current.IsSet(PendingKey(def)))
            {
                return;
            }
            if (!CanPartHere(companion, out string _))
            {
                return;
            }
            DialogueStateManager.Current.Clear(PendingKey(def));
            Depart(companion, deferrable: false);
        }

        public static void Depart(Pawn companion, bool deferrable = true)
        {
            if (companion == null)
            {
                return;
            }
            NamedNpcDef def = CompanionDefOf(companion);
            // Only a named companion can defer: the pending flag is keyed by
            // their def, and a promise nothing can record is a promise the
            // sweep would never keep. Anyone else just leaves now.
            if (deferrable && def != null && !CanPartHere(companion, out string blocked))
            {
                DialogueStateManager.Current.Set(PendingKey(def));
                Messages.Message(
                    $"{companion.LabelShortCap} means to go, and will, as soon as the company is somewhere they can ({blocked}).",
                    companion, MessageTypeDefOf.NeutralEvent, historical: false);
                return;
            }
            if (def != null)
            {
                DialogueStateManager.Current.Set("TSC_Parted_" + def.defName);
                // A companion who walks is a companion whose personal errand
                // walks with them (TSC_WrenLost and kin).
                TSC_CompanionLoss.Notify_CompanionGone(def);
            }
            Faction villagers = GenStep_TSC_Village.VillagerFaction();
            companion.GetLord()?.Notify_PawnLost(companion, PawnLostCondition.ExitedMap);
            if (villagers != null)
            {
                companion.SetFaction(villagers);
            }
            Map map = companion.MapHeld;
            if (companion.Spawned && map != null && villagers != null)
            {
                // Walk out rather than vanish: the company watches them go.
                LordMaker.MakeNewLord(villagers,
                    new LordJob_ExitMapBest(LocomotionUrgency.Walk),
                    map, new List<Pawn> { companion });
            }
            else if (companion.Spawned)
            {
                companion.DeSpawn();
            }
            Find.LetterStack.ReceiveLetter(
                $"{companion.LabelShortCap} goes their own way",
                $"{companion.LabelShortCap} takes their share, shoulders their pack, and walks. "
                + "The company is smaller than it was this morning.\n\n"
                + "They keep what was theirs. Whether the road brings them back is the road's business.",
                LetterDefOf.NeutralEvent, companion.Spawned ? (LookTargets)companion : null);
        }
    }

    /// <summary>DSL effect part_ways(): the companion being spoken to leaves the company.</summary>
    public class DialogueEffect_TSC_PartWays : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            TSC_PartWays.Depart(context.npc);
        }
    }

    /// <summary>
    /// True while the companion being talked to has NOT already announced a
    /// departure that is waiting for walkable ground (TSC_PartWays.PendingKey).
    ///
    /// The ask-to-leave initiation is recurring, and without this it would
    /// re-fire on a companion who already said their goodbye and is just
    /// waiting for the pocket map to end - asking to leave twice while
    /// already leaving.
    /// </summary>
    public class DialogueCondition_NotPendingDeparture : DialogueCondition
    {
        public override bool Met(DialogueContext context)
        {
            NamedNpcDef def = TSC_PartWays.CompanionDefOf(context.npc);
            return def == null
                || DialogueStateManager.Current == null
                || !DialogueStateManager.Current.IsSet(TSC_PartWays.PendingKey(def));
        }
    }
}
