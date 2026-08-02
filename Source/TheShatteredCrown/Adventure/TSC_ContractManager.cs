using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Adventure Mode's engine: generates guild contracts on a cadence from
    /// the pool of TSC_Contract_* quest scripts, scaled to the party. The
    /// quest tab is the MVP delivery surface; guild-hall envoys will later
    /// present the same generated offers through dialogue.
    /// Contracts are OFFERS (autoAccept false): the player picks, and picking
    /// is the whole game loop, so the generator keeps a small standing board
    /// rather than a flood.
    /// </summary>
    public class TSC_ContractManager : WorldComponent
    {
        private const int CheckIntervalTicks = 2000;
        /// <summary>Offers waiting on the board at once; new ones only generate below this.</summary>
        private const int MaxOpenOffers = 2;
        private static readonly IntRange RestockDays = new IntRange(1, 3);

        private int nextContractTick = -1;

        public TSC_ContractManager(World world) : base(world)
        {
        }

        /// <summary>
        /// Save repair: quests bake their parts at GENERATION, and contracts
        /// are generated when the board stocks them - so offers minted while
        /// the site-tile search was broken are siteless forever, and every
        /// one the player picks up is a dud with no map marker. The code fix
        /// cannot reach them; this can. On load, any pending or ongoing
        /// contract with no world object to its name is struck, the board
        /// restocks under the fixed generator, and the player is told once.
        /// </summary>
        // Once per session, on the first world TICK - never in FinalizeInit.
        // Learned the hard way: FinalizeInit runs inside Game.LoadGame BEFORE
        // cross-references resolve, so every Scribe_References field in the
        // game is still null there - and a spawned site is saved by
        // REFERENCE, so the audit read every healthy ACCEPTED contract as
        // siteless and struck it. By the first tick, references are real.
        private bool auditDone;

        /// <summary>
        /// Save repair for the board's stock: offers minted while the tile
        /// search was broken hold sites with unusable tiles (default tile 0,
        /// an ocean) forever. ONLY un-accepted offers are judged - their
        /// sites are deep-saved inside the quest part and thus fully loaded.
        /// Accepted quests are left alone: their sites live in the world,
        /// where the player can see the truth for themselves.
        /// </summary>
        private void AuditBoardStock()
        {
            int struck = 0;
            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            for (int i = quests.Count - 1; i >= 0; i--)
            {
                Quest quest = quests[i];
                if (quest.State != QuestState.NotYetAccepted || !IsContract(quest.root))
                {
                    continue;
                }
                bool hasSite = false;
                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is QuestPart_SpawnWorldObject spawn && spawn.worldObject != null
                        && Patch_TryFindNewSiteTile_NoWater.UsableSiteTile(spawn.worldObject.Tile))
                    {
                        hasSite = true;
                        break;
                    }
                }
                if (hasSite)
                {
                    continue;
                }
                quest.End(QuestEndOutcome.InvalidPreAcceptance, sendLetter: false, playSound: false);
                struck++;
            }
            if (struck > 0)
            {
                Log.Message($"[The Shattered Crown] Struck {struck} board offer(s) holding unusable sites "
                    + "(an older tile-search bug); the guild board will restock with sound ones.");
                Messages.Message($"The guild audits its books: {struck} unsound contract(s) struck from the ledger.",
                    MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        public void KickstartFirstContract()
        {
            nextContractTick = 0;
        }

        /// <summary>
        /// A guild factor asked in person and the board was empty: generate
        /// one now, off-cadence. The regular restock timer is untouched -
        /// but the favor is ONCE A DAY. Uncooled, this was an infinite
        /// faucet: take everything, reopen the board, and the factor
        /// "found" two more in the satchel, forever. The world cadence
        /// (1-3 days per posting) is the real economy; this is a courtesy
        /// for a party that arrives to an empty board, not a bypass.
        /// </summary>
        private int nextOnDemandTick = -1;
        private const int OnDemandCooldownTicks = 60000; // one day

        /// <summary>
        /// Top the board up to `target` offers in one visit. The cooldown
        /// gates the VISIT, not each contract: the story scenario's board
        /// restocks only through this path, so a per-contract cooldown
        /// would quietly halve it. One satchel-opening a day, however many
        /// postings come out of it.
        /// </summary>
        public int TopUpNow(int target, int openNow)
        {
            // Broader gate than the world-clock restock: a guild factor can
            // hand out work in EITHER scenario (story campaigns visit guild
            // halls too); only the automatic cadence is Adventure-only.
            if (!TSC_RpgMode.Active || Find.TickManager.TicksGame < nextOnDemandTick)
            {
                return 0;
            }
            int made = 0;
            for (int i = openNow; i < target; i++)
            {
                if (!GenerateContract())
                {
                    break;
                }
                made++;
            }
            if (made > 0)
            {
                nextOnDemandTick = Find.TickManager.TicksGame + OnDemandCooldownTicks;
            }
            return made;
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (!auditDone)
            {
                auditDone = true;
                AuditBoardStock();
            }
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0
                || !TSC_AdventureModeGate.Active)
            {
                return;
            }
            if (nextContractTick < 0)
            {
                // Fresh world that never kickstarted (dev quickstart): begin
                // the cadence now rather than never.
                nextContractTick = Find.TickManager.TicksGame;
            }
            ReclaimStrayStrongbox();
            if (Find.TickManager.TicksGame < nextContractTick
                || OpenOfferCount() >= MaxOpenOffers)
            {
                return;
            }
            // Only advance the cadence when a contract actually generated:
            // the templates need a player map for site placement, and a fully
            // nomadic party can be mid-caravan with none loaded. A failed
            // beat retries on the next pulse instead of silently skipping
            // 1-3 days of work.
            if (GenerateContract())
            {
                nextContractTick = Find.TickManager.TicksGame + RestockDays.RandomInRange * GenDate.TicksPerDay;
            }
        }

        /// <summary>
        /// Save-compat and belt-and-braces: a strongbox in the party's bags
        /// with no delve running means a completed delve failed to collect it
        /// (the double-pay bug) - a guild agent quietly reclaims it before it
        /// can instantly complete the next delve contract.
        /// </summary>
        private static void ReclaimStrayStrongbox()
        {
            ThingDef boxDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_GuildStrongbox");
            if (boxDef == null)
            {
                return;
            }
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                // Any live delve (offered or running) legitimately involves a
                // box: hands off, the quest's own TakeItem part collects it.
                if (quest.root != null && quest.root.defName == "TSC_Contract_Delve"
                    && (quest.State == QuestState.NotYetAccepted || quest.State == QuestState.Ongoing))
                {
                    return;
                }
            }
            int taken = QuestPart_TSC_TakeItem.RemoveFromParty(boxDef, int.MaxValue);
            if (taken > 0)
            {
                Messages.Message("A guild agent collects the recovered strongbox: returned goods belong under guild seal.",
                    MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        private static int OpenOfferCount()
        {
            int count = 0;
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.State == QuestState.NotYetAccepted && IsContract(quest.root))
                {
                    count++;
                }
            }
            return count;
        }

        public static bool IsContract(QuestScriptDef def)
        {
            return def != null && def.defName.StartsWith("TSC_Contract_");
        }

        private bool GenerateContract()
        {
            // Site placement needs an origin map; with the whole party in a
            // caravan there is none, and the offer waits for the next stop.
            bool anyMap = false;
            foreach (Map map in Find.Maps)
            {
                if (map.mapPawns.FreeColonistsSpawnedCount > 0)
                {
                    anyMap = true;
                    break;
                }
            }
            if (!anyMap)
            {
                return false;
            }
            // Prefer templates NOT already on the board or in progress: two
            // live delves would race their detections against one strongbox,
            // and a varied board is better play anyway. Fall back to any
            // template if everything is in use.
            HashSet<QuestScriptDef> active = new HashSet<QuestScriptDef>();
            foreach (Quest existing in Find.QuestManager.QuestsListForReading)
            {
                if ((existing.State == QuestState.NotYetAccepted || existing.State == QuestState.Ongoing)
                    && IsContract(existing.root))
                {
                    active.Add(existing.root);
                }
            }
            List<QuestScriptDef> templates = new List<QuestScriptDef>();
            List<QuestScriptDef> fresh = new List<QuestScriptDef>();
            foreach (QuestScriptDef def in DefDatabase<QuestScriptDef>.AllDefsListForReading)
            {
                if (!IsContract(def))
                {
                    continue;
                }
                templates.Add(def);
                if (!active.Contains(def))
                {
                    fresh.Add(def);
                }
            }
            if (templates.Count == 0)
            {
                return false;
            }
            QuestScriptDef template = (fresh.Count > 0 ? fresh : templates).RandomElement();
            Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(template, ContractPoints());
            if (quest == null)
            {
                return false;
            }
            if (!quest.hidden)
            {
                QuestUtility.SendLetterQuestAvailable(quest);
            }
            return true;
        }

        /// <summary>
        /// Threat points for the contract's site: grows with the party's
        /// adventuring levels (the mod's own progression, not colony wealth -
        /// a travelling party has no wealth curve to lean on), scaled by the
        /// difficulty's threatScale, and clamped so the first contract is
        /// beatable by one level-0 rider and the hundredth still generates.
        /// </summary>
        public static float PartyScaledPoints()
        {
            return ContractPoints();
        }

        public static float ContractPoints()
        {
            int totalLevels = 0;
            int adventurers = 0;
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                adventurers++;
                totalLevels += TSC_ProgressionManager.Current.LevelOf(pawn);
            }
            float basePoints = 90f + 55f * totalLevels + 35f * Mathf.Max(0, adventurers - 1);
            float threatScale = Find.Storyteller?.difficulty?.threatScale ?? 1f;
            return Mathf.Clamp(basePoints * threatScale, 80f, 2200f);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextContractTick, "nextContractTick", -1);
            Scribe_Values.Look(ref nextOnDemandTick, "nextOnDemandTick", -1);
        }
    }

    /// <summary>
    /// A travelling party can take guild work: vanilla refuses to accept any
    /// quest without a permanent colony, and this mod's party never founds
    /// one. Story quests route around it by accepting in dialogue; contract
    /// OFFERS are accepted from the quest tab, so the rule itself must yield
    /// for the mod's quests.
    /// </summary>
    [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.CanAcceptQuest))]
    public static class Patch_CanAcceptQuest_TravellingParty
    {
        public static void Postfix(Quest quest, ref AcceptanceReport __result)
        {
            if (__result.Accepted || quest?.root == null
                || !quest.root.defName.StartsWith("TSC_"))
            {
                return;
            }
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                __result = true;
                return;
            }
        }
    }
}
