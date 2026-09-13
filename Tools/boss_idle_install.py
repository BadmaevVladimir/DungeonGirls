#!/usr/bin/env python3
"""Install approved boss Idle clips into Resources and wire the kits to them.

Copies the reviewed frames to Assets/Resources/CharacterAnimations/Boss_<key>/Idle/
as idle_N.png with a .meta cloned from the Warden frames (fresh guids), then sets
animationFolderKey on every kit phase that uses that phase sprite.

  python Tools/boss_idle_install.py            # dry run
  python Tools/boss_idle_install.py --apply
"""
import os
import re
import shutil
import sys
import uuid

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.stdout.reconfigure(encoding="utf-8")

import boss_anim_index as idx

ROOT = idx.ROOT
RES = os.path.join(ROOT, "Assets", "Resources", "CharacterAnimations")
KITS = os.path.join(ROOT, "Assets", "ScriptableObjects", "Bosses")
META_TEMPLATE = os.path.join(RES, "Boss_Warden_Phase1", "Idle", "idle_0.png.meta")

# clipId -> (folder key, [(kit name, phase index 1-based), ...])
# Several kits share one sprite: the three Mirror Twin class branches reuse the
# base twin art, and the Pit Lord's phases 2 and 3 are the same sprite.
INSTALL = {
    "Amalgam_P1_Idle":        ("Amalgam_Phase1",        [("Amalgam", 1)]),
    "Amalgam_P3_Idle":        ("Amalgam_Phase3",        [("Amalgam", 3)]),
    "AshInquisitor_P2_Idle":  ("AshInquisitor_Phase2",  [("AshInquisitor", 2)]),
    "BoneLeviathan_P2_Idle":  ("BoneLeviathan_Phase2",  [("BoneLeviathan", 2)]),
    "Broodmother_P1_Idle":    ("Broodmother_Phase1",    [("Broodmother", 1)]),
    "Broodmother_P2_Idle":    ("Broodmother_Phase2",    [("Broodmother", 2)]),
    "Butcher_P1_Idle":        ("Butcher_Phase1",        [("Butcher", 1)]),
    "Butcher_P2_Idle":        ("Butcher_Phase2",        [("Butcher", 2)]),
    "CandleKeeper_P1_Idle":   ("CandleKeeper_Phase1",   [("CandleKeeper", 1)]),
    "ClockworkTitan_P2_Idle": ("ClockworkTitan_Phase2", [("ClockworkTitan", 2)]),
    "DrunkGiant_P1_Idle":     ("DrunkGiant_Phase1",     [("DrunkGiant", 1)]),
    "DrunkGiant_P2_Idle":     ("DrunkGiant_Phase2",     [("DrunkGiant", 2)]),
    "DungeonHeart_P2_Idle":   ("DungeonHeart_Phase2",   [("DungeonHeart", 2)]),
    "FrostWraith_P1_Idle":    ("FrostWraith_Phase1",    [("FrostWraith", 1)]),
    "FrostWraith_P2_Idle":    ("FrostWraith_Phase2",    [("FrostWraith", 2)]),
    "GildedUsurer_P1_Idle":   ("GildedUsurer_Phase1",   [("GildedUsurer", 1)]),
    "GildedUsurer_P2_Idle":   ("GildedUsurer_Phase2",   [("GildedUsurer", 2)]),
    "Jailer_P1_Idle":         ("Jailer_Phase1",         [("Jailer", 1)]),
    "MirrorTwin_P1_Idle":     ("MirrorTwin_Phase1",     [("MirrorTwin", 1),
                                                         ("MirrorTwin_Warrior", 1),
                                                         ("MirrorTwin_Rogue", 1),
                                                         ("MirrorTwin_Barbarian", 1)]),
    "PitHound_P1_Idle":       ("PitHound_Phase1",       [("PitHound", 1)]),
    "PitLord_P2_Idle":        ("PitLord_Phase2",        [("PitLord", 2), ("PitLord", 3)]),
    "RelicEater_P1_Idle":     ("RelicEater_Phase1",     [("RelicEater", 1)]),
    "RelicEater_P2_Idle":     ("RelicEater_Phase2",     [("RelicEater", 2)]),
    "RustSmith_P1_Idle":      ("RustSmith_Phase1",      [("RustSmith", 1)]),
    "RustSmith_P2_Idle":      ("RustSmith_Phase2",      [("RustSmith", 2)]),
    "SporeMother_P2_Idle":    ("SporeMother_Phase2",    [("SporeMother", 2)]),
    "StoneIdol_P1_Idle":      ("StoneIdol_Phase1",      [("StoneIdol", 1)]),
    "StoneIdol_P2_Idle":      ("StoneIdol_Phase2",      [("StoneIdol", 2)]),
}


