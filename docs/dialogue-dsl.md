# Dialogue DSL (.agd) reference

Conversations are authored as plain-text `.agd` files in `Dialogues/` and
compiled to DialogueDef XML in `1.6/Defs/Dialogues/`:

```
py scripts/compile_dialogue.py                  # compile everything
py scripts/compile_dialogue.py guild_envoy.agd  # compile one file

# PowerShell front-end (same compiler):
powershell -ExecutionPolicy Bypass -File scripts\Compile-Dialogue.ps1
...\Compile-Dialogue.ps1 -Files guild_envoy.agd   # specific file(s)
...\Compile-Dialogue.ps1 -Watch                   # recompile on every save
```

The compiler validates the tree (duplicate nodes, dangling links, empty nodes)
and refuses to emit broken XML. **Never edit the generated XML by hand** - it is
overwritten on every compile.

## Dialogue tester (WPF app)

Walk any conversation outside the game:

```
powershell -ExecutionPolicy Bypass -File scripts\Run-DialogueTester.ps1
```

The tester (tools/DialogueTester) loads the COMPILED XML - the exact data the
game reads - and plays it with clickable options and a transcript. The right
panel simulates the world: flags (auto-discovered from the loaded trees),
quest states, party/nearby/dead toggles per named character, and party-best
proficiency values; checks roll a real d10 (or force success/failure).
"Show hidden options" reveals condition-gated options greyed out with the
reason. Effects apply to the simulated state and are logged, so give_quest /
join_party / flag chains can be followed across restarts: "Talk again"
re-resolves the entry conditions against the current state, simulating a
return visit. The "Recompile .agd + Reload" button runs the compiler and
reloads, closing the edit-test loop without launching RimWorld.

## File structure

```
# Comments start with '#'.
dialogue TSC_Dialogue_MyNpc            # required: the DialogueDef defName

entry other_node if flag(SomeFlag)    # optional conditional entry points,
entry another if quest_active(Q)      # tried in order; first match wins
# fallback other_default              # optional; default start node is "start"

== start ==                           # a node
Narration and speech, written naturally.

A blank line becomes a paragraph break in-game.

* "An option." -> next_node           # '-> target' links; no arrow = ends convo
* "Another." -> end                   # 'end' also ends the conversation

== next_node ==
...
```

## Options

Everything after `* ` up to a trailing ` -> target` is the option's literal
display text. Indented lines under an option add behavior:

```
* "Only shown sometimes, does things when picked." -> somewhere
    if not flag(AlreadyDidThis)
    if quest_succeeded(TSC_Quest1_WayfarersCall)
    do flag(DidThis); message(Something happened.)
* "A skill-checked option."
    check Social 15 -> win_node | lose_node
    on success do flag(Impressed)
    on fail do goodwill(-5)
```

- `if <cond> [and <cond> ...]` - all conditions must pass or the option is hidden.
  Multiple `if` lines AND together.
- `do <effect>[; <effect>...]` - applied when the option is chosen (before any check).
- `check <Proficiency> <DC> -> <successNode> | <failNode>` - d10 + the party's
  best effective proficiency vs DC. Use `end` for "conversation ends on this
  outcome". Checks are **once per save**: rolling one (pass OR fail) hides the
  option permanently - no re-roll fishing, no repeat rewards. The compiler
  enforces this via an auto-generated `onceKey` flag
  (`TSC_Rolled_<dialogue>_<node>_<index>`); append `retryable` after the fail
  node to opt out: `check Athletics 7 -> open | holds retryable`. Proficiencies only (vanilla skills contribute via synergy):
  **Lore, Thievery, Nature, Athletics, Persuasion, Arcana, Investigation,
  Insight, Perception, Survival, Performance**. Effective value = trained points
  + class training + related vanilla skill / 5. e.g. `check Lore 7 -> deep | shallow`.
  DC ladder: **6** easy, **8** standard, **10** hard, **12+** heroic
  (specialists only).
- `on success do ...` / `on fail do ...` - effects for each check outcome.

## Conditions

