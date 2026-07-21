# In-game map editor

RimWorld's dev mode is the editing surface (god-mode instant building, terrain
painting via debug tools, thing spawning). Our tools add the missing round trip:
**select → export as prefab XML → respawn anywhere**.

All tools live in the debug actions menu under **The Shattered Crown**
(dev mode on → stack-of-lines icon in the top bar).

## Workflow

1. **Build** the structure in-game: god mode + instant construction for walls,
   floors, doors, furniture. Set quality/materials as you build - both are
   captured.
2. **Mark the selection**: `Map editor: mark corner`, click one corner, click
   the opposite corner. A message confirms the rect size.
3. **Export**: `Map editor: export selection as prefab`. The XML lands in
   `Exported/TSC_Export_<tick>_<n>.xml` in the repo (via the mod junction) with
   the def already valid. A message shows the full path.
4. **Ship it**: move the file under `1.6/Defs/`, rename the defName to something
   meaningful, restart the game.
5. **Verify / reuse**: `Map editor: spawn prefab...` lists every PrefabDef -
   pick one and click a cell to stamp it. Works for round-trip checks and for
   quickly kitbashing larger scenes out of exported pieces.
6. **Roofs**: prefab XML has no roof channel, so after spawning use
   `Map editor: roof enclosed cells in selection` (or let a C# genstep roof
   enclosed rooms - GenStep_TSC_Village candidates for the same pass).

## What gets captured

- **Terrain**: every cell, compressed into per-def rects (greedy decomposition).
- **Things**: buildings and items with material (stuff), rotation, quality, and
  stack counts, grouped compactly. Multi-entries of the same def are fine.
- **Skipped**: pawns, filth, blueprints/frames, motes, natural rock (chunks of
  mountain), plants. Roofs (see above).

## Notes

- Exports include *all* terrain in the rect - including the soil under things.
  Prune the soil rects from the XML if you want the prefab to sit on whatever
  ground it spawns onto.
- The spawn tool places relative to PrefabUtility's anchor; arrangement fidelity
  is exact either way since export and spawn share the same coordinate space.
- Big selections work (whole-base scale), but consider exporting rooms/buildings
  as separate prefabs and composing them in a GenStep - smaller pieces are
  reusable across maps.
- For fully bespoke MAPS (no procedural terrain at all), pair exported prefabs
  with a custom MapGeneratorDef whose only genstep stamps them -
  Anomaly's labyrinth (`LabyrinthMapGenerator.xml`) is the vanilla precedent,
  including pocket maps for underground dungeon floors.
