using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Marks a SitePartDef's site as persistent: vanilla removes a Site world
    /// object once it has been visited and left, which deleted Serra's camp
    /// (the hub the rites REQUIRE returning to), the grove, Underhill, the
    /// ettersnap cave, and the moss barrow - softlocking their quests. With
    /// this extension the world object stays; the map still despawns when
    /// everyone leaves and regenerates on the next visit (village genstep
    /// re-spawns residents; barrow gensteps rebuild crypt, loot, and
    /// shamblers; the animal spawn node re-dens the ettersnap).
    /// </summary>
    public class TSC_PersistentSiteExtension : DefModExtension
    {
        /// <summary>
        /// If set, the site only persists WHILE a quest from this script is
        /// ongoing/offered - objective sites (barrow, cave) clean up normally
        /// once their quest resolves, so they can't be farmed forever.
        /// Story hubs leave this null and persist for good.
        /// </summary>
        public QuestScriptDef whileQuestActive;

        /// <summary>Suppress vanilla's timed detection raids here (story hubs
        /// only - dungeon sites keep the looting pressure).</summary>
        public bool noDetectionRaids;

        /// <summary>
        /// Suppress vanilla's "Too dangerous to re-enter for X" lockout. That
        /// cooldown is an anti-farming measure for randomly generated sites;
        /// on story locations it just walls the player out of the village,
        /// the camp, or a dungeon they stepped out of to resupply. Default ON
        /// for every site carrying this extension; set false to opt back in.
        /// </summary>
        public bool noEnterCooldown = true;

        private static TSC_PersistentSiteExtension ExtensionOf(WorldObject worldObject)
        {
            if (!(worldObject is Site site))
            {
                return null;
            }
            for (int i = 0; i < site.parts.Count; i++)
            {
                TSC_PersistentSiteExtension ext = site.parts[i]?.def?.GetModExtension<TSC_PersistentSiteExtension>();
                if (ext != null)
                {
                    return ext;
                }
            }
            return null;
        }

        public static bool PersistsNow(WorldObject worldObject)
        {
            TSC_PersistentSiteExtension ext = ExtensionOf(worldObject);
            if (ext == null)
            {
                return false;
            }
            return ext.whileQuestActive == null || QuestOngoing(ext.whileQuestActive);
        }

        public static bool RaidExempt(WorldObject worldObject)
        {
            TSC_PersistentSiteExtension ext = ExtensionOf(worldObject);
            return ext != null && ext.noDetectionRaids;
        }

        public static bool CooldownExempt(WorldObject worldObject)
        {
            TSC_PersistentSiteExtension ext = ExtensionOf(worldObject);
            return ext != null && ext.noEnterCooldown;
        }

        private static bool QuestOngoing(QuestScriptDef script)
        {
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root == script && (quest.State == QuestState.Ongoing || quest.State == QuestState.NotYetAccepted))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Story hubs do not call raids down on their own guests: vanilla's
    /// TimedDetectionRaids comp ("your presence has been detected...") starts
    /// counting the moment the player stays at any site. Block it from ever
    /// starting on raid-exempt sites.
    /// </summary>
    [HarmonyPatch(typeof(TimedDetectionRaids), "StartDetectionCountdown")]
    public static class Patch_NoDetectionRaids_StoryHubs
    {
        public static bool Prefix(TimedDetectionRaids __instance)
        {
            return !TSC_PersistentSiteExtension.RaidExempt(__instance.parent);
        }
    }

    /// <summary>
    /// Story sites have no re-entry lockout: vanilla starts an EnterCooldown
    /// the moment the player's last pawn leaves a site map, so walking out of
    /// Harrowfield to make camp locks the village behind "Too dangerous to
    /// re-enter for 4 days". Never let the countdown start there.
    /// </summary>
    [HarmonyPatch(typeof(EnterCooldownComp), nameof(EnterCooldownComp.Start))]
    public static class Patch_NoEnterCooldown_StorySites
    {
        public static bool Prefix(EnterCooldownComp __instance)
        {
            return !TSC_PersistentSiteExtension.CooldownExempt(__instance.parent);
        }
    }

    /// <summary>
    /// Covers cooldowns already baked into an existing save (and the tick or
    /// two before the guard sweep clears one): the site may still hold an end
    /// tick, but it never blocks entering.
    /// </summary>
    [HarmonyPatch(typeof(EnterCooldownComp), "get_BlocksEntering")]
    public static class Patch_NoEnterCooldownBlock_StorySites
    {
        public static void Postfix(EnterCooldownComp __instance, ref bool __result)
        {
            if (__result && TSC_PersistentSiteExtension.CooldownExempt(__instance.parent))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// Disarms countdowns that already started (saves from before the block,
    /// or any start path the prefix misses). Cheap sweep, every ~4 in-game
    /// seconds.
    /// </summary>
    public partial class TSC_StoryHubGuard : GameComponent
    {
        public TSC_StoryHubGuard(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0)
            {
                return;
            }
            HealMissingHarrowfieldQuest();
            HealUnacceptedQuests();
            HealMissingCompanionPets();
            FeedNamedNpcs();
            var sites = Find.WorldObjects?.Sites;
            if (sites == null)
            {
                return;
            }
            for (int i = 0; i < sites.Count; i++)
            {
                if (TSC_PersistentSiteExtension.RaidExempt(sites[i]))
                {
                    TimedDetectionRaids comp = sites[i].GetComponent<TimedDetectionRaids>();
                    if (comp != null && comp.DetectionCountdownStarted)
                    {
                        comp.ResetCountdown();
                    }
                }
                // Clears the "too dangerous to re-enter" line from the world
                // map too, which the BlocksEntering patch alone would leave up.
                if (TSC_PersistentSiteExtension.CooldownExempt(sites[i]))
                {
                    EnterCooldownComp cooldown = sites[i].GetComponent<EnterCooldownComp>();
                    if (cooldown != null && cooldown.Active)
                    {
                        cooldown.Stop();
                    }
                }
            }
        }
    }

    public partial class TSC_StoryHubGuard
    {
        private bool harrowfieldHealChecked;
        private static System.Collections.Generic.HashSet<PawnKindDef> namedNpcKinds;

        /// <summary>
        /// Named NPCs never starve: villagers/companNPCs eat only from their
        /// pockets (the duty tree can't harvest crops), so any of them who
        /// hits UrgentlyHungry with an empty pantry gets pemmican slipped in.
        /// Recruited (player-faction) characters are the player's charge and
        /// are skipped.
        /// </summary>
        private static void FeedNamedNpcs()
        {
            if (namedNpcKinds == null)
            {
                namedNpcKinds = new System.Collections.Generic.HashSet<PawnKindDef>();
                foreach (NamedNpcDef def in DefDatabase<NamedNpcDef>.AllDefsListForReading)
                {
                    if (def.kind != null)
                    {
                        namedNpcKinds.Add(def.kind);
                    }
                }
            }
            foreach (Map map in Find.Maps)
            {
                System.Collections.Generic.IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn.Faction == Faction.OfPlayer || !namedNpcKinds.Contains(pawn.kindDef)
                        || pawn.needs?.food == null || pawn.inventory == null)
                    {
                        continue;
                    }
                    if ((int)pawn.needs.food.CurCategory < (int)HungerCategory.UrgentlyHungry)
                    {
                        continue;
                    }
                    bool hasFood = false;
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        if (thing.def.IsNutritionGivingIngestible)
                        {
                            hasFood = true;
                            break;
                        }
                    }
                    if (!hasFood)
                    {
                        Thing pemmican = ThingMaker.MakeThing(ThingDefOf.Pemmican);
                        pemmican.stackCount = 10;
                        pawn.inventory.innerContainer.TryAdd(pemmican, false);
                    }
                }
            }
        }

        /// <summary>
        /// This mod's quests are handed over in conversation, so the player
        /// has already accepted them by the time they exist - but vanilla
        /// refuses to accept any quest while the colony has no permanent
        /// settlement, which this mod's travelling party never founds. An
        /// un-accepted quest never fires its Initiate signal, so its site is
        /// never spawned and the objective simply is not on the map.
        /// Accept anything of ours the player is stuck on.
        /// </summary>
        private static void HealUnacceptedQuests()
        {
            System.Collections.Generic.List<Quest> stuck = null;
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.State != QuestState.NotYetAccepted || quest.root == null
                    || !quest.root.defName.StartsWith("TSC_"))
                {
                    continue;
                }
                // Adventure Mode contracts are OFFERS: sitting un-accepted on
                // the board is their normal state, not a stranding. The
                // CanAcceptQuest patch makes them acceptable by hand.
                if (TSC_ContractManager.IsContract(quest.root))
                {
                    continue;
                }
                stuck = stuck ?? new System.Collections.Generic.List<Quest>();
                stuck.Add(quest);
            }
            if (stuck == null)
            {
                return;
            }
            Pawn accepter = null;
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
            {
                accepter = pawn;
                break;
            }
            foreach (Quest quest in stuck)
            {
                quest.Accept(accepter);
                Log.Message($"[The Shattered Crown] Accepted stranded quest '{quest.name}' "
                    + "(story quests are accepted in dialogue; vanilla blocks manual acceptance without a permanent colony).");
            }
        }

        /// <summary>
        /// Save-compat self-heal: a companion recruited BEFORE they had a pet
        /// defined (Maewyn, whose Corvus used to be an anonymous grove crow)
        /// never spawns at their home site again, so the normal pet spawn can
        /// never fire for them. Give them their animal once, guarded by a
        /// persistent flag so a pet that later DIES stays dead.
        /// </summary>
        private static void HealMissingCompanionPets()
        {
            foreach (NamedNpcDef npcDef in DefDatabase<NamedNpcDef>.AllDefsListForReading)
            {
                if (npcDef.petKind == null)
                {
                    continue;
                }
                string givenFlag = "TSC_PetGiven_" + npcDef.defName;
                string trainedFlag = "TSC_PetTrained_" + npcDef.defName;
                if (DialogueStateManager.Current.IsSet(givenFlag)
                    && DialogueStateManager.Current.IsSet(trainedFlag))
                {
                    continue;
                }
                Pawn owner = DialogueStateManager.Current.GetNamedNpcIfExists(npcDef);
                if (owner == null || owner.Dead || !owner.Spawned
                    || owner.Faction != Faction.OfPlayer || owner.relations == null)
                {
                    continue; // not recruited (or not on a live map): nothing to heal
                }
                Pawn pet = null;
                foreach (DirectPawnRelation relation in owner.relations.DirectRelations)
                {
                    if (relation.def == PawnRelationDefOf.Bond && relation.otherPawn != null
                        && relation.otherPawn.RaceProps.Animal && !relation.otherPawn.Dead)
                    {
                        pet = relation.otherPawn;
                        break;
                    }
                }
                if (!DialogueStateManager.Current.IsSet(givenFlag))
                {
                    DialogueStateManager.Current.Set(givenFlag);
                    if (pet == null)
                    {
                        pet = PawnGenerator.GeneratePawn(npcDef.petKind, Faction.OfPlayer);
                        if (!npcDef.petName.NullOrEmpty())
                        {
                            pet.Name = new NameSingle(npcDef.petName);
                        }
                        GenSpawn.Spawn(pet, CellFinder.RandomClosewalkCellNear(owner.Position, owner.Map, 3), owner.Map);
                        owner.relations.AddDirectRelation(PawnRelationDefOf.Bond, pet);
                        Messages.Message($"{pet.LabelShortCap} has caught up with {owner.LabelShort}.",
                            pet, MessageTypeDefOf.PositiveEvent, historical: false);
                    }
                }
                // Separate one-shot: repairs a pet handed over by an earlier
                // build that trained tameness but never obedience (and so could
                // not be mastered). Flagged so the player's own later choice to
                // un-master the animal is never overridden.
                if (!DialogueStateManager.Current.IsSet(trainedFlag))
                {
                    DialogueStateManager.Current.Set(trainedFlag);
                    if (pet != null && pet.Faction == Faction.OfPlayer)
                    {
                        TSC_CompanionPets.MakePlayerPet(pet, owner);
                    }
                }
            }
        }

        /// <summary>
        /// Save-compat self-heal: quest parts are BAKED into the save when a
        /// quest generates, so ettersnap quests created before the Harrowfield
        /// epilogue existed completed the rites without handing out "The Road
        /// to Harrowfield". If the rites are done but that quest has never
        /// existed in any state, give it now. One check per session.
        /// </summary>
        private void HealMissingHarrowfieldQuest()
        {
            if (harrowfieldHealChecked)
            {
                return;
            }
            if (!DialogueStateManager.Current.IsSet("TSC_RitesDone"))
            {
                return; // keep checking: the rites may complete later this session
            }
            harrowfieldHealChecked = true;
            QuestScriptDef script = DefDatabase<QuestScriptDef>.GetNamedSilentFail("TSC_SideQuest_Harrowfield");
            if (script == null)
            {
                return;
            }
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root == script)
                {
                    return; // already given (any state): nothing to heal
                }
            }
            Quest healed = QuestUtility.GenerateQuestAndMakeAvailable(script, 0f);
            if (healed != null)
            {
                QuestUtility.SendLetterQuestAvailable(healed);
                Log.Message("[The Shattered Crown] Save-compat: granted the missing 'Road to Harrowfield' epilogue quest (rites were completed on an older build).");
            }
        }
    }

    [HarmonyPatch(typeof(Site), nameof(Site.ShouldRemoveMapNow))]
    public static class Patch_Site_PersistentStorySites
    {
        /// <summary>The map may go (regenerates on the next visit); the world object stays.</summary>
        public static void Postfix(Site __instance, ref bool alsoRemoveWorldObject)
        {
            if (alsoRemoveWorldObject && TSC_PersistentSiteExtension.PersistsNow(__instance))
            {
                alsoRemoveWorldObject = false;
            }
        }
    }
}
