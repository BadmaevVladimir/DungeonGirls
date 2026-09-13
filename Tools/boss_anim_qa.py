#!/usr/bin/env python3
"""Automatic pre-QA for PixelLab boss animation clips.

Checks the failure modes that dominated the BA-FULL-001 rejection log:
  * detached particles / bursts baked into the sprite
  * new colours outside the source palette (glow, fire, magic trails)
  * silhouette or identity drift (head/weapon morphing, area blow-up)
  * motion too subtle to read as an action
  * character sliding off its planted position
  * last frame not returning to the source pose

Usage:
  python Tools/boss_anim_qa.py <frames_dir> <source_png> [--sheet out.png] [--json]
"""
import argparse
import json
import os
import sys
from collections import deque

from PIL import Image

ALPHA_ON = 32  # alpha at or above this counts as solid pixel


def load_frames(frames_dir):
    names = sorted(
        n for n in os.listdir(frames_dir)
        if n.startswith("frame_") and n.lower().endswith(".png")
    )
    return [(n, Image.open(os.path.join(frames_dir, n)).convert("RGBA")) for n in names]


def mask(img):
    a = img.getchannel("A").load()
    w, h = img.size
    return [[a[x, y] >= ALPHA_ON for x in range(w)] for y in range(h)]


def strip_background(img, tol=12):
    """Some source sprites ship with an opaque backdrop - plain white, or a
    baked-in transparency checkerboard. Flood the border colours away so the
    alpha mask really is the silhouette; an already-transparent sprite comes
    back untouched.
    """
    px = img.load()
    w, h = img.size
    border = [px[x, 0] for x in range(w)] + [px[x, h - 1] for x in range(w)]         + [px[0, y] for y in range(h)] + [px[w - 1, y] for y in range(h)]
    opaque = [c for c in border if c[3] >= ALPHA_ON]
    if len(opaque) < len(border) * 0.8:
        return img  # already has a transparent frame around the character
    counts = {}
    for c in opaque:
        counts[c[:3]] = counts.get(c[:3], 0) + 1
    seeds = [c for c, n in counts.items() if n >= len(opaque) * 0.02]
    out = img.copy()
    o = out.load()
    seen = [[False] * w for _ in range(h)]
    q = deque()
    for x in range(w):
        q.append((x, 0)); q.append((x, h - 1))
    for y in range(h):
        q.append((0, y)); q.append((w - 1, y))
    while q:
        x, y = q.popleft()
        if not (0 <= x < w and 0 <= y < h) or seen[y][x]:
            continue
        seen[y][x] = True
        r, g, b, a = o[x, y]
        if a >= ALPHA_ON:
            if not any(max(abs(r - s[0]), abs(g - s[1]), abs(b - s[2])) <= tol for s in seeds):
                continue
            o[x, y] = (r, g, b, 0)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            q.append((x + dx, y + dy))
    return out


