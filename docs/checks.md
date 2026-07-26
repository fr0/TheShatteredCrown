# Proficiency Checks

Every check in The Shattered Crown, grouped by proficiency. Update this
file when adding checks (sources: `Dialogues/*.agd` `check`/`passive`
lines, `TSC_Interactions.cs`, `MapComponent_TSC_Perception.cs`).

## How checks work

- **Active checks**: d10 + the check's bonus vs DC. Dialogue checks use
  the PARTY'S BEST effective proficiency (points + `1 + classLevel/4`
  for class-trained); physical interactions use the ACTING pawn only.
  Roll details print to the message log / combat log.
- **Once per save**: non-retryable checks are keyed by
  `dialogue + proficiency + DC`, so the same check duplicated across
  branches shares ONE attempt - a failure can't be retried from
  another node.
- **Passive checks**: no die. `5 + best effective proficiency >= DC`,
  evaluated for option visibility (stable - the option doesn't flicker).
- **DC ladder**: 6 easy ("tutorial") / 7-8 standard / 9-10 hard /
  12+ heroic.

---

## Arcana

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 7 | Oswin (ettersnap lore, two entry points sharing one attempt): "Memory as residue: that's Bindery doctrine." | `TSC_OswinRespect` (unlocks his Wizard teaching), +25 XP, +5 affinity, a hint about the crown's nine shards | "Everyone's a scholar." Points for ambition, none for scholarship |
| 10 | The looting party at the survey gallery mouth: "Feel the air coming out of that hole. The magic below is unstable." | The crew wants no part of the hill: they panic off the map, no fight (`parley_flee`) | "I've heard better from fortune tellers": combat, immediately (`parley_hostile`) |

Note: Arcana 7 and Lore 7 are ALTERNATIVE routes to `TSC_OswinRespect`.

## Athletics

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 7 | Wilf's culvert (The Wet Plot quest; ACTIVE, **retryable**) | The culvert tears clear: quest complete, +5 affinity, `TSC_WetPlotStone` | Soaked, boots full, dignity elsewhere - try again |
| 7 | Harrowfield well, first descent (after the ledge is found): "Jump down to the ledge" - the DESCENDER'S own proficiency, not party best | Lands clean on the ledge; the descent is rigged for everyone (`TSC_WellOpened`) | Still gets down - with a broken bone in a random limb (tendable, ~1 week). Descent opens either way |

## Insight

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 8 (active) | Mara's too-quick "No." about the sanguophage crypt | Notice her broken weeding rhythm; pressing yields the Old Wick tip (`TSC_MaraCryptTip`, +25 XP; declining to press is +5 affinity) | Perfect rhythm; "possibly there was never anything at all" - gone forever |
| 8 (passive) | Maewyn's message node: "[Insight] This grove is lonely, Maewyn." option visibility | Opens her `lonely` branch | Option simply never shows |
| 8 (passive) | The body in the well caves, closer look: "[Insight] Whatever did this *grieved*." | The `grieved` node: the arrangement read as apology ("graves dug in anger... this one was dug in apology") | Option simply never shows |
| 8 (passive) | Old Wick's journal deflection (after reading the weathered journal): "[Insight] You didn't ask what it says, elder. You already know." | The confession: he wrote it, he is the sanguophage, meet him INSIDE the crypt (`TSC_WickCryptTold` + `TSC_WickConfessed`, +150 XP, +10 affinity) - bypasses the help-the-villagers gate | Option never shows; he stays cagey and the trust route stays the only way in |

## Investigation

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 7 | Mara's weed patch after identifying the moss (What Grows Back): "Dig at the roots first." | An orderly unmarked grave older than the village: `TSC_PatchGrave`, +25 XP, then burn-or-cutting choice | Roots, worms, and patience - burn option only |
| 6 | Bran at the border ruin: "The rubble tells a story. Rationed fires and a single bedroll." | He's genuinely surprised: `TSC_BranRead`, +25 XP, +5 affinity | "Good instinct, wrong ruin" (he's been burning whole logs) |

## Lore

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 7 | Oswin (gated `not TSC_OswinRespect`): "The Chronicle mentions grave-drinkers. Twice, in the burial cantos." | `TSC_OswinRespect`, +25 XP, +5 affinity ("I take back a full third of what I assumed about you") | You're thinking of the Lay of the Cold Hearth, and misquoting it |

## Nature

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 7 | Maewyn's door (gated `not TSC_MaewynBees`): "Your bees are wintering early." | She actually stops - warms her before the message ask, +25 XP | The bees sulk; so does she |
| 7 | Mara's weed patch (What Grows Back): "Kneel and read it properly." | It's remembrance moss on unbroken ground - unlocks the Investigation dig | "Weeds is weeds": burn option only, forever |
| 6 | Harrowfield well, first descent (alternative to the jump): "Climb down the vines" - descender's own proficiency | Same as the Athletics success (the vines hold) | Same as the Athletics failure (they mostly hold) |

## Perception

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 9 (passive) | Map traps (`MapComponent_TSC_Perception`): best qualified colonist within 9.9 cells with line of sight, once per trap | Message + camera ping: "X notices the trap ahead! (passive perception N vs 9)" | Silence. Informational only - trap behavior is never altered |
| 7 (HIDDEN d10 roll) | Harrowfield well: a colonist lingers within 5 cells (party-best Perception; `MapComponent_TSC_WellWatcher`) | Letter: the ledge and opening inside the shaft (`TSC_WellLedgeFound`) - unlocks the descent checks | SILENT. Retries automatically after a 24 in-game hour cooldown (persisted) |

## Persuasion

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 8 | Serra's "overdue" opening: "The guild doesn't doubt you. It sent one rider, not an auditor." | The frost breaks: +25 XP, +5 affinity | "Pretty words." You still get the story, colder |
| 8 | Haldor's stock branch (the haggle): "Guild work keeps these roads open. Militia rates?" | **15% off everything he sells** (`TSC_TraderDiscount_TSC_Villager_Haldor`), +5 affinity, +25 XP | Full price forever, "and that's the compliment" |

## Performance

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 7 | Any campfire, right-click: "[Performance] tell a tale" (ACTOR rolls) | Nearby colonists gain the inspired-verse hediff | The tale lands flat |
| 7 | Bryn's fence (One True Story quest; **retryable**, three deed-gated telling options share the roll) | Quest complete: +100 XP, +5 affinity with ALL TEN villagers | "The telling wants practice" - come back braver |

## Survival

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 7 | Serra's hub (gated `not TSC_SerraWeather`): "Storm's coming off the peaks by tomorrow night." | She adjusts her map pins: +25 XP, +5 affinity | "That's woodsmoke off the raiders' ridge, courier." Flag set either way |

## Thievery

| DC | Where | Success | Failure |
|----|-------|---------|---------|
| 8 | Maewyn's cellar hatch (ACTOR rolls; **one attempt EVER, whole party**) | Unlocks the cellar pocket map; if Maewyn is in the party this is the THEFT moment (-10 affinity, `TSC_MaewynCellarTheft`, confrontation dialogue) | "It will not be surprised twice." Locked forever |

## Legacy (dormant)

The mosskeeper's chest checks - **[Thievery 8]** pick it quietly (failure
woke dormant nests within 30 cells) and **[Athletics 7]** break it open
(always loud; failure retryable) - no longer occur: the chest was removed
and the moss lies loose on the barrow floor. The code paths remain in
`TSC_Interactions.cs` if a future container wants them.

## Currently unused proficiencies

None - every proficiency now has at least one live check. (Athletics
gained the culvert; Thievery remains thinnest at one.)
