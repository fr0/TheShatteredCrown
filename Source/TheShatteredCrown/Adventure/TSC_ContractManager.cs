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

        public void KickstartFirstContract()
        {
            nextContractTick = 0;
        }

        /// <summary>
        /// A guild factor asked in person and the board was empty: generate
        /// one now, off-cadence. The regular restock timer is untouched.
        /// </summary>
        public bool TryGenerateNow()
        {
            return TSC_AdventureModeGate.Active && GenerateContract();
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
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

        private static float ContractPoints()
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
