#!/usr/bin/env python3
"""Pre-QA for combat VFX overlay sprites.

An overlay is drawn on top of a combatant, so it has to stay out of the way:
open in the middle, sparse overall, greyscale (colour comes from the code
tint), and matched to the character's pixel density.

  python Tools/vfx_overlay_qa.py <sprite.png> [--frame 518] [--fraction 0.7]
"""
import argparse
import json
import sys

from PIL import Image

ALPHA_ON = 16
CHAR_CANVAS = 128  # boss sprites are 128px on a 518px stage frame


# A one-frame impact may cover the torso - it is gone in 0.2s and the flash is
# the point. An overlay that is held (shield, poison, aura) must not, or the
# player loses sight of the boss for as long as it lasts.
KINDS = {
    "momentary": {"solidCore": 0.90, "coreCoverage": 0.95, "coverage": 0.50},
    "sustained": {"solidCore": 0.20, "coreCoverage": 0.40, "coverage": 0.30},
}


def analyse(path, frame=518, fraction=0.7, char_canvas=CHAR_CANVAS, kind="sustained"):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    p = im.load()

    opaque = [(x, y) for y in range(h) for x in range(w) if p[x, y][3] > ALPHA_ON]
    if not opaque:
        return {"error": "sprite is fully transparent"}

    coverage = len(opaque) / float(w * h)

    # the middle of the overlay sits over the face and torso - keep it clear
    x0, x1 = int(w * 0.3), int(w * 0.7)
    y0, y1 = int(h * 0.3), int(h * 0.7)
    core = [(x, y) for x, y in opaque if x0 <= x < x1 and y0 <= y < y1]
    core_coverage = len(core) / float((x1 - x0) * (y1 - y0))

    # opaque (not just present) pixels in the core are what actually hides art
    solid_core = sum(1 for x, y in core if p[x, y][3] > 200)
    solid_core_coverage = solid_core / float((x1 - x0) * (y1 - y0))

    sat = max(max(p[x, y][:3]) - min(p[x, y][:3]) for x, y in opaque)

    overlay_px = (frame * fraction) / float(w)
    char_px = frame / float(char_canvas)

    rep = {
        "sprite": path,
        "size": [w, h],
        "coverage": round(coverage, 3),
        "coreCoverage": round(core_coverage, 3),
        "solidCoreCoverage": round(solid_core_coverage, 3),
        "maxSaturation": sat,
        "overlayPixelOnScreen": round(overlay_px, 2),
        "characterPixelOnScreen": round(char_px, 2),
        "pixelDensityRatio": round(overlay_px / char_px, 2),
        "flags": [],
    }
    lim = KINDS[kind]
    rep["kind"] = kind
    f = rep["flags"]
    if solid_core_coverage > lim["solidCore"]:
        f.append("HIDES_CHARACTER: %.0f%% of the centre is opaque (%s allows %.0f%%)"
                 % (solid_core_coverage * 100, kind, lim["solidCore"] * 100))
    elif core_coverage > lim["coreCoverage"]:
        f.append("BUSY_CENTRE: %.0f%% of the centre is covered" % (core_coverage * 100))
    if coverage > lim["coverage"]:
        f.append("TOO_DENSE: covers %.0f%% of the canvas" % (coverage * 100))
    if sat > 24:
        f.append("NOT_GREYSCALE: saturation %d - the code tint will fight it" % sat)
    ratio = rep["pixelDensityRatio"]
    if ratio > 1.15:
        f.append("TOO_COARSE: overlay pixels are %.2fx the character's" % ratio)
    elif ratio < 0.7:
        f.append("TOO_FINE: overlay pixels are %.2fx the character's" % ratio)

    rep["autoVerdict"] = "REJECT" if f else "PASS"
    return rep


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("sprites", nargs="+")
    ap.add_argument("--frame", type=int, default=518)
    ap.add_argument("--fraction", type=float, default=0.7)
    ap.add_argument("--kind", choices=sorted(KINDS), default="sustained")
    a = ap.parse_args()
    for s in a.sprites:
        rep = analyse(s, a.frame, a.fraction, kind=a.kind)
        if "error" in rep:
            print("%-40s !! %s" % (s, rep["error"]))
            continue
        print("%-40s %-6s cover=%.2f core=%.2f solid=%.2f sat=%-3d density=%.2f"
              % (s, rep["autoVerdict"], rep["coverage"], rep["coreCoverage"],
                 rep["solidCoreCoverage"], rep["maxSaturation"], rep["pixelDensityRatio"]))
        for fl in rep["flags"]:
            print("      - %s" % fl)


if __name__ == "__main__":
    sys.exit(main())
