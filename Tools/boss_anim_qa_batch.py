#!/usr/bin/env python3
"""Run the automatic pre-QA over recorded attempts and print a compact table.

  python Tools/boss_anim_qa_batch.py --wave 32
  python Tools/boss_anim_qa_batch.py --clip Jailer_P1_Idle --clip MirrorTwin_P2_Idle
  python Tools/boss_anim_qa_batch.py --wave 32 --sheets <dir>
"""
import argparse
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")

import boss_anim_index as idx
import boss_anim_qa as qa

ROOT = idx.ROOT


def resolve_source(rec, spec):
    p = rec.get("sourcePath") or (spec or {}).get("sourcePath")
    if not p:
        return None
    full = os.path.join(ROOT, p.replace("\\", "/"))
    return full if os.path.exists(full) else None


def run(records, sheets_dir=None):
    rows = []
    for rec, spec in records:
        cid = rec["clipId"]
        if not rec["framesDir"]:
            rows.append((cid, rec, None, "no frames on disk"))
            continue
        src = resolve_source(rec, spec)
        if not src:
            rows.append((cid, rec, None, "source sprite not found"))
            continue
        rep = qa.analyse(rec["framesDir"], src, cid)
        if sheets_dir:
            os.makedirs(sheets_dir, exist_ok=True)
            out = os.path.join(sheets_dir, "%s.png" % cid.replace("/", "_"))
            qa.contact_sheet(rec["framesDir"], src, out)
            rep["sheet"] = out
        rows.append((cid, rec, rep, None))
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--wave", type=int, action="append", default=[])
    ap.add_argument("--clip", action="append", default=[])
    ap.add_argument("--sheets")
    ap.add_argument("--json")
    a = ap.parse_args()

    rows_state, orphan = idx.state()
    att = idx.attempts()
    picked = []
    for cid, recs in att.items():
        for r in recs:
            if a.wave and r["wave"] not in a.wave:
                continue
            if a.clip and cid not in a.clip:
                continue
            if not a.wave and not a.clip:
                continue
            spec = rows_state.get(cid, {}).get("spec")
            picked.append((r, spec))

    out = run(picked, a.sheets)
    payload = []
    for cid, rec, rep, err in out:
        if err:
            print("%-50s w%-2s  !! %s" % (cid, rec["wave"], err))
            continue
        s = rep["summary"]
        print("%-50s w%-2s  %-6s  detach=%-4d grow=%-4d newBright=%-2d motion=%.3f area=%.2f-%.2f endDiff=%.3f"
              % (cid, rec["wave"], rep["autoVerdict"], s["largestDetachedBlob"], s["largestGrowthBlob"],
                 s["maxNewBrightColourBuckets"], s["peakMotion"],
                 (s["areaRatioRange"] or [0, 0])[0], (s["areaRatioRange"] or [0, 0])[1],
                 s["lastFrameDiffVsSource"]))
        for fl in rep["flags"]:
            print("      - %s" % fl)
        rep["clipId"] = cid
        rep["wave"] = rec["wave"]
        rep["recordedStatus"] = rec["status"]
        rep["recordedNote"] = rec["note"]
        payload.append(rep)
    if a.json:
        with open(a.json, "w", encoding="utf-8") as fh:
            json.dump(payload, fh, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    main()
