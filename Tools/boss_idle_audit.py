#!/usr/bin/env python3
"""Audit the state of every boss Idle clip: approved? frames on disk? still passes QA?
installed into Resources? referenced by a kit phase?
"""
import glob
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.stdout.reconfigure(encoding="utf-8")

import boss_anim_index as idx
import boss_anim_qa as qa

ROOT = idx.ROOT
KIT_DIR = os.path.join(ROOT, "Assets", "ScriptableObjects", "Bosses")
RES_DIR = os.path.join(ROOT, "Assets", "Resources", "CharacterAnimations")


def kit_phases():
    """(boss, phase index, animationFolderKey or '') for every phase of every kit."""
    out = []
    for f in sorted(glob.glob(os.path.join(KIT_DIR, "BossKit_*.asset"))):
        txt = open(f, encoding="utf-8", errors="replace").read()
        boss = os.path.basename(f)[len("BossKit_"):-len(".asset")]
        # read the key inside each phase block: a kit where only some phases
        # carry the field would otherwise shift every key onto the wrong phase
        starts = [m.start() for m in re.finditer(r"\n  - phaseName:", txt)]
        for i, st in enumerate(starts):
            end = starts[i + 1] if i + 1 < len(starts) else len(txt)
            m = re.search(r"\n    animationFolderKey:(.*)", txt[st:end])
            out.append((boss, i + 1, m.group(1).strip() if m else None))
    return out


def installed(key):
    if not key:
        return 0
    d = os.path.join(RES_DIR, "Boss_%s" % key, "Idle")
    if not os.path.isdir(d):
        return 0
    return len([n for n in os.listdir(d) if n.lower().endswith(".png")])


def main():
    rows, _ = idx.state()
    idles = {c: v for c, v in rows.items() if v["spec"].get("kind") == "Idle"}

    ready, broken, absent = [], [], []
    for cid, v in sorted(idles.items()):
        a = v["approved"]
        if not a:
            absent.append(cid)
            continue
        src = a.get("sourcePath") or v["spec"].get("sourcePath")
        p = os.path.join(ROOT, src.replace("\\", "/")) if src else None
        if not a["framesDir"] or not p or not os.path.exists(p):
            broken.append((cid, a["wave"]))
            continue
        rep = qa.analyse(a["framesDir"], p, cid)
        ready.append((cid, a["wave"], rep["autoVerdict"], len(rep["frames"]), rep["flags"]))

    print("Idle-клипов в реестре: %d" % len(idles))
    print("\nПРИНЯТЫ, кадры на диске, прогнаны через гейт: %d" % len(ready))
    for cid, w, verdict, n, fl in ready:
        note = ("  | " + "; ".join(fl))[:76] if fl else ""
        print("  %-26s w%-2d %-6s кадров=%d%s" % (cid, w, verdict, n, note))
    print("\nПРИНЯТЫ, но кадров или исходника нет: %d" % len(broken))
    for cid, w in broken:
        print("  %-26s w%d" % (cid, w))
    print("\nПРИНЯТОГО idle нет вообще: %d" % len(absent))
    for cid in absent:
        print("  %s" % cid)

    print("\n--- заведение в игру ---")
    phases = kit_phases()
    no_field = [(b, i) for b, i, k in phases if k is None]
    empty = [(b, i) for b, i, k in phases if k == ""]
    filled = [(b, i, k) for b, i, k in phases if k]
    print("фазовых состояний: %d" % len(phases))
    print("  ключ прописан : %d  %s" % (len(filled), [f"{b}P{i}={k}" for b, i, k in filled]))
    print("  ключ пустой   : %d" % len(empty))
    print("  поля нет вовсе: %d  %s" % (len(no_field), sorted({b for b, _ in no_field})))
    print("  кадров установлено в Resources: %d" % sum(installed(k) for _, _, k in filled))


if __name__ == "__main__":
    main()