def dilate(m, r=2):
    h, w = len(m), len(m[0])
    out = [[False] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            if not m[y][x]:
                continue
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < h and 0 <= nx < w:
                        out[ny][nx] = True
    return out


def components(m):
    h, w = len(m), len(m[0])
    seen = [[False] * w for _ in range(h)]
    out = []
    for sy in range(h):
        for sx in range(w):
            if not m[sy][sx] or seen[sy][sx]:
                continue
            q = deque([(sx, sy)])
            seen[sy][sx] = True
            cells = []
            while q:
                x, y = q.popleft()
                cells.append((x, y))
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and m[ny][nx] and not seen[ny][nx]:
                        seen[ny][nx] = True
                        q.append((nx, ny))
            out.append(cells)
    out.sort(key=len, reverse=True)
    return out


def palette(img, tol_bucket=16):
    """Coarse colour buckets present in an image (ignores transparent pixels)."""
    px = img.load()
    w, h = img.size
    s = set()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a >= ALPHA_ON:
                s.add((r // tol_bucket, g // tol_bucket, b // tol_bucket))
    return s


def colour_set(img):
    px = img.load()
    w, h = img.size
    return {px[x, y][:3] for y in range(h) for x in range(w) if px[x, y][3] >= ALPHA_ON}


def foreign_mask(img, src_colours, thresh=60):
    """Pixels whose colour is far from every colour the source sprite uses.

    Generated glow, fire, magic trails and impact bursts land here even when
    they overlap the body, which the silhouette checks cannot see.
    """
    px = img.load()
    w, h = img.size
    cache = {}
    out = [[False] * w for _ in range(h)]
    sc = list(src_colours)
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < ALPHA_ON:
                continue
            key = (r >> 2, g >> 2, b >> 2)
            d = cache.get(key)
            if d is None:
                d = min(max(abs(r - c[0]), abs(g - c[1]), abs(b - c[2])) for c in sc)
                cache[key] = d
            if d > thresh:
                out[y][x] = True
    return out


def luma(img):
    px = img.load()
    w, h = img.size
    return [[(px[x, y][0] * 299 + px[x, y][1] * 587 + px[x, y][2] * 114) // 1000
             if px[x, y][3] >= ALPHA_ON else -1 for x in range(w)] for y in range(h)]


def flash_masks(lumas, delta=70):
    """Per-frame blobs that are far brighter than the same pixel usually is.

    A generated impact burst, glow or fire arc lights up one region for one or
    two frames; honest pixel art re-uses its palette across the whole clip.
    """
    h, w = len(lumas[0]), len(lumas[0][0])
    base = [[0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            vals = sorted(v for v in (l[y][x] for l in lumas) if v >= 0)
            base[y][x] = vals[len(vals) // 2] if vals else -1
    out = []
    for l in lumas:
        out.append([[l[y][x] >= 0 and base[y][x] >= 0 and l[y][x] - base[y][x] > delta
                     for x in range(w)] for y in range(h)])
    return out


def bbox(m):
    h, w = len(m), len(m[0])
    xs = [x for y in range(h) for x in range(w) if m[y][x]]
    ys = [y for y in range(h) for x in range(w) if m[y][x]]
    if not xs:
        return None
    return min(xs), min(ys), max(xs), max(ys)


def area(m):
    return sum(sum(1 for v in row if v) for row in row_iter(m))


def row_iter(m):
    return m


def alpha_diff(a, b):
    """Fraction of pixels where the solid/empty state differs."""
    h, w = len(a), len(a[0])
    d = sum(1 for y in range(h) for x in range(w) if a[y][x] != b[y][x])
    return d / float(w * h)


# Thresholds for Attack/Action are calibrated on the shipped Warden attack -
# the one clip in the project that is accepted as a readable swing. It runs at
# peak motion 0.22 and area ratio 1.39-1.55; every animate_image attack in the
# rejection log sat near 0.09 / 1.00, which is why none of them read as attacks.
PROFILES = {
    # clip kind -> (min motion, max motion, area lo, area hi, end drift, growth cap)
    "Idle":       (0.012, 0.12, 0.88, 1.15, 0.04, 130),
    "Attack":     (0.100, 0.55, 0.75, 1.70, 1.00, 1200),
    "Action":     (0.080, 0.55, 0.75, 1.70, 1.00, 1200),
    "PhaseEnter": (0.020, 0.60, 0.70, 1.70, 1.00, 800),
}

# A weapon trail or motion smear baked into the sprite is allowed (decision
# 2026-09-13, matching the shipped Warden attack); loose particles, impact
# bursts, projectiles and fire are not. The two are not separable by pixel
# statistics, so on action clips the effect checks only ask for a human look.
SOFT_ON_ACTION = ("DETACHED_ARTIFACT", "SPECKLE", "FLASH", "FOREIGN_COLOUR", "NEW_COLOURS",
                  # an action clip starts from its own fight stance, not from the idle pose
                  "FIRST_FRAME_DRIFT")


def profile_for(clip_id):
    for k in ("PhaseEnter", "Attack", "Action"):
        if k in (clip_id or ""):
            return k, PROFILES[k]
    return "Idle", PROFILES["Idle"]


def analyse(frames_dir, source_png, clip_id=None, ref_first_frame=False):
    """ref_first_frame: judge identity against the clip's own frame 0 instead of
    the supplied sprite. Skeleton-driven clips legitimately start from a fight
    stance rather than from the static in-game pose, so the sprite is only a
    palette reference there."""
    src = strip_background(Image.open(source_png).convert("RGBA"))
    frames = load_frames(frames_dir)
    if not frames:
        return {"error": "no frames found in %s" % frames_dir}
    if ref_first_frame:
        src = frames[0][1]

    if src.size != frames[0][1].size:
        src = src.resize(frames[0][1].size, Image.NEAREST)

    src_m = mask(src)
    # Some sources ship with parts that are genuinely separate from the body -
    # the Titan's steam plume, the Inquisitor's ground flame, the Spore Mother's
    # puff. Those must not be reported as generated artifacts.
    src_comps = components(src_m)
    src_detached = [len(c) for c in src_comps[1:]]
    src_detached_max = max(src_detached) if src_detached else 0
    src_detached_total = sum(src_detached)
    src_grown = dilate(src_m, 5)
    src_pal = palette(src)
    src_cols = colour_set(src)
    src_area = area(src_m)

    kind, (mo_lo, mo_hi, ar_lo, ar_hi, end_tol, grow_cap) = profile_for(clip_id or os.path.basename(frames_dir))

    report = {
        "framesDir": frames_dir,
        "source": source_png,
        "clipId": clip_id,
        "profile": kind,
        "frameCount": len(frames),
        "canvas": list(frames[0][1].size),
        "frames": [],
        "flags": [],
    }

    bad_canvas = [n for n, im in frames if im.size != frames[0][1].size]
    if bad_canvas:
        report["flags"].append("CANVAS_MISMATCH: " + ",".join(bad_canvas))

    lumas = [luma(im) for _, im in frames]
    flashes = flash_masks(lumas)

    masks = []
    for i, (name, im) in enumerate(frames):
        m = mask(im)
        masks.append(m)
        comps = components(m)
        detached = [len(c) for c in comps[1:]]

        # pixels that exist here but nowhere near the source silhouette
        h, w = len(m), len(m[0])
        grown = [[m[y][x] and not src_grown[y][x] for x in range(w)] for y in range(h)]
        growth = components(grown)
        biggest_growth = len(growth[0]) if growth else 0

        fm = foreign_mask(im, src_cols)
        fcomp = components(fm)
        foreign_big = len(fcomp[0]) if fcomp else 0
        foreign_total = sum(len(c) for c in fcomp)

        fl = components(flashes[i])
        flash_big = len(fl[0]) if fl else 0

        pal = palette(im)
        new_colours = pal - src_pal
        bright_new = [c for c in new_colours if sum(c) >= 40]
        report["frames"].append({
            "frame": name,
            "area": area(m),
            "areaRatio": round(area(m) / float(src_area), 3) if src_area else None,
            "detachedBlobs": len(detached),
            "detachedPixels": sum(detached),
            "largestDetached": max(detached) if detached else 0,
            "largestGrowth": biggest_growth,
            "largestForeignBlob": foreign_big,
            "largestFlashBlob": flash_big,
            "foreignPixels": foreign_total,
            "newColourBuckets": len(new_colours),
            "brightNewColourBuckets": len(bright_new),
            "bbox": bbox(m),
            "motionVsFirst": round(alpha_diff(m, masks[0]), 4),
        })

    fr = report["frames"]
    worst_detached = max(f["largestDetached"] for f in fr)
    total_detached = max(f["detachedPixels"] for f in fr)
    worst_growth = max(f["largestGrowth"] for f in fr)
    worst_foreign = max(f["largestForeignBlob"] for f in fr)
    worst_flash = max(f["largestFlashBlob"] for f in fr)
    total_foreign = max(f["foreignPixels"] for f in fr)
    src_foreign = 0
    max_new_bright = max(f["brightNewColourBuckets"] for f in fr)
    peak_motion = max(f["motionVsFirst"] for f in fr)
    ratios = [f["areaRatio"] for f in fr if f["areaRatio"]]
    end_return = alpha_diff(masks[-1], src_m)
    first_fidelity = alpha_diff(masks[0], src_m)
    bottoms = [f["bbox"][3] for f in fr if f["bbox"]]
    foot_drift = (max(bottoms) - min(bottoms)) if bottoms else 0
    lefts = [f["bbox"][0] for f in fr if f["bbox"]]
    left_drift = (max(lefts) - min(lefts)) if lefts else 0

    report["summary"] = {
        "largestDetachedBlob": worst_detached,
        "sourceDetachedMax": src_detached_max,
        "maxDetachedPixels": total_detached,
        "largestGrowthBlob": worst_growth,
        "largestForeignBlob": worst_foreign,
        "largestFlashBlob": worst_flash,
        "maxForeignPixels": total_foreign,
        "maxNewBrightColourBuckets": max_new_bright,
        "peakMotion": round(peak_motion, 4),
        "areaRatioRange": [min(ratios), max(ratios)] if ratios else None,
        "firstFrameDiffVsSource": round(first_fidelity, 4),
        "lastFrameDiffVsSource": round(end_return, 4),
        "footDriftPx": foot_drift,
        "leftEdgeDriftPx": left_drift,
    }

    f = report["flags"]
    blob_limit = max(6, src_detached_max + 6)
    speckle_limit = max(12, src_detached_total + 12)
    if worst_detached >= blob_limit:
        f.append("DETACHED_ARTIFACT: blob of %d px separated from the body (source has %d)"
                 % (worst_detached, src_detached_max))
    elif total_detached >= speckle_limit:
        f.append("SPECKLE: %d loose pixels off the main silhouette (source has %d)"
                 % (total_detached, src_detached_total))
    if worst_growth >= grow_cap:
        f.append("GROWTH: %d px of new mass attached outside the source silhouette" % worst_growth)
    if worst_flash >= 150:
        f.append("FLASH: %d px lit far above their usual brightness (impact burst/glow/fire?)"
                 % worst_flash)
    if worst_foreign >= 20 or total_foreign >= 45:
        f.append("FOREIGN_COLOUR: %d px blob (%d total) in colours the source never uses "
                 "(burst/glow/fire/trail?)" % (worst_foreign, total_foreign))
    if max_new_bright >= 6:
        f.append("NEW_COLOURS: %d bright colour buckets absent from the source (glow/fire/burst?)"
                 % max_new_bright)
    if peak_motion < mo_lo:
        f.append("TOO_STATIC: peak silhouette change %.1f%% (%s needs >= %.1f%%)"
                 % (peak_motion * 100, kind, mo_lo * 100))
    if peak_motion > mo_hi:
        f.append("OVER_MOTION: peak silhouette change %.1f%% suggests a redraw, not a movement"
                 % (peak_motion * 100))
    if ratios and (max(ratios) > ar_hi or min(ratios) < ar_lo):
        f.append("SILHOUETTE_DRIFT: area ratio range %.2f-%.2f (%s allows %.2f-%.2f)"
                 % (min(ratios), max(ratios), kind, ar_lo, ar_hi))
    if first_fidelity > 0.035:
        f.append("FIRST_FRAME_DRIFT: %.1f%% differs from the source pose" % (first_fidelity * 100))
    if end_return > end_tol:
        f.append("NO_RETURN: last frame differs from the source pose by %.1f%%" % (end_return * 100))
    if foot_drift >= 5:
        f.append("FOOT_DRIFT: baseline moves %d px" % foot_drift)

    hard = ["DETACHED_ARTIFACT", "GROWTH", "FLASH", "FOREIGN_COLOUR", "NEW_COLOURS",
            "TOO_STATIC", "OVER_MOTION", "SILHOUETTE_DRIFT", "FIRST_FRAME_DRIFT",
            "CANVAS_MISMATCH"]
    if kind != "Idle":
        hard = [h for h in hard if h not in SOFT_ON_ACTION]
    report["autoVerdict"] = "REJECT" if any(x.split(":")[0] in hard for x in f)         else ("REVIEW" if f else "PASS")
    return report


def contact_sheet(frames_dir, source_png, out_path, scale=3):
    src = strip_background(Image.open(source_png).convert("RGBA"))
    frames = load_frames(frames_dir)
    w, h = frames[0][1].size
    if src.size != (w, h):
        src = src.resize((w, h), Image.NEAREST)
    cells = [("src", src)] + frames
    pad = 4
    sheet = Image.new("RGBA", (len(cells) * (w * scale + pad) + pad, h * scale + 2 * pad),
                      (28, 28, 34, 255))
    for i, (_, im) in enumerate(cells):
        big = im.resize((w * scale, h * scale), Image.NEAREST)
        sheet.alpha_composite(big, (pad + i * (w * scale + pad), pad))
    sheet.save(out_path)
    return out_path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("frames_dir")
    ap.add_argument("source_png")
    ap.add_argument("--sheet")
    ap.add_argument("--clip-id", dest="clip_id")
    ap.add_argument("--ref-first-frame", action="store_true")
    ap.add_argument("--scale", type=int, default=3)
    a = ap.parse_args()
    rep = analyse(a.frames_dir, a.source_png, a.clip_id, a.ref_first_frame)
    if a.sheet:
        rep["sheet"] = contact_sheet(a.frames_dir, a.source_png, a.sheet, a.scale)
    json.dump(rep, sys.stdout, ensure_ascii=False, indent=1)
    print()


if __name__ == "__main__":
    main()