| DSL | Meaning |
|---|---|
| `flag(Name)` | persistent flag is set |
| `not flag(Name)` | persistent flag is not set |
| `quest_active(QuestScriptDefName)` | a quest from that script is ongoing/offered |
| `quest_succeeded(QuestScriptDefName)` | a quest from that script ended in success |
| `in_party()` | the NPC being talked to is in the player faction |
| `in_party(NamedNpcDefName)` | that named character is in the player faction |
| `nearby(NamedNpcDefName)` | that named character is spawned on this conversation's map within 20 tiles of the NPC - pair with `in_party(...)` for "your companion is standing right here" reactions |
| `dead(NamedNpcDefName)` / `not dead(...)` | that named character has died (destroyed-but-living, e.g. a despawned site, does NOT count) - use for delivering news of a death |
| `passive(Insight, 8)` | passive check: 5 + party's best effective proficiency ≥ DC (no die; stable visibility - use for noticing things) |

## Effects

| DSL | Meaning |
|---|---|
| `flag(Name)` | set a persistent flag |
| `unflag(Name)` | clear a persistent flag |
| `signal(QuestScriptDefName, SignalName)` | send a signal into the ongoing quest |
| `give_quest(QuestScriptDefName)` | grant a quest, with the "new quest" letter |
| `give_quest_silent(QuestScriptDefName)` | grant a quest, no letter |
| `message(any text here)` | top-left in-game message |
| `goodwill(-10)` | shift NPC faction goodwill |
| `join_party()` | the NPC being talked to joins the player faction (companion recruit) |
| `grant_xp(50)` | grants XP to the whole party (levels/class unlocks) |
| `learn_class(TSC_Class_Cleric)` | the TALKING COLONIST learns a class (level 1; banked levels are assigned via the Level up! dialog) |
| `teach_class(TSC_Class_Bard)` | the NPC being talked to learns a class |
| `grant_prof(Lore, 1)` | the talking colonist gains proficiency points (friendly names or defNames) |

## Text substitutions (at display time)

- `{PLAYER}` - the talking colonist's short name
- `{NPC}` - the NPC's short name
- The flag `TSC_Talked_<defName>` is set automatically the first time a
  conversation opens - use it for "have we met" entry conditions.

## NPC-initiated conversations

Companions can start conversations themselves. Author the conversation as a
normal `.agd`, then declare an *initiation* on the pawn kind's extension:

```xml
<li Class="TheShatteredCrown.DialogueExtension">
  <dialogue>TSC_Dialogue_Bram</dialogue>          <!-- right-click "Talk to" tree -->
  <initiations>
    <li>
      <dialogue>TSC_Dialogue_Bram_CampTalk</dialogue>
      <mtbDays>1</mtbDays>                        <!-- mean time between fires -->
      <once>true</once>                           <!-- fire once per save -->
      <conditions>                                <!-- same condition classes as options -->
        <li Class="TheShatteredCrown.DialogueCondition_InParty" />
      </conditions>
    </li>
  </initiations>
</li>
```

Checked hourly while the pawn is a free colonist on ANY calm map: home,
quest site, encampment (skipped while drafted, downed, asleep, in a mental
state, or during threats), and ALSO during **caravan night rest** - this
scenario lives on the road, so fireside talks are the common case. On a map,
the pawn walks to the **protagonist** - the starting pawn - or, if they are
dead, to the nearest living colonist, and the window opens on arrival;
interrupted walks retry later, and `once` is only consumed when the
conversation actually opens. In a resting caravan there is no walk: the
window simply opens (one initiation per rest check), and party-assist checks
use the caravan's members as the party.

## Adding a new conversation to an NPC

1. Write `Dialogues/my_npc.agd` with `dialogue TSC_Dialogue_MyNpc`.
2. Compile.
3. Point a PawnKindDef at it via the mod extension:
   ```xml
   <modExtensions>
     <li Class="TheShatteredCrown.DialogueExtension">
       <dialogue>TSC_Dialogue_MyNpc</dialogue>
     </li>
   </modExtensions>
   ```