def new_guid():
    return uuid.uuid4().hex


def write_meta(png_path, template):
    txt = template
    txt = re.sub(r"^guid: [0-9a-f]{32}", "guid: " + new_guid(), txt, count=1, flags=re.M)
    txt = re.sub(r"spriteID: [0-9a-f]{32}", "spriteID: " + new_guid(), txt, count=1)
    with open(png_path + ".meta", "w", encoding="utf-8", newline="\n") as fh:
        fh.write(txt)


def set_phase_key(text, phase_index, key):
    """Set animationFolderKey on the Nth phase, inserting the field if absent."""
    phase_starts = [m.start() for m in re.finditer(r"\n  - phaseName:", text)]
    if phase_index > len(phase_starts):
        return text, "phase %d not found" % phase_index
    start = phase_starts[phase_index - 1]
    end = phase_starts[phase_index] if phase_index < len(phase_starts) else len(text)
    block = text[start:end]

    if re.search(r"\n    animationFolderKey:", block):
        new_block = re.sub(r"\n    animationFolderKey:[^\n]*",
                           "\n    animationFolderKey: " + key, block, count=1)
        note = "set"
    else:
        m = re.search(r"\n    phaseSprite:[^\n]*", block)
        if not m:
            return text, "no phaseSprite anchor"
        new_block = block[:m.end()] + "\n    animationFolderKey: " + key + block[m.end():]
        note = "inserted"
    return text[:start] + new_block + text[end:], note


def main():
    apply = "--apply" in sys.argv
    rows, _ = idx.state()
    template = open(META_TEMPLATE, encoding="utf-8").read()

    planned_keys = {}
    for clip, (key, targets) in sorted(INSTALL.items()):
        rec = rows.get(clip, {}).get("approved")
        if not rec or not rec["framesDir"]:
            print("!! %-24s нет принятых кадров" % clip)
            continue
        frames = sorted(n for n in os.listdir(rec["framesDir"])
                        if n.startswith("frame_") and n.endswith(".png"))
        dst = os.path.join(RES, "Boss_%s" % key, "Idle")
        print("%-24s -> Boss_%s/Idle  кадров=%d  фаз=%s"
              % (clip, key, len(frames), targets))
        planned_keys[key] = targets
        if not apply:
            continue
        os.makedirs(dst, exist_ok=True)
        for i, f in enumerate(frames):
            out = os.path.join(dst, "idle_%d.png" % i)
            shutil.copyfile(os.path.join(rec["framesDir"], f), out)
            write_meta(out, template)

    print("\n--- киты ---")
    by_kit = {}
    for key, targets in planned_keys.items():
        for kit, phase in targets:
            by_kit.setdefault(kit, []).append((phase, key))
    for kit, items in sorted(by_kit.items()):
        path = os.path.join(KITS, "BossKit_%s.asset" % kit)
        text = open(path, encoding="utf-8").read()
        notes = []
        for phase, key in sorted(items):
            text, note = set_phase_key(text, phase, key)
            notes.append("фаза%d=%s(%s)" % (phase, key, note))
        print("%-22s %s" % (kit, ", ".join(notes)))
        if apply:
            with open(path, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(text)

    print("\n%s" % ("ПРИМЕНЕНО" if apply else "ПРОБНЫЙ ПРОГОН — ничего не записано"))


if __name__ == "__main__":
    main()
