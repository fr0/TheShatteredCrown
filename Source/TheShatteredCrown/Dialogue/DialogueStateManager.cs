using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Persistent, save-safe conversation state: a global set of string flags.
    /// Auto-instantiated by the game (WorldComponent subclasses are discovered
    /// automatically) and serialized with the save.
    ///
    /// Opening a conversation auto-sets the flag "TSC_Talked_&lt;dialogueDefName&gt;",
    /// so "have we met" checks need no explicit authoring.
    /// </summary>
    public class DialogueStateManager : WorldComponent
    {
        private HashSet<string> flags = new HashSet<string>();
        private Dictionary<string, Pawn> namedNpcs = new Dictionary<string, Pawn>();
        private HashSet<string> firedInitiations = new HashSet<string>();
        private Pawn protagonist;

        /// <summary>Transient: pawns currently walking over to start a conversation (not saved; retries after load).</summary>
        private static readonly Dictionary<Pawn, DialogueDef> pendingInitiations = new Dictionary<Pawn, DialogueDef>();

        private const int CheckIntervalTicks = 2500; // hourly

        public DialogueStateManager(World world) : base(world)
        {
        }

        public static DialogueStateManager Current => Find.World.GetComponent<DialogueStateManager>();

        public bool IsSet(string flag)
        {
            return !flag.NullOrEmpty() && flags.Contains(flag);
        }

        public void Set(string flag)
        {
            if (!flag.NullOrEmpty())
            {
                flags.Add(flag);
            }
        }

        public void Clear(string flag)
        {
            if (!flag.NullOrEmpty())
            {
                flags.Remove(flag);
            }
        }

        /// <summary>
        /// The one true pawn for this story character. Generated (with the def's
        /// fixed name/gender) on first request and kept alive as a world pawn;
        /// every later request returns the same pawn. If they died, a fresh pawn
        /// is generated as a replacement.
        /// </summary>
        public Pawn GetOrGenerateNamedNpc(NamedNpcDef def, Faction factionIfNew)
        {
            if (namedNpcs.TryGetValue(def.defName, out Pawn existing) && existing != null && !existing.Dead && !existing.Destroyed)
            {
                TSC_ProgressionManager.Current.SeedClass(existing, def.classDef);
                return existing;
            }
            PawnGenerationRequest request = new PawnGenerationRequest(def.kind, factionIfNew, PawnGenerationContext.NonPlayer)
            {
                ForceGenerateNewPawn = true,
                AllowPregnant = false,
            };
            if (def.gender != Gender.None)
            {
                request.FixedGender = def.gender;
            }
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            pawn.Name = def.MakeName();
            if (def.forcedWeapon != null && pawn.equipment != null)
            {
                pawn.equipment.DestroyAllEquipment();
                ThingDef stuff = def.forcedWeapon.MadeFromStuff
                    ? (def.forcedWeaponStuff ?? GenStuff.DefaultStuffFor(def.forcedWeapon))
                    : null;
                ThingWithComps weapon = (ThingWithComps)ThingMaker.MakeThing(def.forcedWeapon, stuff);
                weapon.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
                pawn.equipment.AddEquipment(weapon);
            }
            if (!def.forcedApparel.NullOrEmpty() && pawn.apparel != null)
            {
                foreach (TSC_ForcedApparel entry in def.forcedApparel)
                {
                    if (entry?.def == null)
                    {
                        continue;
                    }
                    ThingDef apparelStuff = entry.def.MadeFromStuff
                        ? (entry.stuff ?? GenStuff.DefaultStuffFor(entry.def))
                        : null;
                    Apparel apparel = (Apparel)ThingMaker.MakeThing(entry.def, apparelStuff);
                    apparel.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
                    pawn.apparel.Wear(apparel, dropReplacedApparel: false);
                }
            }
            TSC_ProgressionManager.Current.SeedClass(pawn, def.classDef);
            if (!pawn.Spawned && !Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            namedNpcs[def.defName] = pawn;
            return pawn;
        }

        /// <summary>The character's pawn if they have ever been generated, else null.</summary>
        public Pawn GetNamedNpcIfExists(NamedNpcDef def)
        {
            return def != null && namedNpcs.TryGetValue(def.defName, out Pawn pawn) ? pawn : null;
        }

        /// <summary>Reverse lookup: the character def whose pawn this is, else null.</summary>
        public NamedNpcDef NpcDefFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }
            foreach (KeyValuePair<string, Pawn> pair in namedNpcs)
            {
                if (pair.Value == pawn)
                {
                    return DefDatabase<NamedNpcDef>.GetNamedSilentFail(pair.Key);
                }
            }
            return null;
        }

        /// <summary>
        /// The story's main character: captured lazily as the first free colonist
        /// seen (in the Lone Adventurer scenario, the starting pawn). Null once
        /// dead - initiated dialogue then falls back to whoever is left.
        /// </summary>
        public Pawn Protagonist
        {
            get
            {
                if (protagonist == null || protagonist.Destroyed)
                {
                    protagonist = null;
                    foreach (Map map in Find.Maps)
                    {
                        List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
                        if (colonists.Count > 0)
                        {
                            protagonist = colonists[0];
                            break;
                        }
                    }
                }
                return protagonist != null && !protagonist.Dead ? protagonist : null;
            }
        }

        // ---------------------------------------------------------------- initiated dialogue

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }
            // Wherever the party is: home maps, quest sites, encampments. This
            // scenario is a traveling one, so home-only would almost never fire.
            // Threats pause it.
            foreach (Map map in Find.Maps)
            {
                if (map.dangerWatcher.DangerRating != StoryDanger.None)
                {
                    continue;
                }
                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    TryStartInitiation(colonists[i], map);
                }
            }
            // And the roads themselves: fire during caravan night rest, where
            // most of this scenario's downtime actually happens.
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.IsPlayerControlled && caravan.NightResting)
                {
                    TryStartCaravanInitiation(caravan);
                }
            }
        }

        /// <summary>
        /// Caravan variant: no map to walk across, so a firing initiation simply
        /// opens the conversation at the night fire.
        /// </summary>
        private void TryStartCaravanInitiation(Caravan caravan)
        {
            List<Pawn> pawns = caravan.PawnsListForReading;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn npc = pawns[i];
                DialogueExtension ext = npc.kindDef?.GetModExtension<DialogueExtension>();
                if (ext == null || ext.initiations.Count == 0 || npc.Dead || npc.Downed)
                {
                    continue;
                }
                Pawn target = null;
                Pawn hero = Protagonist;
                if (hero != null && hero != npc && !hero.Dead && !hero.Downed && hero.GetCaravan() == caravan)
                {
                    target = hero;
                }
                else
                {
                    for (int j = 0; j < pawns.Count; j++)
                    {
                        Pawn p = pawns[j];
                        if (p != npc && p.IsFreeColonist && !p.Dead && !p.Downed)
                        {
                            target = p;
                            break;
                        }
                    }
                }
                if (target == null)
                {
                    continue;
                }
                foreach (DialogueInitiation init in ext.initiations)
                {
                    if (init.dialogue == null || (init.once && firedInitiations.Contains(init.Key(npc.kindDef))))
                    {
                        continue;
                    }
                    if (!Rand.MTBEventOccurs(init.mtbDays, GenDate.TicksPerDay, CheckIntervalTicks))
                    {
                        continue;
                    }
                    DialogueContext context = new DialogueContext(npc, target);
                    bool met = true;
                    foreach (DialogueCondition condition in init.conditions)
                    {
                        if (!condition.Met(context))
                        {
                            met = false;
                            break;
                        }
                    }
                    if (!met)
                    {
                        continue;
                    }
                    if (init.once)
                    {
                        firedInitiations.Add(init.Key(npc.kindDef));
                    }
                    Find.WindowStack.Add(new Dialog_Conversation(init.dialogue, npc, target));
                    return; // one fireside talk per rest check
                }
            }
        }

        private void TryStartInitiation(Pawn npc, Map map)
        {
            DialogueExtension ext = npc.kindDef?.GetModExtension<DialogueExtension>();
            if (ext == null || ext.initiations.Count == 0 || pendingInitiations.ContainsKey(npc))
            {
                return;
            }
            if (npc.Downed || npc.Drafted || npc.InMentalState || !npc.Awake())
            {
                return;
            }
            Pawn target = FindInitiationTarget(npc, map);
            if (target == null)
            {
                return;
            }
            foreach (DialogueInitiation init in ext.initiations)
            {
                if (init.dialogue == null || (init.once && firedInitiations.Contains(init.Key(npc.kindDef))))
                {
                    continue;
                }
                if (!Rand.MTBEventOccurs(init.mtbDays, GenDate.TicksPerDay, CheckIntervalTicks))
                {
                    continue;
                }
                DialogueContext context = new DialogueContext(npc, target);
                bool met = true;
                foreach (DialogueCondition condition in init.conditions)
                {
                    if (!condition.Met(context))
                    {
                        met = false;
                        break;
                    }
                }
                if (!met)
                {
                    continue;
                }
                pendingInitiations[npc] = init.dialogue;
                Verse.AI.Job job = JobMaker.MakeJob(TSC_DefOf.TSC_InitiateTalk, target);
                npc.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);
                return;
            }
        }

        /// <summary>Protagonist if alive and on this map; otherwise the nearest other free colonist.</summary>
        private Pawn FindInitiationTarget(Pawn npc, Map map)
        {
            Pawn hero = Protagonist;
            if (hero != null && hero != npc && hero.Spawned && hero.Map == map && !hero.Downed)
            {
                return hero;
            }
            Pawn best = null;
            float bestDist = float.MaxValue;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == npc || p.Downed || p.Dead)
                {
                    continue;
                }
                float dist = p.Position.DistanceToSquared(npc.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>Called by the job driver when the walk-up completes; returns the dialogue to open.</summary>
        public DialogueDef ConsumePendingInitiation(Pawn npc, bool arrived)
        {
            if (!pendingInitiations.TryGetValue(npc, out DialogueDef dialogue))
            {
                return null;
            }
            pendingInitiations.Remove(npc);
            if (!arrived)
            {
                return null; // job failed; conditions will re-fire later
            }
            DialogueExtension ext = npc.kindDef?.GetModExtension<DialogueExtension>();
            if (ext != null)
            {
                foreach (DialogueInitiation init in ext.initiations)
                {
                    if (init.dialogue == dialogue && init.once)
                    {
                        firedInitiations.Add(init.Key(npc.kindDef));
                    }
                }
            }
            return dialogue;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref flags, "flags", LookMode.Value);
            Scribe_Collections.Look(ref namedNpcs, "namedNpcs", LookMode.Value, LookMode.Reference);
            Scribe_Collections.Look(ref firedInitiations, "firedInitiations", LookMode.Value);
            Scribe_References.Look(ref protagonist, "protagonist");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (flags == null)
                {
                    flags = new HashSet<string>();
                }
                if (namedNpcs == null)
                {
                    namedNpcs = new Dictionary<string, Pawn>();
                }
                if (firedInitiations == null)
                {
                    firedInitiations = new HashSet<string>();
                }
            }
        }
    }
}
