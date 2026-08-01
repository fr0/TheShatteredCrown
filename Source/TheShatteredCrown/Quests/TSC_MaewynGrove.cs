using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Opens the grove scene when Maewyn gets home.
    ///
    /// No new site and no new building: the druid's grove is already a
    /// persistent world object (TSC_PersistentSiteExtension), so her quest
    /// points at a place the player can simply walk back to. This watches
    /// for the conditions that make the scene mean anything - her quest
    /// open, her standing on the map, somebody there to hear it - and fires
    /// once.
    ///
    /// Every map gets one of these; the site check is the gate.
    /// </summary>
    public class MapComponent_TSC_MaewynGrove : MapComponent
    {
        private const int Interval = 60;
        private bool checkedMap;
        private bool isGrove;
        private bool opened;

        public MapComponent_TSC_MaewynGrove(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (opened || Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            if (!checkedMap)
            {
                checkedMap = true;
                if (map.Parent is Site site)
                {
                    for (int i = 0; i < site.parts.Count; i++)
                    {
                        if (site.parts[i].def?.defName == "TSC_DruidGrove")
                        {
                            isGrove = true;
                            break;
                        }
                    }
                }
            }
            if (!isGrove)
            {
                opened = true;
                return;
            }
            DialogueStateManager state = DialogueStateManager.Current;
            if (!state.IsSet("TSC_MaewynGroveAsked")
                || state.IsSet("TSC_MaewynKeeper") || state.IsSet("TSC_MaewynHills"))
            {
                return;
            }
            if (Find.WindowStack.WindowOfType<Dialog_Conversation>() != null)
            {
                return;
            }
            Pawn maewyn = FindMaewyn();
            if (maewyn == null || maewyn.Downed)
            {
                return; // she has to be here; it is her grove
            }
            Pawn witness = null;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist != maewyn && !colonist.Downed)
                {
                    witness = colonist;
                    break;
                }
            }
            DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_MaewynGrove");
            if (witness == null || def == null)
            {
                return;
            }
            opened = true;
            Find.WindowStack.Add(new Dialog_Conversation(def, maewyn, witness));
        }

        private Pawn FindMaewyn()
        {
            NamedNpcDef def = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Maewyn");
            if (def == null)
            {
                return null;
            }
            Pawn maewyn = DialogueStateManager.Current.GetNamedNpcIfExists(def);
            return maewyn != null && maewyn.Spawned && maewyn.Map == map && !maewyn.Dead ? maewyn : null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref opened, "maewynGroveOpened");
        }
    }
}
