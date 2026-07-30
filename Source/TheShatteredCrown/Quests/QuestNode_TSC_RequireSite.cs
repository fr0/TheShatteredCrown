using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A tripwire, not a fix: site generation can fail SILENTLY (the tile
    /// node simply does not set its slate value and everything downstream
    /// shrugs), which shipped as "The Missing Rider has no map marker and
    /// no hyperlink" - an uncompletable contract with no evidence of why.
    /// This node stands right after site generation and names the failure
    /// in the log the moment it happens, with the quest and script named,
    /// so the NEXT silent failure is a one-line diagnosis instead of a
    /// bug report about hyperlinks.
    /// </summary>
    public class QuestNode_TSC_RequireSite : QuestNode
    {
        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }

        protected override void RunInt()
        {
            Site site = QuestGen.slate.Get<Site>("site");
            if (site == null)
            {
                Log.Error("[The Shattered Crown] "
                    + (QuestGen.quest?.root?.defName ?? "unknown script")
                    + ": quest generated with NO SITE. Dismiss it.");
                return;
            }
            // The real defect, learned the hard way: a failed tile search
            // leaves DEFAULT tile 0 - which passes Tile.Valid, spawns in
            // whatever ocean tile #0 occupies, and fooled every guard that
            // asked "valid?" instead of "usable?". So: repair, not report.
            // A site with a garbage tile gets a real one, found by plain
            // spiral from wherever the party is.
            if (Patch_TryFindNewSiteTile_NoWater.UsableSiteTile(site.Tile))
            {
                return;
            }
            PlanetTile anchor = Patch_TryFindNewSiteTile_NoWater.PartyAnchorTile();
            if (anchor.Valid && Patch_TryFindNewSiteTile_NoWater.TrySpiral(anchor, 3, 40, out PlanetTile repaired))
            {
                site.Tile = repaired;
                Log.Warning("[The Shattered Crown] "
                    + (QuestGen.quest?.root?.defName ?? "unknown script")
                    + $": site generated with an unusable tile; repaired to {repaired} near the party.");
                return;
            }
            Log.Error("[The Shattered Crown] "
                + (QuestGen.quest?.root?.defName ?? "unknown script")
                + ": site generated with an unusable tile and no repair "
                + "found. This quest will not be completable - dismiss it.");
        }
    }
}
