using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Marks a ThingDef as a shard of the shattered crown and names the
    /// unique ability it grants its carrier. The extension IS the shard
    /// registry: ShardKeeper's drop guard, the Kingsblade's severity count,
    /// and the ability tracker all key off it, so a future act's shard is
    /// one ThingDef with this extension plus a spawner.
    /// </summary>
    public class TSC_ShardAbilityExtension : DefModExtension
    {
        public AbilityDef ability;
    }

    /// <summary>Every def that is a crown shard, resolved once after def load.</summary>
    public static class TSC_Shards
    {
        private static List<ThingDef> all;

        public static List<ThingDef> AllDefs
        {
            get
            {
                if (all == null)
                {
                    all = new List<ThingDef>();
                    foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                    {
                        if (def.HasModExtension<TSC_ShardAbilityExtension>())
                        {
                            all.Add(def);
                        }
                    }
                }
                return all;
            }
        }

        public static bool IsShard(ThingDef def) => def != null && AllDefs.Contains(def);

        public static AbilityDef AbilityFor(ThingDef def) =>
            def?.GetModExtension<TSC_ShardAbilityExtension>()?.ability;
    }

    /// <summary>
    /// The crown shards' powers. Any pawn CARRYING a shard in their
    /// inventory holds that shard's unique ability (the grave shard's
    /// Shardfall, the reliquary shard's Quickening, ...). The tracker polls
    /// holders (maps + caravans), grants/revokes abilities, fires the
    /// first-touch rush dialogue (Dialogues/shard_rush.agd) and the Act 2
    /// title card. Cooldowns belong to the SHARDS, not the pawns: each
    /// shard is one-of-a-kind, so one world clock per ability survives
    /// drop, hand-off and re-pickup.
    /// </summary>
    public class TSC_ShardTracker : WorldComponent
    {
        private const int ScanIntervalTicks = 60;
        public const string RushFlag = "TSC_ShardRushSeen";
        public const string TitleFlag = "TSC_Act2TitleShown";
        public const string Rush2Flag = "TSC_ShardRush2Seen";
        private Dictionary<string, int> lastCastTicks = new Dictionary<string, int>();
        private int lastStormTick = -999999; // legacy single-shard clock, migrated on load
        private bool titlePending;
        private bool act3Pending;
        private bool splitHealed;

        public TSC_ShardTracker(World world) : base(world)
        {
        }

        public static TSC_ShardTracker Current => Find.World.GetComponent<TSC_ShardTracker>();

        public int CooldownRemaining(AbilityDef ability)
        {
            if (ability == null || !lastCastTicks.TryGetValue(ability.defName, out int cast))
            {
                return 0;
            }
            return Mathf.Max(0, cast + ability.cooldownTicksRange.TrueMax - Find.TickManager.TicksGame);
        }

        public void NoteCast(AbilityDef ability)
        {
            if (ability != null)
            {
                lastCastTicks[ability.defName] = Find.TickManager.TicksGame;
            }
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (Find.TickManager.TicksGame % ScanIntervalTicks != 0)
            {
                return;
            }
            ShowPendingTitle();
            HealSharedDefSplit();
            HealThorndenSignal();
            HealBrokenAct3();
            HealBakedShardCount();
            TryFireSecondRush();
            OpenAct3IfPending();
            foreach (ThingDef shardDef in TSC_Shards.AllDefs)
            {
                AbilityDef ability = TSC_Shards.AbilityFor(shardDef);
                if (ability == null)
                {
                    continue;
                }
                foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
                {
                    bool holds = HoldsShard(pawn, shardDef);
                    Ability existing = pawn.abilities?.GetAbility(ability);
                    if (holds && existing == null && pawn.abilities != null)
                    {
                        pawn.abilities.GainAbility(ability);
                        // The shard remembers its own exhaustion: re-pickup
                        // (by anyone) does not reset the day.
                        int remaining = CooldownRemaining(ability);
                        if (remaining > 0)
                        {
                            pawn.abilities.GetAbility(ability)?.StartCooldown(remaining);
                        }
                        if (shardDef.defName == "TSC_CrownShard")
                        {
                            FirstTouch(pawn);
                        }
                    }
                    else if (!holds && existing != null)
                    {
                        pawn.abilities.RemoveAbility(ability);
                    }
                }
            }
        }

        private static bool HoldsShard(Pawn pawn, ThingDef shardDef)
        {
            if (pawn.inventory?.innerContainer != null
                && pawn.inventory.innerContainer.Contains(shardDef))
            {
                return true;
            }
            return pawn.carryTracker?.CarriedThing?.def == shardDef;
        }

        /// <summary>
        /// One-time save repair for the def split. Before the shards were
        /// distinct items, the reliquary spawned a second TSC_CrownShard;
        /// on such a save the SECOND-created instance (higher thingID) IS
        /// the reliquary shard and is re-minted as one, wherever it sits -
        /// a pawn's pack, a caravan, or still lying on the reliquary floor.
        /// </summary>
        private void HealSharedDefSplit()
        {
            if (splitHealed)
            {
                return;
            }
            splitHealed = true;
            ThingDef graveDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard");
            ThingDef reliquaryDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard_Reliquary");
            if (graveDef == null || reliquaryDef == null)
            {
                return;
            }
            List<Thing> shards = new List<Thing>();
            foreach (Map map in Find.Maps)
            {
                shards.AddRange(map.listerThings.ThingsOfDef(graveDef));
            }
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        if (thing.def == graveDef)
                        {
                            shards.Add(thing);
                        }
                    }
                }
                if (pawn.carryTracker?.CarriedThing?.def == graveDef)
                {
                    shards.Add(pawn.carryTracker.CarriedThing);
                }
            }
            if (shards.Count < 2)
            {
                return;
            }
            shards.SortBy(t => t.thingIDNumber);
            for (int i = 1; i < shards.Count; i++)
            {
                Thing old = shards[i];
                Thing minted = ThingMaker.MakeThing(reliquaryDef);
                if (old.Spawned)
                {
                    Map map = old.Map;
                    IntVec3 cell = old.Position;
                    old.Destroy(DestroyMode.Vanish);
                    GenSpawn.Spawn(minted, cell, map);
                }
                else
                {
                    ThingOwner owner = old.holdingOwner;
                    owner?.Remove(old);
                    old.Destroy(DestroyMode.Vanish);
                    if (owner == null || !owner.TryAdd(minted))
                    {
                        minted.Destroy(DestroyMode.Vanish);
                        Log.Warning("[The Shattered Crown] Shard def-split heal could not re-home a converted shard; skipped.");
                        continue;
                    }
                }
                Log.Message("[The Shattered Crown] Shard def-split heal: re-minted a duplicate grave shard as the reliquary shard.");
            }
        }

        /// <summary>
        /// Save-text repair for the nine-to-five shard reduction. Quest
        /// descriptions and their letters are resolved at GENERATION time
        /// and stored in the save, so a quest created before the change
        /// completes with "Two of nine" no matter what the defs say now.
        /// Once per session, every stored TSC quest text gets the count
        /// corrected in place. Reflection over TaggedString fields, because
        /// letter subclasses keep their text in differently named fields.
        /// </summary>
        private static bool bakedCountHealed;

        private static void HealBakedShardCount()
        {
            if (bakedCountHealed)
            {
                return;
            }
            bakedCountHealed = true;
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root?.defName == null || !quest.root.defName.StartsWith("TSC_"))
                {
                    continue;
                }
                string description = quest.description.RawText;
                if (description != null && description.Contains("of nine"))
                {
                    quest.description = description.Replace("of nine", "of five");
                }
                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (!(part is QuestPart_Letter letterPart) || letterPart.letter == null)
                    {
                        continue;
                    }
                    foreach (System.Reflection.FieldInfo field in letterPart.letter.GetType()
                        .GetFields(System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.NonPublic))
                    {
                        if (field.FieldType != typeof(TaggedString))
                        {
                            continue;
                        }
                        TaggedString value = (TaggedString)field.GetValue(letterPart.letter);
                        if (value.RawText != null && value.RawText.Contains("of nine"))
                        {
                            field.SetValue(letterPart.letter, new TaggedString(value.RawText.Replace("of nine", "of five")));
                        }
                    }
                }
            }
        }

        /// <summary>The rush, once ever - then the act turns.</summary>
        private void FirstTouch(Pawn pawn)
        {
            if (DialogueStateManager.Current.IsSet(RushFlag))
            {
                return;
            }
            DialogueStateManager.Current.Set(RushFlag);
            DialogueDef rush = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_ShardRush");
            if (rush != null)
            {
                Find.WindowStack.Add(new Dialog_Conversation(rush, pawn, pawn));
            }
            titlePending = true;
        }

        /// <summary>
        /// The second rush: the first time BOTH shards ride with the same
        /// company (same caravan, or maps under the same root - a party
        /// split across dungeon floors still counts), and the fighting is
        /// over, the shards find each other. The scene reveals their intent
        /// and the vision of the Iron Brand; Act 3 opens when it closes.
        /// </summary>
        private void TryFireSecondRush()
        {
            if (act3Pending || DialogueStateManager.Current.IsSet(Rush2Flag))
            {
                return;
            }
            ThingDef grave = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard");
            ThingDef reliquary = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard_Reliquary");
            if (grave == null || reliquary == null)
            {
                return;
            }
            Pawn first = null;
            Pawn second = null;
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (first == null && HoldsShard(pawn, grave))
                {
                    first = pawn;
                }
                if (second == null && HoldsShard(pawn, reliquary))
                {
                    second = pawn;
                }
            }
            if (first == null || second == null || !SameCompany(first, second))
            {
                return;
            }
            // The scene is a fireside beat: it waits out any active fight on
            // the holder's map, and never interrupts another conversation.
            Map map = second.MapHeld;
            if (map != null && GenHostility.AnyHostileActiveThreatToPlayer(map))
            {
                return;
            }
            if (Find.WindowStack.WindowOfType<Dialog_Conversation>() != null)
            {
                return;
            }
            DialogueStateManager.Current.Set(Rush2Flag);
            DialogueDef rush = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_ShardRush2");
            if (rush != null)
            {
                Find.WindowStack.Add(new Dialog_Conversation(rush, second, second));
            }
            act3Pending = true;
        }

        private static bool SameCompany(Pawn a, Pawn b)
        {
            if (a == b)
            {
                return true;
            }
            Caravan caravanA = a.GetCaravan();
            if (caravanA != null)
            {
                return b.GetCaravan() == caravanA;
            }
            Map mapA = a.MapHeld;
            Map mapB = b.MapHeld;
            return mapA != null && mapB != null
                && TSC_Threat.RootMap(mapA) == TSC_Threat.RootMap(mapB);
        }

        /// <summary>Act 3's curtain: the title card and the Iron Brand quest, once the vision scene closes.</summary>
        private void OpenAct3IfPending()
        {
            if (!act3Pending)
            {
                return;
            }
            if (Find.WindowStack.WindowOfType<Dialog_Conversation>() != null)
            {
                return;
            }
            // SURFACE GATE: quest generation runs QuestNode_GetSiteTile from
            // the party's current map, and a pocket dungeon floor has tile -1
            // ("Attempted to access a tile with ID -1", seen live when the
            // vision fired in the reliquary). Hold the curtain until someone
            // stands under open sky; the act opens on the climb out.
            bool surfaced = false;
            foreach (Map m in Find.Maps)
            {
                if (m.mapPawns.FreeColonistsSpawnedCount > 0
                    && TSC_Threat.RootMap(m) == m && m.Tile.Valid)
                {
                    surfaced = true;
                    break;
                }
            }
            // A party ON THE ROAD is above ground by definition: caravans
            // have world tiles, and the party-rooted tile node anchors on
            // them. Without this the re-grant stalls (journal empty) for as
            // long as the party keeps riding.
            if (!surfaced)
            {
                foreach (RimWorld.Planet.Caravan caravan in Find.WorldObjects.Caravans)
                {
                    if (caravan.IsPlayerControlled && caravan.Tile.Valid)
                    {
                        surfaced = true;
                        break;
                    }
                }
            }
            if (!surfaced)
            {
                return;
            }
            act3Pending = false;
            // Card once, ever - the re-grant heal must not replay it.
            if (!DialogueStateManager.Current.IsSet("TSC_Act3CardShown"))
            {
                DialogueStateManager.Current.Set("TSC_Act3CardShown");
                TSC_TitleCardManager.Show("Act 3", "The Bandit Baron");
            }
            // The chain opener, not the assault: the fire on the road first,
            // then Thornden, then the surety, and the keep last.
            QuestScriptDef script = DefDatabase<QuestScriptDef>.GetNamedSilentFail("TSC_Act3_RoadFire");
            if (script == null)
            {
                return;
            }
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                // Only LIVE or WON quests block a grant. Ended failures stay
                // in the quest list forever, and the heal ENDS wrecks - so a
                // state-blind check here deadlocked the re-grant against the
                // corpse of the quest it was replacing.
                if (quest.root == script
                    && (quest.State == QuestState.NotYetAccepted
                        || quest.State == QuestState.Ongoing
                        || quest.State == QuestState.EndedSuccess))
                {
                    return;
                }
            }
            float points = StorytellerUtility.DefaultThreatPointsNow(Find.World);
            Quest newQuest = QuestUtility.GenerateQuestAndMakeAvailable(script, points);
            if (newQuest != null && !newQuest.EverAccepted)
            {
                // Same reasoning as dialogue-granted chapters: the party has no
                // permanent colony, so an un-accepted quest would never initiate.
                Pawn acceptor = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.FirstOrDefault();
                newQuest.Accept(acceptor);
            }
        }

        /// <summary>Checked once per session: the wreck it repairs can only be created at grant time.</summary>
        private static bool act3Checked;

        private static bool thorndenRetargeted;

        /// <summary>
        /// A Thornden quest granted before the collectors reported their own
        /// defeat still carries a listener for site.AllEnemiesDefeated, which
        /// on a factionless square is true on arrival - the quest completed
        /// itself the moment the party set foot in the village. Move that
        /// listener onto the signal the crew actually sends. Runs long before
        /// anyone travels there, so the letter never gets its chance.
        /// </summary>
        private void HealThorndenSignal()
        {
            if (thorndenRetargeted)
            {
                return;
            }
            thorndenRetargeted = true;
            TSC_QuestSignals.Retarget("TSC_Act3_Thornden",
                ".site.AllEnemiesDefeated", ".TSC_CollectorsBeaten");
        }

        /// <summary>
        /// Save repair for Act 3 openers generated before the surface gate:
        /// underground generation could produce a Road Fire quest whose site
        /// never found a tile. If the rush has fired but the quest is absent
        /// or siteless, clear the wreck and queue a clean re-grant (the
        /// card-shown flag keeps the curtain from replaying).
        /// </summary>
        private void HealBrokenAct3()
        {
            if (act3Checked || act3Pending || !DialogueStateManager.Current.IsSet(Rush2Flag))
            {
                return;
            }
            // Once is enough. The generation bugs that produced siteless and
            // ocean-borne openers are fixed at the source (the nomad GetMap
            // patch and the party-rooted tile node), so this is insurance
            // against a save that already carries the damage - not a thing
            // worth re-deciding every sixty ticks for the rest of the game.
            act3Checked = true;
            QuestScriptDef script = DefDatabase<QuestScriptDef>.GetNamedSilentFail("TSC_Act3_RoadFire");
            if (script == null)
            {
                return;
            }
            Quest existing = null;
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.root != script)
                {
                    continue;
                }
                if (quest.State == QuestState.EndedSuccess)
                {
                    return; // the act opened and was played; nothing to heal
                }
                if (quest.State == QuestState.NotYetAccepted || quest.State == QuestState.Ongoing)
                {
                    existing = quest;
                    break;
                }
                // Ended failures (including our own scrapped wrecks) are
                // ignored: as far as the heal cares, no quest exists.
            }
            RimWorld.Planet.Site roadFire = null;
            SitePartDef sitePart = DefDatabase<SitePartDef>.GetNamedSilentFail("TSC_RoadFire");
            if (sitePart != null)
            {
                foreach (RimWorld.Planet.Site site in Find.WorldObjects.Sites)
                {
                    foreach (RimWorld.Planet.SitePart part in site.parts)
                    {
                        if (part.def == sitePart)
                        {
                            roadFire = site;
                            break;
                        }
                    }
                }
            }
            if (existing == null)
            {
                act3Pending = true; // rush fired, quest lost: re-open the act
                return;
            }
            if (existing.State != QuestState.Ongoing)
            {
                return;
            }
            // A site is only a site if a campfire could exist there. The
            // pre-fix wreck generation could fall back to a GARBAGE tile -
            // seen live as the fire on the road burning in open ocean - and
            // the first version of this heal accepted any site at all.
            bool misplaced = roadFire != null
                && (!roadFire.Tile.Valid
                    || Find.WorldGrid[roadFire.Tile]?.PrimaryBiome == null
                    || Find.WorldGrid[roadFire.Tile].PrimaryBiome.impassable);
            if (roadFire == null || misplaced)
            {
                Log.Warning(misplaced
                    ? "[The Shattered Crown] Act 3 opener's site landed on an impassable tile (pre-fix wreck); scrapping and re-granting."
                    : "[The Shattered Crown] Act 3 opener had no site (generated underground); re-granting cleanly.");
                if (roadFire != null)
                {
                    Find.WorldObjects.Remove(roadFire);
                }
                existing.hidden = true;
                existing.End(QuestEndOutcome.InvalidPreAcceptance, false);
                act3Pending = true;
            }
        }

        /// <summary>
        /// Act 2's title card is the curtain AFTER the epilogue scene: it
        /// waits for Oswin's bard lead to be TAKEN (TSC_Act2LeadTaken, set by
        /// his dialogue) and for that conversation window to close - shard,
        /// epilogue, quest, THEN the card. Fallback: if Oswin is dead and can
        /// never give the lead, the card fires once the fighting stops, so
        /// the act still turns on a save that lost its scholar.
        /// </summary>
        private void ShowPendingTitle()
        {
            if (!titlePending)
            {
                return;
            }
            if (DialogueStateManager.Current.IsSet(TitleFlag))
            {
                titlePending = false;
                return;
            }
            if (Find.WindowStack.WindowOfType<Dialog_Conversation>() != null)
            {
                return; // let the scene finish; the curtain follows the words
            }
            if (!DialogueStateManager.Current.IsSet("TSC_Act2LeadTaken"))
            {
                NamedNpcDef oswinDef = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Oswin");
                Pawn oswin = oswinDef != null ? DialogueStateManager.Current.GetNamedNpcIfExists(oswinDef) : null;
                bool oswinGone = oswinDef == null || (oswin != null && oswin.Dead);
                if (!oswinGone)
                {
                    return; // the lead is coming; hold the curtain
                }
                foreach (Map m in Find.Maps)
                {
                    if (m.mapPawns.FreeColonistsSpawnedCount > 0
                        && GenHostility.AnyHostileActiveThreatToPlayer(m))
                    {
                        return;
                    }
                }
            }
            titlePending = false;
            DialogueStateManager.Current.Set(TitleFlag);
            TSC_TitleCardManager.Show("Act 2", "The Sunken Cellars");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref lastCastTicks, "lastCastTicks", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref lastStormTick, "lastStormTick", -999999);
            Scribe_Values.Look(ref titlePending, "titlePending");
            Scribe_Values.Look(ref act3Pending, "act3Pending");
            Scribe_Values.Look(ref splitHealed, "splitHealed");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (lastCastTicks == null)
                {
                    lastCastTicks = new Dictionary<string, int>();
                }
                // Pre-split saves tracked only Shardfall, in lastStormTick.
                if (lastStormTick != -999999 && !lastCastTicks.ContainsKey("TSC_Ability_Shardfall"))
                {
                    lastCastTicks["TSC_Ability_Shardfall"] = lastStormTick;
                }
            }
        }
    }

    public class CompProperties_TSC_IceStorm : CompProperties_AbilityEffect
    {
        public float radius = 4.5f;
        public int damageAmount = 25;
        public float armorPenetration = 0.5f;

        public CompProperties_TSC_IceStorm()
        {
            compClass = typeof(CompAbilityEffect_TSC_IceStorm);
        }
    }

    public class CompAbilityEffect_TSC_IceStorm : CompAbilityEffect
    {
        public new CompProperties_TSC_IceStorm Props => (CompProperties_TSC_IceStorm)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster.MapHeld;
            if (map == null)
            {
                return;
            }
            DamageDef ice = DefDatabase<DamageDef>.GetNamedSilentFail("TSC_IceShard") ?? DamageDefOf.Cut;
            GenExplosion.DoExplosion(target.Cell, map, Props.radius, ice, caster,
                Props.damageAmount, Props.armorPenetration);
            // Winter falls with the shards.
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(target.Cell, Props.radius, useCenter: true))
            {
                if (cell.InBounds(map))
                {
                    map.snowGrid.AddDepth(cell, 0.4f);
                    if (Rand.Chance(0.35f))
                    {
                        FleckMaker.ThrowAirPuffUp(cell.ToVector3Shifted(), map);
                    }
                }
            }
            TSC_ShardTracker.Current?.NoteCast(parent.def);
        }
    }

    public class CompProperties_TSC_PartyHediff : CompProperties_AbilityEffect
    {
        public HediffDef hediff;

        public CompProperties_TSC_PartyHediff()
        {
            compClass = typeof(CompAbilityEffect_TSC_PartyHediff);
        }
    }

    /// <summary>
    /// Applies a hediff to the whole party at the caster's location: every
    /// free colonist on the caster's map AND its sibling pocket maps (same
    /// root), so a party split across dungeon floors is still one party -
    /// the same location rule the threat scaler uses. Re-cast refreshes by
    /// replacement, so durations never stack.
    /// </summary>
    public class CompAbilityEffect_TSC_PartyHediff : CompAbilityEffect
    {
        public new CompProperties_TSC_PartyHediff Props => (CompProperties_TSC_PartyHediff)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map casterMap = caster.MapHeld;
            if (casterMap == null || Props.hediff == null)
            {
                return;
            }
            Map root = TSC_Threat.RootMap(casterMap);
            foreach (Map map in Find.Maps)
            {
                if (TSC_Threat.RootMap(map) != root)
                {
                    continue;
                }
                foreach (Pawn pawn in new List<Pawn>(map.mapPawns.FreeColonistsSpawned))
                {
                    if (pawn.Dead || pawn.health?.hediffSet == null)
                    {
                        continue;
                    }
                    Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                    if (existing != null)
                    {
                        pawn.health.RemoveHediff(existing);
                    }
                    pawn.health.AddHediff(HediffMaker.MakeHediff(Props.hediff, pawn));
                }
            }
            TSC_ShardTracker.Current?.NoteCast(parent.def);
        }
    }
}
