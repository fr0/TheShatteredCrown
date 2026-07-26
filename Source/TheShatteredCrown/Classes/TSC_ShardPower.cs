using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The crown shard's power. Any pawn CARRYING the shard in their
    /// inventory holds Shardfall: a once-a-day rain of ice shards (heavy
    /// AoE). The tracker polls holders (maps + caravans), grants/revokes
    /// the ability, fires the first-touch rush dialogue
    /// (Dialogues/shard_rush.agd), and follows it with the Act 2 title
    /// card. The cooldown belongs to the SHARD, not the pawn: it survives
    /// drop/re-pickup (lastStormTick).
    /// </summary>
    public class TSC_ShardTracker : WorldComponent
    {
        private const int ScanIntervalTicks = 60;
        public const string RushFlag = "TSC_ShardRushSeen";
        public const string TitleFlag = "TSC_Act2TitleShown";
        private int lastStormTick = -999999;
        private bool titlePending;

        public TSC_ShardTracker(World world) : base(world)
        {
        }

        public static TSC_ShardTracker Current => Find.World.GetComponent<TSC_ShardTracker>();

        public int CooldownRemaining => Mathf.Max(0, lastStormTick + GenDate.TicksPerDay - Find.TickManager.TicksGame);

        public void NoteStormCast()
        {
            lastStormTick = Find.TickManager.TicksGame;
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (Find.TickManager.TicksGame % ScanIntervalTicks != 0)
            {
                return;
            }
            ShowPendingTitle();
            ThingDef shardDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_CrownShard");
            AbilityDef stormDef = DefDatabase<AbilityDef>.GetNamedSilentFail("TSC_Ability_Shardfall");
            if (shardDef == null || stormDef == null)
            {
                return;
            }
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                bool holds = HoldsShard(pawn, shardDef);
                Ability existing = pawn.abilities?.GetAbility(stormDef);
                if (holds && existing == null && pawn.abilities != null)
                {
                    pawn.abilities.GainAbility(stormDef);
                    // The shard remembers its own exhaustion: re-pickup does
                    // not reset the day.
                    int remaining = CooldownRemaining;
                    if (remaining > 0)
                    {
                        pawn.abilities.GetAbility(stormDef)?.StartCooldown(remaining);
                    }
                    FirstTouch(pawn);
                }
                else if (!holds && existing != null)
                {
                    pawn.abilities.RemoveAbility(stormDef);
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
            Scribe_Values.Look(ref lastStormTick, "lastStormTick", -999999);
            Scribe_Values.Look(ref titlePending, "titlePending");
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
            TSC_ShardTracker.Current?.NoteStormCast();
        }
    }
}
