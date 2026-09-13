#!/usr/bin/env python3
"""Consolidated view over the BA-FULL-001 registry and its wave files.

Resolves, for every clipId, the latest recorded attempt, its jobId, where the
frames live on disk and which source sprite it was generated from.
"""
import glob
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ORDER_DIR = os.path.join(ROOT, "Docs", "Art", "BossAnimations")
REVIEW_DIR = os.path.join(ROOT, "Assets", "ArtSource", "PixelLab", "Reviews", "BA-FULL-001")

DONE = ("approved", "approved_review")


def registry():
    with open(os.path.join(ORDER_DIR, "BA-FULL-001.json"), encoding="utf-8") as fh:
        return json.load(fh)["jobs"]


def wave_files():
    files = glob.glob(os.path.join(ORDER_DIR, "BA-FULL-001-wave-*.json"))
    return sorted(files, key=lambda p: int(re.search(r"wave-(\d+)", p).group(1)))


def wave_dir(num):
    """Frames for a wave live in Wave<NN> - with one recovered variant."""
    for name in ("Wave%02d" % num, "Wave%02d-Recovered" % num, "Wave%d" % num):
        p = os.path.join(REVIEW_DIR, name)
        if os.path.isdir(p):
            return p
    return None


def attempts():
    """clipId -> list of attempt records, oldest first."""
    out = {}
    for f in wave_files():
        with open(f, encoding="utf-8") as fh:
            d = json.load(fh)
        w = d["wave"]
        for j in d["jobs"]:
            cid = j.get("clipId")
            if not cid:
                # a few early waves only carry boss + clip fragments
                if j.get("boss") and j.get("clip"):
                    cid = "%s_%s" % (j["boss"], j["clip"])
                else:
                    cid = None
            rec = {
                "wave": w,
                "clipId": cid,
                "jobId": j.get("jobId"),
                "status": j.get("status"),
                "note": j.get("rejection") or j.get("review") or j.get("note") or "",
                "sourcePath": j.get("sourcePath"),
                "frameCount": j.get("frameCount") or j.get("frames"),
                "raw": j,
            }
            # early waves store frames under the clipId, later ones under the jobId
            wd = wave_dir(w)
            rec["framesDir"] = None
            if wd:
                for cand in (rec["jobId"], cid):
                    if cand and os.path.isdir(os.path.join(wd, cand)):
                        rec["framesDir"] = os.path.join(wd, cand)
                        break
            out.setdefault(cid, []).append(rec)
    return out


def state():
    """clipId -> {'required': regrow, 'best': record or None, 'latest': record}"""
    reg = {j["clipId"]: j for j in registry()}
    att = attempts()
    rows = {}
    for cid, spec in reg.items():
        a = att.get(cid, [])
        best = next((r for r in reversed(a) if r["status"] in DONE), None)
        rows[cid] = {
            "spec": spec,
            "attempts": a,
            "approved": best,
            "latest": a[-1] if a else None,
        }
    orphan = {c: v for c, v in att.items() if c not in reg}
    return rows, orphan


if __name__ == "__main__":
    rows, orphan = state()
    done = [c for c, v in rows.items() if v["approved"]]
    todo = [c for c, v in rows.items() if not v["approved"]]
    print("registry clips : %d" % len(rows))
    print("approved       : %d" % len(done))
    print("outstanding    : %d" % len(todo))
    print("orphan attempts: %d (clipIds not in the registry)" % len(orphan))
    if "--list" in sys.argv:
        for c in sorted(todo):
            l = rows[c]["latest"]
            print("  %-52s %s" % (c, (l["status"] + " w%d" % l["wave"]) if l else "never ordered"))
    if "--orphans" in sys.argv:
        for c, v in sorted(orphan.items()):
            print("  %-52s %s" % (c, [r["status"] for r in v]))
