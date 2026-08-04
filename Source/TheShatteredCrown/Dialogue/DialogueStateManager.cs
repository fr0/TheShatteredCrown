using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
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
        /// <summary>Failed retryable checks: key -> game tick when retrying is allowed again.</summary>
        private Dictionary<string, int> retryAtTicks = new Dictionary<string, int>();
        private Dictionary<string, Pawn> namedNpcs = new Dictionary<string, Pawn>();
        private HashSet<string> firedInitiations = new HashSet<string>();
        private Pawn protagonist;
        private List<string> npcKeysWorking;
        private List<Pawn> npcValuesWorking;

        /// <summary>Transient: pawns currently walking over to start a conversation (not saved; retries after load).</summary>
        private static readonly Dictionary<Pawn, DialogueDef> pendingInitiations = new Dictionary<Pawn, DialogueDef>();

        private const int CheckIntervalTicks = 2500; // hourly

        public DialogueStateManager(World world) : base(world)
        {
            cached = this;
        }

        // Cached: this is the mod's most-called accessor (flags, affinity,
        // named pawns - from tick sweeps, damage patches, and per-frame UI),
        // and World.GetComponent<T> is a linear type-scan of every world
        // component in the load order on every call. The instance registers
        // itself in the constructor; the world check invalidates it across
        // save loads.
        private static DialogueStateManager cached;

        public static DialogueStateManager Current
        {
            get
            {
                DialogueStateManager c = cached;
                if (c != null && ReferenceEquals(c.world, Find.World))
                {
                    return c;
                }
                cached = Find.World?.GetComponent<DialogueStateManager>();
                return cached;
            }
        }

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

        // ------------------------------------------------ retryable check cooldowns

        /// <summary>Starts a retry cooldown: the check stays hidden for this many in-game hours.</summary>
        public void StartRetryCooldown(string key, float hours)
        {
            if (!key.NullOrEmpty())
            {
                retryAtTicks[key] = Find.TickManager.TicksGame + (int)(hours * GenDate.TicksPerHour);
            }
        }

        /// <summary>Ticks left on a retry cooldown, or 0 if it is over.</summary>
        public int CooldownLeft(string key)
        {
            if (key.NullOrEmpty() || !retryAtTicks.TryGetValue(key, out int at))
            {
                return 0;
            }
            return Mathf.Max(0, at - Find.TickManager.TicksGame);
        }

        public bool IsCoolingDown(string key)
        {
            return !key.NullOrEmpty()
                && retryAtTicks.TryGetValue(key, out int at)
                && Find.TickManager.TicksGame < at;
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
                TSC_ProgressionManager.Current.SeedLevel(existing, def.startingLevel);
                return existing;
            }
            PawnGenerationRequest request = new PawnGenerationRequest(def.kind, factionIfNew, PawnGenerationContext.NonPlayer)
            {
                ForceGenerateNewPawn = true,
                AllowPregnant = false,
                // Companions fight: no backstories/traits that disable violence.
                MustBeCapableOfViolence = true,
            };
            if (def.gender != Gender.None)
            {
                request.FixedGender = def.gender;
            }
            if (def.biologicalAge > 0f)
            {
                request.FixedBiologicalAge = def.biologicalAge;
                request.FixedChronologicalAge = def.biologicalAge;
            }
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            // Married pairs: whichever half generates second completes the
            // link (the def sets spouse on one side only).
            Pawn partner = def.spouse != null ? GetNamedNpcIfExists(def.spouse) : null;
            if (partner == null)
            {
                foreach (NamedNpcDef other in DefDatabase<NamedNpcDef>.AllDefsListForReading)
                {
                    if (other.spouse == def)
                    {
                        partner = GetNamedNpcIfExists(other);
                        break;
                    }
                }
            }
            if (partner != null && !partner.Dead && pawn.relations != null && partner.relations != null
                && !pawn.relations.DirectRelationExists(PawnRelationDefOf.Spouse, partner))
            {
                pawn.relations.AddDirectRelation(PawnRelationDefOf.Spouse, partner);
            }
            pawn.Name = def.MakeName();
            // Fixed backstories: bio text and work-tags follow the def; the
            // rolled skills stay (our proficiency/class systems carry the
            // character identity anyway).
            if (def.childhood != null)
            {
                pawn.story.Childhood = def.childhood;
            }
            if (def.adulthood != null)
            {
                pawn.story.Adulthood = def.adulthood;
            }
            // Skill floors: the swapped-in backstories' skillGains never
            // applied (they arrive after the roll), so signature skills are
            // guaranteed here instead.
            if (!def.minSkills.NullOrEmpty() && pawn.skills != null)
            {
                foreach (TSC_MinSkill entry in def.minSkills)
                {
                    if (entry?.skill == null)
                    {
                        continue;
                    }
                    SkillRecord record = pawn.skills.GetSkill(entry.skill);
                    if (record != null && !record.TotallyDisabled && record.Level < entry.level)
                    {
                        record.Level = entry.level;
                    }
                }
            }
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
            TSC_ProgressionManager.Current.SeedLevel(pawn, def.startingLevel);
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

        // ---------------------------------------------------------------- affinity

        private Dictionary<string, int> affinity = new Dictionary<string, int>();

        /// <summary>Relationship score with a named character. 0 = neutral; dialogue choices and story actions move it.</summary>
        public int AffinityOf(NamedNpcDef def)
        {
            return def != null && affinity.TryGetValue(def.defName, out int value) ? value : 0;
        }

        /// <summary>BG3-style approval: shifts affinity and (by default) surfaces "X approves. (+5)".</summary>
        public void ChangeAffinity(NamedNpcDef def, int amount, bool announce = true)
        {
            if (def == null || amount == 0)
            {
                return;
            }
            affinity[def.defName] = AffinityOf(def) + amount;
            if (announce)
            {
                Pawn pawn = GetNamedNpcIfExists(def);
                string name = pawn?.Name?.ToStringShort ?? def.label ?? def.defName;
                string strength = UnityEngine.Mathf.Abs(amount) >= 10 ? "greatly " : "";
                string word = amount > 0 ? "approves" : "disapproves";
                string line = $"{name} {strength}{word}. ({(amount > 0 ? "+" : "")}{amount})";
                if (pawn != null && pawn.Spawned)
                {
                    Messages.Message(line, pawn, MessageTypeDefOf.SilentInput, historical: false);
                }
                else
                {
                    Messages.Message(line, MessageTypeDefOf.SilentInput, historical: false);
                }
            }
        }

        /// <summary>Human-readable relationship tier for UI.</summary>
        public static string AffinityTier(int value)
        {
            if (value <= -20) return "hostile";
            if (value < 0) return "cold";
            if (value < 10) return "neutral";
            if (value < 25) return "warm";
            return "devoted";
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

        /// <summary>
        /// Named characters must KEEP their unique pawn kinds: the whole
        /// dialogue layer (talk trees, initiated camp talks, the trader and
        /// trade-discount lookups) hangs off kindDef. Some recruit paths
        /// normalize a joined pawn's kind to Colonist (observed in play: Bran
        /// and Maewyn both drifted, which silently killed their camp talks
        /// forever), so drift is healed on load and before each hourly
        /// initiation check.
        /// </summary>
        private void HealNamedNpcKinds()
        {
            foreach (KeyValuePair<string, Pawn> entry in namedNpcs)
            {
                Pawn pawn = entry.Value;
                if (pawn == null || pawn.Dead)
                {
                    continue;
                }
                NamedNpcDef def = DefDatabase<NamedNpcDef>.GetNamedSilentFail(entry.Key);
                if (def?.kind != null && pawn.kindDef != def.kind)
                {
                    Log.Message($"[The Shattered Crown] Restoring {pawn.LabelShortCap}'s pawn kind ({pawn.kindDef?.defName ?? "null"} -> {def.kind.defName}).");
                    pawn.ChangeKind(def.kind);
                    // Kind changes on a spawned pawn can leave the render tree
                    // uninitialized ("Node is null ... EnsureGraphicsInitialized"
                    // during parallel pre-draw); force a rebuild.
                    if (pawn.Spawned)
                    {
                        pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                    }
                }
            }
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            HealNamedNpcKinds();
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }
            HealNamedNpcKinds();
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
                    // Rolls only happen during night rest (~6 of 24 hourly
                    // checks), so compress the mtb to keep the road cadence
                    // matching the camp cadence.
                    if (!Rand.MTBEventOccurs(init.mtbDays * 0.25f, GenDate.TicksPerDay, CheckIntervalTicks))
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
            // Not while anyone is fighting. The caravan path only rolls
            // during night rest, but nothing stopped the map path from
            // opening Madoc's worst evening while raiders were mid-charge.
            // Same quiet test the camp system uses; the roll simply comes
            // back next hour.
            if (!TSC_CampDeploy.CombatQuiet(map))
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
                if (!npc.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc))
                {
                    // Job refused: clear the pending entry or this pawn's
                    // initiations would be blocked for the rest of the session.
                    pendingInitiations.Remove(npc);
                }
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
            Scribe_Collections.Look(ref retryAtTicks, "retryAtTicks", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && retryAtTicks == null)
            {
                retryAtTicks = new Dictionary<string, int>();
            }
            // Tolerant manual scribing: reference-valued dictionaries REQUIRE
            // working lists (the plain Look overload errors and the dict was
            // silently never saved). Null pawns (long-gone characters) are
            // dropped on load instead of erroring.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                npcKeysWorking = new List<string>();
                npcValuesWorking = new List<Pawn>();
                foreach (KeyValuePair<string, Pawn> pair in namedNpcs)
                {
                    if (pair.Value != null && !pair.Value.Discarded)
                    {
                        npcKeysWorking.Add(pair.Key);
                        npcValuesWorking.Add(pair.Value);
                    }
                }
            }
            if (Scribe.EnterNode("namedNpcs"))
            {
                try
                {
                    Scribe_Collections.Look(ref npcKeysWorking, "keys", LookMode.Value);
                    Scribe_Collections.Look(ref npcValuesWorking, "values", LookMode.Reference);
                }
                finally
                {
                    Scribe.ExitNode();
                }
            }
            Scribe_Collections.Look(ref firedInitiations, "firedInitiations", LookMode.Value);
            Scribe_Collections.Look(ref affinity, "affinity", LookMode.Value, LookMode.Value);
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
                if (affinity == null)
                {
                    affinity = new Dictionary<string, int>();
                }
                namedNpcs = new Dictionary<string, Pawn>();
                if (npcKeysWorking != null && npcValuesWorking != null)
                {
                    int count = System.Math.Min(npcKeysWorking.Count, npcValuesWorking.Count);
                    for (int i = 0; i < count; i++)
                    {
                        if (!npcKeysWorking[i].NullOrEmpty() && npcValuesWorking[i] != null
                            && !namedNpcs.ContainsKey(npcKeysWorking[i]))
                        {
                            namedNpcs[npcKeysWorking[i]] = npcValuesWorking[i];
                        }
                    }
                }
                npcKeysWorking = null;
                npcValuesWorking = null;
            }
        }
    }
}
