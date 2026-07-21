using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The in-game map editor: build with dev/god mode, mark two corners, export
    /// the selection as PrefabDef XML into the mod's Exported/ folder, then
    /// spawn it back with the "spawn prefab" tool to verify the round trip.
    /// </summary>
    public static class TSC_MapEditor
    {
        private const string Category = "The Shattered Crown";

        private static IntVec3 cornerA = IntVec3.Invalid;
        private static IntVec3 cornerB = IntVec3.Invalid;
        private static int exportCounter;

        [DebugAction(Category, "Map editor: mark corner", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MarkCorner()
        {
            IntVec3 cell = UI.MouseCell();
            if (!cornerA.IsValid || cornerB.IsValid)
            {
                cornerA = cell;
                cornerB = IntVec3.Invalid;
                Messages.Message($"Corner A set: {cell}. Mark the opposite corner.", MessageTypeDefOf.NeutralEvent, historical: false);
            }
            else
            {
                cornerB = cell;
                CellRect rect = CellRect.FromLimits(cornerA, cornerB);
                Messages.Message($"Selection: {rect.Width}x{rect.Height} at {rect.Min}. Use 'export selection as prefab'.", MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        [DebugAction(Category, "Map editor: export selection as prefab", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ExportSelection()
        {
            if (!cornerA.IsValid || !cornerB.IsValid)
            {
                Messages.Message("Mark both corners first (Map editor: mark corner).", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            CellRect rect = CellRect.FromLimits(cornerA, cornerB);
            Map map = Find.CurrentMap;
            exportCounter++;
            string defName = $"TSC_Export_{Find.TickManager.TicksGame}_{exportCounter}";
            string xml = PrefabExporter.Export(rect, map, defName);

            ModContentPack pack = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "cfrolik.theshatteredcrown");
            string dir = pack != null ? Path.Combine(pack.RootDir, "Exported") : Path.Combine(GenFilePaths.SaveDataFolderPath, "TSC_Exported");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, defName + ".xml");
            File.WriteAllText(path, xml, Encoding.UTF8);

            cornerA = IntVec3.Invalid;
            cornerB = IntVec3.Invalid;
            Messages.Message($"Exported {rect.Width}x{rect.Height} prefab to {path}", MessageTypeDefOf.PositiveEvent, historical: false);
            Log.Message($"[The Shattered Crown] Prefab exported: {path}\nMove it under 1.6/Defs/ (and rename the defName) to ship it.");
        }

        [DebugAction(Category, "Map editor: spawn prefab...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnPrefabTool()
        {
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (PrefabDef def in DefDatabase<PrefabDef>.AllDefsListForReading.OrderBy(d => d.defName))
            {
                PrefabDef local = def;
                options.Add(new DebugMenuOption(local.defName, DebugMenuOptionMode.Tool, delegate
                {
                    PrefabUtility.SpawnPrefab(local, Find.CurrentMap, UI.MouseCell(), Rot4.North);
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction(Category, "Map editor: roof enclosed cells in selection", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RoofSelection()
        {
            if (!cornerA.IsValid || !cornerB.IsValid)
            {
                Messages.Message("Mark both corners first (Map editor: mark corner).", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Map map = Find.CurrentMap;
            CellRect rect = CellRect.FromLimits(cornerA, cornerB);
            int roofed = 0;
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                Room room = cell.GetRoom(map);
                if (room != null && !room.PsychologicallyOutdoors && !cell.Roofed(map))
                {
                    map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                    roofed++;
                }
            }
            Messages.Message($"Roofed {roofed} enclosed cells.", MessageTypeDefOf.PositiveEvent, historical: false);
        }
    }

    /// <summary>Serializes a rect of an existing map into PrefabDef XML.</summary>
    public static class PrefabExporter
    {
        public static string Export(CellRect rect, Map map, string defName)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
            sb.AppendLine("<Defs>");
            sb.AppendLine();
            sb.AppendLine($"  <!-- Exported in-game with The Shattered Crown map editor ({rect.Width}x{rect.Height}). -->");
            sb.AppendLine("  <PrefabDef>");
            sb.AppendLine($"    <defName>{defName}</defName>");
            sb.AppendLine($"    <size>({rect.Width},{rect.Height})</size>");
            AppendThings(sb, rect, map);
            AppendTerrain(sb, rect, map);
            sb.AppendLine("  </PrefabDef>");
            sb.AppendLine();
            sb.AppendLine("</Defs>");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- things

        private class ThingGroup
        {
            public ThingDef def;
            public ThingDef stuff;
            public Rot4 rot;
            public QualityCategory? quality;
            public int stackCount;
            public List<IntVec3> positions = new List<IntVec3>();
        }

        private static void AppendThings(StringBuilder sb, CellRect rect, Map map)
        {
            Dictionary<string, ThingGroup> groups = new Dictionary<string, ThingGroup>();
            HashSet<Thing> seen = new HashSet<Thing>();
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (!seen.Add(thing) || !ShouldExport(thing) || !rect.Contains(thing.Position))
                    {
                        continue;
                    }
                    QualityCategory? quality = null;
                    if (thing.TryGetComp<CompQuality>() != null)
                    {
                        quality = thing.TryGetComp<CompQuality>().Quality;
                    }
                    int stack = thing.def.stackLimit > 1 ? thing.stackCount : 0;
                    string key = $"{thing.def.defName}|{thing.Stuff?.defName}|{thing.Rotation.AsInt}|{quality}|{stack}";
                    if (!groups.TryGetValue(key, out ThingGroup group))
                    {
                        group = new ThingGroup
                        {
                            def = thing.def,
                            stuff = thing.Stuff,
                            rot = thing.Rotation,
                            quality = quality,
                            stackCount = stack,
                        };
                        groups[key] = group;
                    }
                    IntVec3 local = thing.Position - rect.Min;
                    group.positions.Add(new IntVec3(local.x, 0, local.z));
                }
            }

            if (groups.Count == 0)
            {
                return;
            }
            sb.AppendLine("    <things>");
            foreach (ThingGroup group in groups.Values.OrderBy(g => g.def.defName))
            {
                sb.AppendLine($"      <{group.def.defName}>");
                if (group.positions.Count == 1)
                {
                    sb.AppendLine($"        <position>({group.positions[0].x},0,{group.positions[0].z})</position>");
                }
                else
                {
                    sb.AppendLine("        <positions>");
                    foreach (IntVec3 pos in group.positions)
                    {
                        sb.AppendLine($"          <li>({pos.x},0,{pos.z})</li>");
                    }
                    sb.AppendLine("        </positions>");
                }
                if (group.stuff != null)
                {
                    sb.AppendLine($"        <stuff>{group.stuff.defName}</stuff>");
                }
                if (RotationMatters(group.def) && RelativeRotationFor(group.rot) != null)
                {
                    sb.AppendLine($"        <relativeRotation>{RelativeRotationFor(group.rot)}</relativeRotation>");
                }
                if (group.quality != null)
                {
                    sb.AppendLine($"        <quality>{group.quality}</quality>");
                }
                if (group.stackCount > 1)
                {
                    sb.AppendLine($"        <stackCountRange>{group.stackCount}~{group.stackCount}</stackCountRange>");
                }
                sb.AppendLine($"      </{group.def.defName}>");
            }
            sb.AppendLine("    </things>");
        }

        private static bool ShouldExport(Thing thing)
        {
            ThingDef def = thing.def;
            if (thing is Pawn || def.IsBlueprint || def.IsFrame || def.IsFilth || def.mote != null)
            {
                return false;
            }
            if (def.category == ThingCategory.Building)
            {
                return def.building == null || !def.building.isNaturalRock;
            }
            return def.category == ThingCategory.Item;
        }

        private static bool RotationMatters(ThingDef def)
        {
            return def.rotatable && def.category == ThingCategory.Building;
        }

        private static string RelativeRotationFor(Rot4 rot)
        {
            // Prefabs spawn facing North; a thing's absolute rotation becomes its
            // rotation relative to that.
            if (rot == Rot4.North) return null;
            if (rot == Rot4.East) return "Clockwise";
            if (rot == Rot4.South) return "Opposite";
            return "Counterclockwise";
        }

        // ---------------------------------------------------------------- terrain

        private static void AppendTerrain(StringBuilder sb, CellRect rect, Map map)
        {
            // Greedy rectangle decomposition per terrain def.
            int width = rect.Width;
            int height = rect.Height;
            TerrainDef[,] grid = new TerrainDef[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    IntVec3 cell = new IntVec3(rect.minX + x, 0, rect.minZ + z);
                    grid[x, z] = cell.InBounds(map) ? map.terrainGrid.TerrainAt(cell) : null;
                }
            }
            bool[,] used = new bool[width, height];
            Dictionary<TerrainDef, List<CellRect>> rects = new Dictionary<TerrainDef, List<CellRect>>();
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (used[x, z] || grid[x, z] == null)
                    {
                        continue;
                    }
                    TerrainDef terrain = grid[x, z];
                    int w = 1;
                    while (x + w < width && !used[x + w, z] && grid[x + w, z] == terrain)
                    {
                        w++;
                    }
                    int h = 1;
                    bool rowMatches = true;
                    while (z + h < height && rowMatches)
                    {
                        for (int i = 0; i < w; i++)
                        {
                            if (used[x + i, z + h] || grid[x + i, z + h] != terrain)
                            {
                                rowMatches = false;
                                break;
                            }
                        }
                        if (rowMatches)
                        {
                            h++;
                        }
                    }
                    for (int i = 0; i < w; i++)
                    {
                        for (int j = 0; j < h; j++)
                        {
                            used[x + i, z + j] = true;
                        }
                    }
                    if (!rects.TryGetValue(terrain, out List<CellRect> list))
                    {
                        list = new List<CellRect>();
                        rects[terrain] = list;
                    }
                    list.Add(new CellRect(x, z, w, h));
                }
            }

            if (rects.Count == 0)
            {
                return;
            }
            sb.AppendLine("    <terrain>");
            foreach (KeyValuePair<TerrainDef, List<CellRect>> pair in rects.OrderBy(p => p.Key.defName))
            {
                sb.AppendLine($"      <{pair.Key.defName}>");
                sb.AppendLine("        <rects>");
                foreach (CellRect r in pair.Value)
                {
                    sb.AppendLine($"          <li>({r.minX},{r.minZ},{r.Width},{r.Height})</li>");
                }
                sb.AppendLine("        </rects>");
                sb.AppendLine($"      </{pair.Key.defName}>");
            }
            sb.AppendLine("    </terrain>");
        }
    }
}
