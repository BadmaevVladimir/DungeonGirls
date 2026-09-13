#!/usr/bin/env python3
"""Download a PixelLab character animation into a frames folder.

The MCP get_character output lists each animation's frame URLs. Paste the
comma-separated list (or the whole line) and this saves them as frame_NN.png
in the order given, which is the order they play in.

  python Tools/boss_anim_fetch.py <out_dir> <url> [<url> ...]
  python Tools/boss_anim_fetch.py <out_dir> --stdin   # URLs on stdin, any separator
"""
import os
import re
import sys
import urllib.request


def fetch(out_dir, urls):
    os.makedirs(out_dir, exist_ok=True)
    written = []
    for i, u in enumerate(urls):
        dst = os.path.join(out_dir, "frame_%02d.png" % i)
        # the CDN rejects the default python user agent
        req = urllib.request.Request(u, headers={"User-Agent": "curl/8"})
        with urllib.request.urlopen(req) as r, open(dst, "wb") as fh:
            fh.write(r.read())
        written.append(dst)
    return written


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    out_dir = sys.argv[1]
    if sys.argv[2] == "--stdin":
        blob = sys.stdin.read()
    else:
        blob = " ".join(sys.argv[2:])
    urls = re.findall(r"https://\S+?\.png(?:\?[^\s,]*)?", blob)
    if not urls:
        print("no frame URLs found")
        return 1
    for p in fetch(out_dir, urls):
        print(p)
    return 0


if __name__ == "__main__":
    sys.exit(main())
