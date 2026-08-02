# Classes and Spells

**Undrafted casting**: healing and cleansing spells (Healing Touch,
Cleanse, Nature's Balm, Lay on Hands, Field Dressing, Song of Rest,
Inner Calm) can be cast while undrafted - field medicine between
fights. Energy costs and cooldowns apply as normal. All other spells
require drafting.

Reference for all 11 companion classes in The Shattered Crown. Generated
from the defs (`TSC_Classes.xml`, `TSC_Abilities.xml`, `TSC_Books.xml`);
update this file when those change.

## How classes work

- **Learning a class**, two paths:
  - **Manuals** ("Study warden's manual" etc.; the mosskeepers' barrow
    always yields one random manual): studying UNLOCKS the class for
    the whole company - it appears as a "(new class)" choice in the
    Level up! dialog, where beginning it at level 1 costs a class
    level like any other assignment. The manual is consumed; a second
    copy of an already-studied manual is just paper.
  - **Mentors** teach directly (class at level 1, free, +25 XP):
    - Bran → **Warden** (after `TSC_BranLoyal`)
    - Serra → **Ranger**
    - Oswin → **Wizard** (after earning `TSC_OswinRespect`)
    - Maewyn → **Druid** (in-party road conversation)
- **XP and levels**: XP comes from quest completions and dialogue
  (checks, confidences), kills, and check spots. Level N → N+1 costs
  **400 x N XP** (400, 800, 1200...). Cumulative: level 5 at 4,000,
  level 8 at 11,200, **level 10 at 18,000**. The main chain pays about
  16,000 with act rewards scaling up (Act 1 x1.5 through Acts 4-5 x3),
  so a normal run finishes Act 5 around **level 10**, a rushed one at 9,
  a completionist at 11. Level-ups are banked and spent via the
  **Level up!** dialog; multiclass pawns assign levels per class.
- **Proficiencies**: each class trains three; trained proficiencies add
  `1 + classLevel/4` to d10 checks. Each level-up also awards a
  proficiency pick worth **+2 if it is one of that class's three**, +1
  otherwise, so a specialist funnelling every pick reaches 20+ by level
  10 while a generalist sits nearer 6. See `checks.md` for what that
  does to DCs.
- **Energy**: every spell costs Energy (10/15/25), which regenerates
  during sleep. The Bard's Song of Rest is the only in-field restore.
- **Turn-based mode**: a turn is **8 AP**, and every spell costs a flat **6 AP**. Ability gizmos
  show the AP price and wear a red bar when unaffordable.
- **Cast times**: the values below are the turn-based warmups. Outside
  turn-based mode every cast takes **2.5x** as long (real-time casting
  is interruptible like any warmup).
