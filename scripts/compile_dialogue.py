#!/usr/bin/env python3
"""Compile .agd dialogue scripts into RimWorld DialogueDef XML.

Usage:
    py scripts/compile_dialogue.py              # compile all Dialogues/*.agd
    py scripts/compile_dialogue.py foo.agd ...  # compile specific files

Output goes to 1.6/Defs/Dialogues/<defName>.xml (one file per dialogue).
See docs/dialogue-dsl.md for the syntax reference.
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SOURCE_DIR = REPO / "Dialogues"
OUT_DIR = REPO / "1.6" / "Defs" / "Dialogues"

NS = "TheShatteredCrown"

# Friendly proficiency names usable in `check` lines and grant_prof effects.
PROFICIENCIES = {
    "Lore": "TSC_Prof_Lore",
    "Thievery": "TSC_Prof_Thievery",
    "Nature": "TSC_Prof_Nature",
    "Athletics": "TSC_Prof_Athletics",
    "Persuasion": "TSC_Prof_Persuasion",
    "Arcana": "TSC_Prof_Arcana",
    "Investigation": "TSC_Prof_Investigation",
    "Insight": "TSC_Prof_Insight",
    "Perception": "TSC_Prof_Perception",
    "Survival": "TSC_Prof_Survival",
    "Performance": "TSC_Prof_Performance",
}


# ---------------------------------------------------------------- model

@dataclass
class Condition:
    cls: str                      # C# class short name
    fields: dict = field(default_factory=dict)


@dataclass
class Effect:
    cls: str
    fields: dict = field(default_factory=dict)


@dataclass
class Check:
    proficiency: str = ""
    difficulty: int = 10
    success_link: str = ""
    fail_link: str = ""
    success_effects: list = field(default_factory=list)
    fail_effects: list = field(default_factory=list)
    retryable: bool = False


@dataclass
class Option:
    text: str = ""
    link_to: str = ""
    conditions: list = field(default_factory=list)
    effects: list = field(default_factory=list)
    check: Check | None = None


@dataclass
class Node:
    name: str = ""
    text_lines: list = field(default_factory=list)
    options: list = field(default_factory=list)


@dataclass
class Entry:
    node: str = ""
    conditions: list = field(default_factory=list)


@dataclass
class Dialogue:
    def_name: str = ""
    start_node: str = "start"
    entries: list = field(default_factory=list)
    nodes: list = field(default_factory=list)
    source: str = ""


# ---------------------------------------------------------------- parsing

class ParseError(Exception):
    def __init__(self, path, lineno, msg):
        super().__init__(f"{path}:{lineno}: {msg}")


def parse_conditions(expr: str, path, lineno) -> list:
    """Parse 'flag(X) and not flag(Y) and quest_active(Z)' into Conditions."""
    conditions = []
    for raw in re.split(r"\s+and\s+", expr.strip()):
        raw = raw.strip()
        negated = False
        if raw.startswith("not "):
            negated = True
            raw = raw[4:].strip()
        m = re.fullmatch(r"(\w+)\s*\(\s*([^)]*?)\s*\)", raw)
        if not m:
            raise ParseError(path, lineno, f"cannot parse condition '{raw}'")
        name, arg = m.group(1), m.group(2)
        if name == "flag":
            cls = "DialogueCondition_FlagNotSet" if negated else "DialogueCondition_FlagSet"
            conditions.append(Condition(cls, {"flag": arg}))
        elif name == "quest_active":
            if negated:
                raise ParseError(path, lineno, "'not quest_active(...)' is not supported (no such condition class yet)")
            conditions.append(Condition("DialogueCondition_QuestActive", {"quest": arg}))
        elif name == "quest_succeeded":
            if negated:
                raise ParseError(path, lineno, "'not quest_succeeded(...)' is not supported (no such condition class yet)")
            conditions.append(Condition("DialogueCondition_QuestSucceeded", {"quest": arg}))
        elif name == "in_party":
            if negated:
                raise ParseError(path, lineno, "'not in_party(...)' is not supported (no such condition class yet)")
            fields = {"npc": arg} if arg else {}
            conditions.append(Condition("DialogueCondition_InParty", fields))
        elif name == "dead":
            if not arg:
                raise ParseError(path, lineno, "dead syntax: dead(<NamedNpcDefName>)")
            cls = "DialogueCondition_NpcNotDead" if negated else "DialogueCondition_NpcDead"
            conditions.append(Condition(cls, {"npc": arg}))
        elif name == "nearby":
            if negated:
                raise ParseError(path, lineno, "'not nearby(...)' is not supported (no such condition class yet)")
            if not arg:
                raise ParseError(path, lineno, "nearby syntax: nearby(<NamedNpcDefName>)")
            conditions.append(Condition("DialogueCondition_Nearby", {"npc": arg}))
        elif name == "kind_on_map":
            if negated:
                raise ParseError(path, lineno, "'not kind_on_map(...)' is not supported (no such condition class yet)")
            if not arg:
                raise ParseError(path, lineno, "kind_on_map syntax: kind_on_map(<PawnKindDefName>)")
            conditions.append(Condition("DialogueCondition_KindOnMap", {"kind": arg}))
        elif name == "passive":
            if negated:
                raise ParseError(path, lineno, "'not passive(...)' is not supported")
            parts = [p.strip() for p in arg.split(",")]
            if len(parts) != 2 or parts[0] not in PROFICIENCIES:
                raise ParseError(path, lineno, "passive syntax: passive(<Proficiency>, <DC>)")
            conditions.append(Condition("DialogueCondition_Passive",
                                        {"proficiency": PROFICIENCIES[parts[0]], "difficulty": parts[1]}))
        elif name == "affinity":
            if negated:
                raise ParseError(path, lineno, "'not affinity(...)' is not supported; use the opposite comparison")
            parts = [p.strip() for p in arg.split(",")]
            npc = parts[0] if len(parts) == 2 else None
            if len(parts) > 2:
                raise ParseError(path, lineno, "affinity syntax: affinity([Npc,] >=N | <=N | >N | <N)")
            m = re.match(r"^(>=|<=|>|<)\s*(-?\d+)$", parts[-1])
            if not m:
                raise ParseError(path, lineno, "affinity syntax: affinity([Npc,] >=N | <=N | >N | <N)")
            op, num = m.group(1), int(m.group(2))
            fields = {}
            if npc:
                fields["npc"] = npc
            if op == ">=":
                fields["min"] = str(num)
            elif op == ">":
                fields["min"] = str(num + 1)
            elif op == "<=":
                fields["max"] = str(num)
            else:
                fields["max"] = str(num - 1)
            conditions.append(Condition("DialogueCondition_Affinity", fields))
        else:
            raise ParseError(path, lineno, f"unknown condition '{name}' (know: flag, quest_active, quest_succeeded, in_party, nearby, kind_on_map, dead, passive, affinity)")
    return conditions


def parse_effects(expr: str, path, lineno) -> list:
    """Parse 'flag(X); message(Some text)' into Effects (';'-separated)."""
    effects = []
    for raw in expr.split(";"):
        raw = raw.strip()
        if not raw:
            continue
        m = re.fullmatch(r"(\w+)\s*\(\s*(.*?)\s*\)", raw, re.DOTALL)
        if not m:
            raise ParseError(path, lineno, f"cannot parse effect '{raw}'")
        name, arg = m.group(1), m.group(2)
        if name == "flag":
            effects.append(Effect("DialogueEffect_SetFlag", {"flag": arg}))
        elif name == "unflag":
            effects.append(Effect("DialogueEffect_SetFlag", {"flag": arg, "value": "false"}))
        elif name == "signal":
            parts = [p.strip() for p in arg.split(",")]
            if len(parts) != 2:
                raise ParseError(path, lineno, "signal(...) needs exactly two args: signal(QuestDef, SignalName)")
            effects.append(Effect("DialogueEffect_QuestSignal", {"quest": parts[0], "signal": parts[1]}))
        elif name == "give_quest":
            effects.append(Effect("DialogueEffect_GiveQuest", {"quest": arg, "sendLetter": "true"}))
        elif name == "give_quest_silent":
            effects.append(Effect("DialogueEffect_GiveQuest", {"quest": arg, "sendLetter": "false"}))
        elif name == "message":
            effects.append(Effect("DialogueEffect_Message", {"text": arg}))
        elif name == "goodwill":
            effects.append(Effect("DialogueEffect_Goodwill", {"amount": arg}))
        elif name == "join_party":
            effects.append(Effect("DialogueEffect_JoinParty", {}))
        elif name == "grant_xp":
            effects.append(Effect("DialogueEffect_GrantXp", {"xp": arg}))
        elif name == "learn_class":
            effects.append(Effect("DialogueEffect_LearnClass", {"classDef": arg}))
        elif name == "teach_class":
            effects.append(Effect("DialogueEffect_LearnClass", {"classDef": arg, "teachNpc": "true"}))
        elif name == "grant_prof":
            parts = [p.strip() for p in arg.split(",")]
            prof = PROFICIENCIES.get(parts[0], parts[0])
            fields = {"proficiency": prof}
            if len(parts) > 1:
                fields["points"] = parts[1]
            effects.append(Effect("DialogueEffect_GrantProficiency", fields))
        elif name == "affinity":
            parts = [p.strip() for p in arg.split(",")]
            if len(parts) == 1:
                fields = {"amount": parts[0].lstrip("+")}
            elif len(parts) == 2:
                fields = {"npc": parts[0], "amount": parts[1].lstrip("+")}
            else:
                raise ParseError(path, lineno, "affinity syntax: affinity(+5) or affinity(<NamedNpcDefName>, +5)")
            effects.append(Effect("DialogueEffect_Affinity", fields))
        else:
            raise ParseError(path, lineno,
                             f"unknown effect '{name}' (know: flag, unflag, signal, give_quest, give_quest_silent, message, goodwill, join_party, grant_xp, learn_class, teach_class, grant_prof, affinity)")
    return effects


def resolve_link(target: str) -> str:
    """'end' (or empty) means 'no link' = the conversation ends."""
    target = target.strip()
    return "" if target in ("", "end") else target


def parse_file(path: Path) -> Dialogue:
    dlg = Dialogue(source=path.name)
    node: Node | None = None
    option: Option | None = None

    for lineno, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        line = raw.rstrip()
        stripped = line.strip()

        if stripped.startswith("#"):
            continue

        # --- section header:  == name ==
        m = re.fullmatch(r"==\s*(\w+)\s*==", stripped)
        if m:
            node = Node(name=m.group(1))
            dlg.nodes.append(node)
            option = None
            continue

        # --- top-level directives (before/between nodes)
        if node is None or not stripped or not (stripped.startswith("*") or option is not None):
            if stripped.startswith("dialogue "):
                dlg.def_name = stripped[len("dialogue "):].strip()
                continue
            if stripped.startswith("fallback "):
                dlg.start_node = stripped[len("fallback "):].strip()
                continue
            if stripped.startswith("entry "):
                rest = stripped[len("entry "):]
                if " if " in rest:
                    target, expr = rest.split(" if ", 1)
                    dlg.entries.append(Entry(target.strip(), parse_conditions(expr, path, lineno)))
                else:
                    dlg.entries.append(Entry(rest.strip(), []))
                continue

        if node is None:
            if stripped:
                raise ParseError(path, lineno, f"unexpected content before first '== node ==' section: '{stripped}'")
            continue

        # --- option line:  * text [-> target]
        if stripped.startswith("*"):
            body = stripped[1:].strip()
            if " -> " in body:
                text, target = body.rsplit(" -> ", 1)
                option = Option(text=text.strip(), link_to=resolve_link(target))
            else:
                option = Option(text=body, link_to="")
            node.options.append(option)
            continue

        # --- option sub-lines (indented under a '*')
        if option is not None and stripped:
            if stripped.startswith("if "):
                option.conditions.extend(parse_conditions(stripped[3:], path, lineno))
                continue
            if stripped.startswith("do "):
                option.effects.extend(parse_effects(stripped[3:], path, lineno))
                continue
            if stripped.startswith("check "):
                m = re.fullmatch(r"check\s+(\w+)\s+(\d+)\s*->\s*([^|]*)\|(.*)", stripped)
                if not m:
                    raise ParseError(path, lineno,
                                     "check syntax: check <SkillOrProficiency> <DC> -> <successNode> | <failNode> [retryable]  (use 'end' for no node)")
                name = m.group(1)
                if name not in PROFICIENCIES:
                    raise ParseError(path, lineno,
                                     f"checks use proficiencies only; '{name}' is not one of: {', '.join(sorted(PROFICIENCIES))}")
                fail_part = m.group(4).strip()
                retryable = False
                if fail_part.endswith("retryable"):
                    retryable = True
                    fail_part = fail_part[: -len("retryable")].strip()
                option.check = Check(
                    proficiency=PROFICIENCIES[name],
                    difficulty=int(m.group(2)),
                    success_link=resolve_link(m.group(3)),
                    fail_link=resolve_link(fail_part),
                    retryable=retryable,
                )
                continue
            if stripped.startswith("on success do "):
                if option.check is None:
                    raise ParseError(path, lineno, "'on success' before any 'check' line")
                option.check.success_effects.extend(parse_effects(stripped[len("on success do "):], path, lineno))
                continue
            if stripped.startswith("on fail do "):
                if option.check is None:
                    raise ParseError(path, lineno, "'on fail' before any 'check' line")
                option.check.fail_effects.extend(parse_effects(stripped[len("on fail do "):], path, lineno))
                continue
            raise ParseError(path, lineno, f"unrecognized line under option: '{stripped}'")

        # --- node body text (before the first option)
        if option is None:
            node.text_lines.append(line.strip())
            continue

    if not dlg.def_name:
        raise ParseError(path, 1, "missing 'dialogue <DefName>' directive")
    return dlg


# ---------------------------------------------------------------- validation

def validate(dlg: Dialogue, path: Path):
    names = [n.name for n in dlg.nodes]
    errors = []
    for name in names:
        if names.count(name) > 1:
            errors.append(f"duplicate node '{name}'")
    known = set(names)

    def check_link(link, where):
        if link and link not in known:
            errors.append(f"{where} links to unknown node '{link}'")

    if dlg.start_node not in known:
        errors.append(f"fallback start node '{dlg.start_node}' does not exist")
    for entry in dlg.entries:
        if entry.node not in known:
            errors.append(f"entry references unknown node '{entry.node}'")
    for node in dlg.nodes:
        if not node.options:
            errors.append(f"node '{node.name}' has no options (the window could not be dismissed)")
        for opt in node.options:
            check_link(opt.link_to, f"node '{node.name}' option '{opt.text[:30]}'")
            if opt.check:
                check_link(opt.check.success_link, f"node '{node.name}' check success")
                check_link(opt.check.fail_link, f"node '{node.name}' check fail")
    if errors:
        for e in sorted(set(errors)):
            print(f"  ERROR: {path.name}: {e}")
        raise SystemExit(1)


# ---------------------------------------------------------------- emission

def esc(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def node_text(lines: list) -> str:
    while lines and not lines[0]:
        lines.pop(0)
    while lines and not lines[-1]:
        lines.pop()
    return r"\n".join(lines)   # literal \n sequences; the window converts them


def emit_fields(out, fields: dict, indent: str):
    for key, value in fields.items():
        out.append(f"{indent}<{key}>{esc(value)}</{key}>")


def emit_condition_list(out, conditions, indent):
    out.append(f"{indent}<conditions>")
    for c in conditions:
        out.append(f'{indent}  <li Class="{NS}.{c.cls}">')
        emit_fields(out, c.fields, indent + "    ")
        out.append(f"{indent}  </li>")
    out.append(f"{indent}</conditions>")


def emit_effect_list(out, effects, tag, indent):
    out.append(f"{indent}<{tag}>")
    for e in effects:
        out.append(f'{indent}  <li Class="{NS}.{e.cls}">')
        emit_fields(out, e.fields, indent + "    ")
        out.append(f"{indent}  </li>")
    out.append(f"{indent}</{tag}>")


def emit(dlg: Dialogue) -> str:
    out = []
    out.append('<?xml version="1.0" encoding="utf-8" ?>')
    out.append("<Defs>")
    out.append("")
    out.append(f"  <!-- GENERATED from Dialogues/{dlg.source} - do not edit by hand.")
    out.append("       Rerun:  py scripts/compile_dialogue.py  -->")
    out.append(f"  <{NS}.DialogueDef>")
    out.append(f"    <defName>{dlg.def_name}</defName>")
    out.append(f"    <startNode>{dlg.start_node}</startNode>")
    if dlg.entries:
        out.append("    <starts>")
        for entry in dlg.entries:
            out.append("      <li>")
            out.append(f"        <node>{entry.node}</node>")
            if entry.conditions:
                emit_condition_list(out, entry.conditions, "        ")
            out.append("      </li>")
        out.append("    </starts>")
    out.append("    <nodes>")
    for node in dlg.nodes:
        out.append("      <li>")
        out.append(f"        <name>{node.name}</name>")
        out.append(f"        <text>{esc(node_text(node.text_lines))}</text>")
        out.append("        <options>")
        for opt_index, opt in enumerate(node.options):
            out.append("          <li>")
            out.append(f"            <text>{esc(opt.text)}</text>")
            if opt.link_to:
                out.append(f"            <linkTo>{opt.link_to}</linkTo>")
            if opt.conditions:
                emit_condition_list(out, opt.conditions, "            ")
            if opt.effects:
                emit_effect_list(out, opt.effects, "effects", "            ")
            if opt.check:
                out.append("            <check>")
                out.append(f"              <proficiency>{opt.check.proficiency}</proficiency>")
                out.append(f"              <difficulty>{opt.check.difficulty}</difficulty>")
                if not opt.check.retryable:
                    # Once per save: rolling sets this flag; the option hides after.
                    # Keyed by WHAT the check is (dialogue + proficiency + DC),
                    # not where it sits - the same check duplicated across
                    # branches shares one attempt, so a failure elsewhere in
                    # the tree cannot be retried from another node.
                    out.append(f"              <onceKey>TSC_Rolled_{dlg.def_name}_{opt.check.proficiency}_{opt.check.difficulty}</onceKey>")
                if opt.check.success_link:
                    out.append(f"              <successLink>{opt.check.success_link}</successLink>")
                if opt.check.fail_link:
                    out.append(f"              <failLink>{opt.check.fail_link}</failLink>")
                if opt.check.success_effects:
                    emit_effect_list(out, opt.check.success_effects, "successEffects", "              ")
                if opt.check.fail_effects:
                    emit_effect_list(out, opt.check.fail_effects, "failEffects", "              ")
                out.append("            </check>")
            out.append("          </li>")
        out.append("        </options>")
        out.append("      </li>")
    out.append("    </nodes>")
    out.append(f"  </{NS}.DialogueDef>")
    out.append("")
    out.append("</Defs>")
    return "\n".join(out) + "\n"


# ---------------------------------------------------------------- main

def main(argv):
    if len(argv) > 1:
        sources = [Path(a) if Path(a).is_absolute() else SOURCE_DIR / a for a in argv[1:]]
    else:
        sources = sorted(SOURCE_DIR.glob("*.agd"))
    if not sources:
        print(f"no .agd files found in {SOURCE_DIR}")
        return 1
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for src in sources:
        dlg = parse_file(src)
        validate(dlg, src)
        out_path = OUT_DIR / f"{dlg.def_name}.xml"
        out_path.write_text(emit(dlg), encoding="utf-8")
        n_nodes = len(dlg.nodes)
        n_opts = sum(len(n.options) for n in dlg.nodes)
        print(f"  {src.name} -> {out_path.relative_to(REPO)}  ({n_nodes} nodes, {n_opts} options)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
