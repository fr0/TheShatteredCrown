using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// Guild halls: the Wayfarers keep a factor in every friendly town. On
    /// a settlement map belonging to a non-hostile humanlike faction (in
    /// Adventure Mode), one guild factor spawns near the center and holds
    /// his post. Talking to him is the presentation layer over the same
    /// contract generator that feeds the quest tab: browse the board, hire
    /// on help, or haggle the guild's rates up once and for all.
    /// </summary>
    public class MapComponent_TSC_GuildFactor : MapComponent
    {
        private bool spawned;

        public MapComponent_TSC_GuildFactor(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (spawned || Find.TickManager.TicksGame % 250 != 0
                || !TSC_AdventureModeGate.Active)
            {
                return;
            }
            if (!(map.Parent is Settlement settlement) || settlement.Faction == null
                || settlement.Faction.def.hidden || !settlement.Faction.def.humanlikeFaction
                || settlement.Faction.HostileTo(Faction.OfPlayer))
            {
                return;
            }
            spawned = true;
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_GuildFactor");
            if (kind == null)
            {
                return;
            }
            Pawn factor = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, settlement.Faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            IntVec3 cell = CellFinder.StandableCellNear(map.Center, map, 12f);
            if (!cell.IsValid)
            {
                spawned = false;
                return;
            }
            GenSpawn.Spawn(factor, cell, map);
            LordMaker.MakeNewLord(factor.Faction, new LordJob_DefendPoint(cell), map, Gen.YieldSingle(factor));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref spawned, "spawned");
        }
    }

    /// <summary>DSL contract_board(): the factor unrolls the postings - the open offers, acceptable on the spot.</summary>
    public class DialogueEffect_TSC_ContractBoard : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            // An empty board restocks when someone asks in person: the
            // factor "finds something at the bottom of the satchel".
            bool any = false;
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.State == QuestState.NotYetAccepted && TSC_ContractManager.IsContract(quest.root))
                {
                    any = true;
                    break;
                }
            }
            if (!any)
            {
                Find.World.GetComponent<TSC_ContractManager>()?.TryGenerateNow();
            }
            Find.WindowStack.Add(new Window_TSC_ContractBoard(context.interactor));
        }
    }

    /// <summary>
    /// The postings, as a window: every un-taken contract with its time
    /// left and an Accept button. The same offers as the quest tab - this
    /// is presentation, not a second economy.
    /// </summary>
    public class Window_TSC_ContractBoard : Window
    {
        private readonly Pawn taker;
        private Vector2 scroll;

        public Window_TSC_ContractBoard(Pawn taker)
        {
            this.taker = taker;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(560f, 420f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "The guild board");
            Text.Font = GameFont.Small;
            List<Quest> offers = new List<Quest>();
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.State == QuestState.NotYetAccepted && TSC_ContractManager.IsContract(quest.root))
                {
                    offers.Add(quest);
                }
            }
            Rect body = new Rect(0f, 40f, inRect.width, inRect.height - 40f - CloseButSize.y - 8f);
            if (offers.Count == 0)
            {
                Widgets.Label(body, "\"Nothing posted today. The roads make more work than the guild can write down; check back tomorrow.\"");
                return;
            }
            float rowHeight = 78f;
            Rect view = new Rect(0f, 0f, body.width - 16f, offers.Count * rowHeight);
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            foreach (Quest quest in offers)
            {
                Rect row = new Rect(0f, y, view.width, rowHeight - 6f);
                Widgets.DrawBoxSolidWithOutline(row, new Color(0.13f, 0.12f, 0.10f), new Color(0.35f, 0.30f, 0.22f));
                Rect inner = row.ContractedBy(6f);
                Widgets.Label(new Rect(inner.x, inner.y, inner.width - 110f, 26f), quest.name);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.75f, 0.7f, 0.6f);
                string expiry = quest.TicksUntilExpiry > 0
                    ? $"Posted rates. Expires in {quest.TicksUntilExpiry.ToStringTicksToDays()}."
                    : "Posted rates.";
                Widgets.Label(new Rect(inner.x, inner.y + 26f, inner.width - 110f, 34f), expiry);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Rect btn = new Rect(inner.xMax - 100f, inner.y + (inner.height - 30f) / 2f, 100f, 30f);
                if (Widgets.ButtonText(btn, "Take it"))
                {
                    quest.Accept(taker);
                    Messages.Message($"Contract taken: {quest.name}", MessageTypeDefOf.PositiveEvent, historical: false);
                    SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                }
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }
    }

    /// <summary>
    /// DSL hire_offer(): the factor knows who in town wants guild work. A
    /// generated adventurer - random class already learned - offered at a
    /// flat rate, paid from the party's pooled silver on this map.
    /// </summary>
    public class DialogueEffect_TSC_HireOffer : DialogueEffect
    {
        public const int Price = 400;

        public override void Apply(DialogueContext context)
        {
            Pawn hirer = context.interactor;
            Map map = hirer?.Map;
            if (map == null)
            {
                return;
            }
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager");
            if (kind == null)
            {
                return;
            }
            Pawn recruit = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, null, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true));
            List<TSC_ClassDef> classes = DefDatabase<TSC_ClassDef>.AllDefsListForReading;
            TSC_ClassDef classDef = classes.Count > 0 ? classes.RandomElement() : null;
            if (classDef != null)
            {
                TSC_ProgressionManager.Current.LearnClass(recruit, classDef, announce: false);
            }
            string classLabel = classDef?.label ?? "sellsword";
            int silver = TSC_PartyItems.Count(map, ThingDefOf.Silver);
            string text = $"{recruit.LabelShortCap}, a {classLabel}, will ride under your charter for {Price} silver."
                + $"\n\nParty silver on hand: {silver}.";
            if (silver < Price)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(text + "\n\nYou cannot cover the rate."));
                return;
            }
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, delegate
            {
                if (TSC_PartyItems.Consume(map, ThingDefOf.Silver, Price) < Price)
                {
                    Messages.Message("The silver came up short at the counting.", MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                IntVec3 cell = CellFinder.StandableCellNear(hirer.Position, map, 5f);
                if (!cell.IsValid)
                {
                    cell = hirer.Position;
                }
                GenSpawn.Spawn(recruit, cell, map);
                recruit.SetFaction(Faction.OfPlayer);
                Find.LetterStack.ReceiveLetter(
                    $"{recruit.LabelShortCap} hires on",
                    $"{recruit.LabelShortCap} the {classLabel} takes the guild's rate and your orders. Whether they are worth the silver is now a field question.",
                    LetterDefOf.PositiveEvent, recruit);
            }));
        }
    }
}
