# Changelog

## [Unreleased]

### Fixed
* Reduced the frequency of exertion tracking during realtime mode (performance improvement)
* Don't rebuild pace label text every UI draw (performance)
* Don't rebuild AP label text every UI draw (performance)
* Fixed an infinite loading screen when loading a save during turn-based combat
* Fixed pawns appearing to stop short of a move order's destination
* Pawns no longer teleport slightly when their turn ends
* Enemies no longer freeze halfway through a melee swing
* Enemy turn beats and settle pauses stay the same real-time length at 2x/4x turn pace (they used to shrink with the speed setting)
* Time spent standing still mid-move (waiting for a door to open, finishing an attack cooldown, waiting on the pathfinder) is no longer billed as movement AP: the cost of a move now matches the path preview's quote
* Move orders now go to the exact clicked cell
* Pawns no longer give up mid-move when a frozen pawn blocks a corridor: moving pawns walk through frozen non-hostile pawns, XCOM-style (hostiles still block)
* A move that genuinely can't reach its destination now says "can't get through" instead of silently stopping
* The "out of action points; their turn is ending" message no longer appears for pawns that still have AP: it now distinguishes "out of action points" (empty pool) from "not enough AP for that action"
* Turn-based combat now starts when your own pawns open fire: engagement used to wait for the enemy to react

## [1.0.5] - 2026-08-11

### Fixed
* Fixed a potential crash when a melee attacker destroys its target with a one-shot

## [1.0.4] - 2026-08-10

### Added
* Turn-based speed controls added

## [1.0.3] - 2026-08-10

### Fixed
* Turn-based toggle setting will now be remembered

### Added
* Added an option to hide the turn-based banner when not in a fight

## [1.0.2] - 2026-08-09

### Fixed
* Removed the duplicate "Visit" option on the right-click menu for towns
* Compatibility with Large Faction Bases

## [1.0.1] - 2026-08-07

### Fixed
* Increased the resolution of the search circle textures
* Turn-based combat no longer starts with enemies still far away: an enemy merely intending to fight (targeted from across the map) does not engage until within 40 cells, unless they are already mid-attack.

## [1.0.0] - 2026-08-06

Initial public release.