- **Spell scaling**: every spell's effect grows **+15% per level in the
  class that grants it** (level 5 = x1.6). MAGNITUDE scales, never
  duration: damage, healing, weapon-strike multipliers, buff/debuff
  stat strengths (e.g. a mark's +30% becomes +48%), Charged Shot's damage bonus
  (180% → 228%), and Song of Rest's energy restore. The tables below
  list level-1 baselines. Magic Missile instead uses its own steeper
  +2 damage/level curve. Durations, teleport ranges, Vanish/Stunning
  Palm (vanilla effects), Arcane Ward's charge count, and the binary
  cleanses never scale.
- **Cooldowns** are listed in in-game time (2,500 ticks = 1 hour;
  60,000 = 1 day). Combat spells cool down fast; healing and utility
  spells are deliberately slow.

Ranges: `self` = caster only (one-click cast, no targeting); `touch` =
adjacent (~1-4 cells). Combat buff/debuff durations are REAL seconds at
1x speed (a "60s" mark outlasts most fights but not the day); day-scale
utility buffs (Blessing, Inspiring Verse, Song of Rest, Aura of
Courage) are stated in in-game time. In turn-based mode frozen pawns
don't tick, so durations stretch across the round.

---

## Cleric

*A healer of the old faith: mends wounds, drives out sickness, and
steadies the party with blessings.*

**Proficiencies**: Lore, Insight, Persuasion

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Healing touch | 10 | 1.5s | 6 h | touch | Heals up to 25 injury, worst wounds first |
| 2 | Blessing | 10 | 1s | 1 day | 24 | Target gains +10% consciousness for 1 day |
| 4 | Cleanse | 10 | 2s | 1 day | touch | Removes disease, infection, food poisoning, toxic buildup |

## Bard

*A singer of the old songs: war-hymns for battle, verses for work,
dirges for the enemy, and rest-songs for camp. Nobody watches the hands
of a man who is playing.*

**Proficiencies**: Performance, Persuasion, Thievery

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Battle hymn | 15 | 1s | 30 min | 7 | Allies within 9.9 cells: +0.40 move, +8% melee hit for 2 min |
| 2 | Inspiring verse | 10 | 1s | 1 day | 24 | Target: +20% global work speed for 12 h |
| 3 | Dirge | 15 | 1.5s | 15 min | 24 | Enemies within 6.9 cells of point: -10% consciousness, -10% moving for 40s |
| 5 | Song of rest | 15 | 2s | 2 days | 7 | Allies within 9.9 cells: +25% rest rate, 50% faster healing for 12 h, +15 Energy each (not the singer) |

## Warden

*A soldier of the old watch: holds the line, shrugs off wounds, and
dares the enemy to come and try.*

**Proficiencies**: Athletics, Perception, Survival

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Charge | 10 | 0.25s | 30 min | self | Sprint at 5x normal move speed for 5 seconds |
| 2 | Stand fast | 10 | 0.5s | 30 min | 7 | Allies within 6.9 cells: 35% less damage taken for 40s (no move penalty) |
| 3 | Second wind | 15 | 0.5s | 12 h | self | Heals up to 15 of the caster's own injuries |
| 5 | Challenge | 15 | 1s | 15 min | 19 | Enemies within 6.9 cells of point: -15% consciousness, -15% moving, sharply reduced melee hit and shooting accuracy for 40s |

## Rogue

*Locks, shadows, and knives in that order. Rogues go where they aren't
invited and leave with what isn't theirs.*

**Proficiencies**: Thievery, Investigation, Perception

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Ambush | 15 | 0.5s | 30 min | touch | One perfect strike: 150% of normal weapon damage to an adjacent target, 300% with a knife or dagger |
| 2 | Shadowstep | 10 | 0.5s | 15 min | 20 | Teleports the caster to a visible cell |
| 3 | Marked for death | 10 | 0.5s | 15 min | 24 | Target takes +30% damage from all sources for 60s |
| 5 | Vanish | 15 | 0.5s | 1 h | self | Caster becomes invisible for 15 seconds |

## Wizard

*A student of the old sigils. What the ignorant call magic, the wizard
calls unfinished homework.*

**Proficiencies**: Arcana, Lore, Investigation

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Magic missile | 10 | 1s | 15 min | 24 | 8 + 2 per wizard level burn damage to one target, 30% armor penetration (10 at wizard 1, 18 at 5) |
| 2 | Arcane ward | 10 | 1s | 3 min | 24 | Target takes no damage from the next 3 attacks (periodic harm like fire burns through); lasts up to 30s |
| 3 | Leaden curse | 10 | 1s | 15 min | 24 | Target: half move speed, -20% manipulation for 45s |
| 5 | Blink | 15 | 1s | 15 min | 28 | Teleports the caster to a visible cell; brief stun on arrival |

The cleric, sorcerer, monk, and paladin capstones (all level 5):

| Class | Spell | Energy | Cast | Cooldown | Effect |
|-------|-------|--------|------|----------|--------|
| Cleric | Prayer of mending | 25 | 1.5s | 6 hr | Allies within 6.9 cells regen ~12 injury over 30s, worst wounds first |
| Sorcerer | Frost nova | 15 | 0.5s | 20 min | 10 frost damage to enemies within 4.9 cells of the caster + half speed for 20s; allies spared |
| Monk | Serpent stance | 15 | 0.25s | 20 min | Caster: +18 melee dodge, +0.3 move for 30s |
| Paladin | Smite | 15 | 0.5s | 15 min | 20 burn damage to one adjacent enemy, 50% armor penetration |

## Sorcerer

*Magic as a temperament. Where the wizard studies, the sorcerer simply
burns - and points the burning at whatever needs to stop existing.*

**Proficiencies**: Arcana, Persuasion, Insight

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Scorch | 10 | 1s | 15 min | 24 | 14 burn damage to one target, 30% armor penetration |
| 2 | Arc lightning | 15 | 1.5s | 15 min | 24 | 10 burn damage, 50% AP, to every ENEMY within 3.9 cells of the strike point (allies unharmed) |
| 4 | Firestorm | 25 | 2s | 1 h | 24 | Explosion: 20 flame damage in a 3.5-cell radius, starts fires. Hits allies AND enemies |

## Barbarian

*Fury as a fighting style. Hits harder, hurts less, and apologizes to
nobody.*

**Proficiencies**: Athletics, Survival, Nature

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Rage | 15 | 0.25s | 30 min | self | 60s: +30% melee damage, half pain, +0.3 move; shooting accuracy greatly reduced |
| 2 | War cry | 15 | 0.5s | 15 min | 19 | Enemies within 6.9 cells of point: -10% consciousness, -10% moving for 40s |
| 4 | Unbreakable | 25 | 0.25s | 1 h | self | 40s: only 10% of pain felt, 25% less damage taken |
| 5 | Whirlwind | 15 | 0.5s | 30 min | self | One turning blow: normal weapon damage to EVERY enemy within 1.9 cells |

## Druid

*The hills' own healer. Speaks for the green things, and occasionally
lets the green things speak back.*

**Proficiencies**: Nature, Insight, Perception

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Nature's balm | 15 | 1.5s | 6 h | touch | Heals up to 20 injury, worst wounds first |
| 2 | Bramble snare | 10 | 1s | 15 min | 24 | Roots the target: 85% slower movement for 30s |
| 4 | Barkskin | 15 | 1.5s | 30 min | 7 | Allies within 6.9 cells: +25% sharp / +15% blunt armor for 2 min |
| 6 | Summon bear | 30 | 2s | 2.5 min (real) | 5 | Summons a grizzly that fights at the druid's side for 60s: attacks nearby enemies, follows when idle, vanishes traceless (no corpse). One bear at a time; does not scale with level |

## Monk

*Discipline made flesh. The monk's weapon is the monk, kept sharp by
stillness.*

**Proficiencies**: Athletics, Insight, Perception

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Flurry | 10 | 0.25s | 30 min | self | 40s: melee attacks 40% faster, +10 melee dodge. In turn-based mode the haste cuts attack AP costs (unarmed 2 AP drops to 1: eight punches a round) |
| 2 | Inner calm | 10 | 2s | 1 day | touch | Removes disease, infection, food poisoning, toxic buildup |
| 4 | Stunning palm | 15 | 1s | 3 h | touch | Drops the touched target senseless with psychic shock (not animals) |

## Paladin

*An oath with legs. Heals the fallen, heartens the fearful, and brands
the wicked for what they are.*

**Proficiencies**: Athletics, Persuasion, Insight

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Lay on hands | 25 | 1.5s | 12 h | touch | Heals up to 35 injury, worst wounds first (biggest heal in the game) |
| 2 | Righteous brand | 10 | 1s | 15 min | 24 | Branded target's attacks falter: sharply reduced melee hit chance and shooting accuracy for 60s |
| 4 | Aura of courage | 15 | 1s | 1 day | 7 | Allies within 9.9 cells: -12% mental break threshold, +5% consciousness for 12 h |

## Ranger

*The road's own child: tracker, archer, and field surgeon of last
resort.*

**Proficiencies**: Nature, Survival, Perception

| Lvl | Spell | Energy | Cast | Cooldown | Range | Effect |
|----|--------|--------|------|----------|-------|--------|
| 1 | Charged shot | 10 | 0.5s | 30 min | self | Next 3 ranged attacks deal 180% damage (charges last up to 60s; misses spend charges) |
| 2 | Hunter's mark | 10 | 0.5s | 15 min | 29 | Target takes +30% damage from all sources for 60s |
| 3 | Pass without trace | 15 | 1.5s | 8.3 min | party | 10 min: the whole party at the location (all floors) is noticed at 40% less distance while sneaking. Does not hide anyone by itself - they must be sneaking |
| 4 | Swift quiver | 15 | 0.25s | 30 min | self | 60s: ranged attacks 35% faster, +4 shooting accuracy |

---

## Feats

Feats are the second half of a level-up. A pawn earns one at
**character level 3 and every third level after** (6, 9, 12...), and
because a class point also lands on every level from 2 on, a feat
level ALWAYS grants both - the level-up dialog is two pages, class
first, feat second.

- Feats are **permanent**: no respec.
- **General** feats have no requirement. **Class** feats require levels
  in one class and are listed first in the chooser, since they are the
  ones a given pawn is likeliest to want.
- Everything below works **identically in both combat modes**: feats
  that modify a spell do it through the ability comps both modes share
  (`TSC_FeatMods`), so nothing is turn-based-only unless it says so.

### General feats

| Feat | Effect |
|------|--------|
| Toughened | All incoming damage is reduced by 8%. |
| Fleet of foot | Moves 0.4 cells per second faster. In turn-based combat this means more ground covered per action point. |
| Hardy | Pain shock threshold raised by 0.15. |
| Armored | +10% sharp armor and +15% blunt armor, on top of worn armor. |
| Duelist | +4 melee hit chance and +5 melee dodge chance. |
| Marksman | Shooting accuracy improved by 12%. |
| Iron will | Mental break threshold lowered by 0.15. |
| Versatile | +1 to every proficiency check. |
| Deep reserves | +25 maximum spell energy, and energy regenerates 25% faster. |
| Camp doctor | +25% medical tend quality, and immunity is gained 20% faster. |
| Last stand | When this pawn would be downed, they stay standing for 5 more seconds instead. At most once every 10 seconds. |

### Warden feats

| Feat | Requires | Effect |
|------|----------|--------|
| Bulwark | Warden 2 | Stand Fast's radius increases from 6.9 to 10.4 cells. |
| Opportunity attacks | Warden 3 | When an enemy moves out of melee range, the warden automatically makes one melee attack against them. At most once every 10 seconds. |
| Unmoved | Warden 3 | Second Wind heals 67% more and removes one timed affliction. |
| Unignorable | Warden 5 | Enemies caught in Challenge turn to attack the warden. |

### Cleric feats

| Feat | Requires | Effect |
|------|----------|--------|
| Shared blessing | Cleric 2 | Blessing also applies to a second ally within 7 cells of the target, at full strength. |
| Warm hands | Cleric 3 | Healing Touch also stops all bleeding on the target. |
| Chaplain | Cleric 3 | Allies within 10 cells get -0.12 mental break threshold. |
| Purge | Cleric 4 | Cleanse's cooldown is reduced to a quarter. |

### Bard feats

| Feat | Requires | Effect |
|------|----------|--------|
| Carrying voice | Bard 2 | Battle Hymn, Dirge, and Song of Rest radius +25%. Stacks with instrument bonuses. |
| War song | Bard 2 | Allies under Battle Hymn also gain +8% shooting accuracy. |
| Lingering lament | Bard 3 | Dirge lasts 70 seconds instead of 40. |
| Cudgel chorus | Bard 3 | While an instrument is in hand: +35% melee damage and +20% melee hit chance. |
| Encore | Bard 5 | Song of Rest restores the bard's own energy along with the company's. |

### Rogue feats

| Feat | Requires | Effect |
|------|----------|--------|
| Backstab | Rogue 2 | Melee attacks deal 50% more damage against targets that are asleep, stunned, downed, fighting someone else, or attacked while the rogue is invisible. |
| Light fingers | Rogue 2 | +2 Thievery. Failed check-spot attempts never wake dormant creatures. |
| Killing window | Rogue 3 | A target that dies while Marked for Death refunds the mark's energy cost. |
| Ghost | Rogue 5 | Vanish's cooldown is reduced by 40%. |

### Wizard feats

| Feat | Requires | Effect |
|------|----------|--------|
| Focused ward | Wizard 2 | Arcane Ward absorbs one additional attack and lasts up to 45 seconds. |
| Heavy curse | Wizard 3 | Leaden Curse also reduces the target's shooting accuracy by 25%. |
| Efficient casting | Wizard 3 | Every spell costs 20% less energy. |
| Step twice | Wizard 5 | Blink's cooldown is reduced by half. |

### Sorcerer feats

| Feat | Requires | Effect |
|------|----------|--------|
| Forked lightning | Sorcerer 2 | Arc Lightning's radius increases from 3.9 to 5.4 cells, with +25% armor penetration. |
| Kindled | Sorcerer 3 | Scorch sets the target on fire. |
| Overchannel | Sorcerer 3 | Spells can be cast without enough energy; the shortfall is paid in burn damage. |
| Wildfire | Sorcerer 4 | Firestorm's radius increases from 3.5 to 4.6 cells. |

### Barbarian feats

| Feat | Requires | Effect |
|------|----------|--------|
| Bloodied fury | Barbarian 2 | Rage's strength increases with the barbarian's missing health when cast. |
| Reckless | Barbarian 3 | +15% melee damage, +10% damage taken. |
| Won't stay down | Barbarian 4 | Unbreakable also removes any active stun when cast. |
| Cleave | Barbarian 5 | Whirlwind's radius increases from 1.9 to 2.9 cells. |

### Druid feats

| Feat | Requires | Effect |
|------|----------|--------|
| Thorns | Druid 2 | Bramble Snare also deals 8 damage when applied. |
| Herbcraft | Druid 2 | Nature's Balm also heals the nearest other injured ally within 5 cells, at half strength. |
| Deep roots | Druid 4 | Barkskin also grants 10% less incoming damage. |
| Pack bond | Druid 6 | The summoned bear lasts 50% longer and takes 20% less damage. |

### Monk feats

| Feat | Requires | Effect |
|------|----------|--------|
| Running start | Monk 2 | In turn-based combat, attacking after moving costs 1 less action point. In real time, melee attacks within 5 seconds of moving are 20% faster. |
| Still mind | Monk 2 | Inner Calm also restores 15 energy. |
| Open hand | Monk 3 | Unarmed strikes deal 20% more damage. |
| Pressure point | Monk 4 | Stunning Palm lasts 50% longer. |

### Paladin feats

| Feat | Requires | Effect |
|------|----------|--------|
| Greater lay on hands | Paladin 2 | Lay On Hands heals 50% more. |
| Judgement | Paladin 3 | All player attacks deal 10% more damage against a Righteous Brand target. |
| Oathbound | Paladin 3 | The paladin gets -0.10 mental break threshold. Allies within 8 cells gain +10% sharp and blunt armor. |
| Unyielding aura | Paladin 4 | Aura of Courage's radius increases from 9.9 to 14.9 cells. |

### Ranger feats

| Feat | Requires | Effect |
|------|----------|--------|
| Called shot | Ranger 2 | Charged Shot grants one additional empowered shot. |
| Trailcraft | Ranger 2 | +2 Survival and +2 Nature. |
| Predator's focus | Ranger 3 | The ranger's attacks deal 15% more damage against a Hunted target. |
| Quickstring | Ranger 4 | Swift Quiver also grants +8% shooting accuracy. |

---

## Cross-class notes

- **The +30% damage marks** (Marked for Death, Hunter's Mark) are the
  same debuff by two names - they do not stack with each other on one
  target. (Righteous Brand used to be a third copy; it is now an
  attack-falter debuff instead.)
- **The debuff shouts** (Dirge, War Cry) share one effect at slightly
  different radii/ranges. Challenge is no longer one of them: as the
  Warden capstone it applies its own stronger debuff (-15%/-15% plus
  attack falter).
- **Weapon-scaled strikes** (Ambush 150%, or 300% with a short blade; Whirlwind 100% AoE) price off
  the caster's average weapon damage - better weapon, better spell.
- **Healer ladder** by throughput: Second Wind 15 self (12 h) →
  Nature's Balm 20 (6 h) → Healing Touch 25 (6 h) → Lay on Hands 35
  (12 h). (Field Dressing was replaced by Charged Shot; the Ranger no
  longer heals.)
- **Friendly-fire warning**: Firestorm is the only spell that damages
  allies (and can cook the ettersnap - it must be brought back alive).
  Arc Lightning is the safe AoE.
- **Enemy casters**: bandit hexers carry the Sorcerer kit (Scorch, Arc
  Lightning) and use it under the same energy/AP rules. Magic Missile
  is AI-castable too, so future enemy wizards can use it. Bandit
  **shamans** carry the Cleric kit and the AI aims it at their OWN side
  (`TSC_AiCastHint`): Healing Touch on the most-hurt ally, Blessing on
  whoever lacks one. Kill the shaman first.
- **Party-wide spells** (Pass Without Trace, and the crown shard's
  Quickening) apply to every colonist at the LOCATION, which includes
  sibling pocket maps - a party split across dungeon floors is still
  one party.
- **Stealth** (the Sneak toggle, not a spell) multiplies three factors
  into the distance at which enemies notice a pawn: **gear** (worn mass,
  x0.35 in leathers to x0.9 in full plate), **light** (x0.6 in darkness
  to x1.3 fully lit), and **Pass Without Trace** (x0.6). The product is
  clamped to 20-100%. Sneaking also halves move speed and halves the
  radius that trips turn-based mode; being seen up close, hit, or
  attacking ends it.
