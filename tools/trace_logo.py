"""Vectorize the two-colour Aqua Computer emblem (acicon150.png from aquasuite) into SVG.

Soft colour masks -> 6x Lanczos upscale -> marching squares (iso 0.5, sub-pixel) ->
RDP simplification -> Catmull-Rom cubic Beziers with corner preservation.
"""
import math, sys
import numpy as np
from PIL import Image, ImageFilter

SRC, OUT = sys.argv[1], sys.argv[2]
UP = 6

im = Image.open(SRC).convert("RGBA")
a = np.asarray(im).astype(np.float64)
W, H = im.size
alpha = a[..., 3] / 255.0
# RGB of nearly transparent pixels is unreliable: classify confident pixels by hue,
# then let the rest inherit the majority class of already-classified neighbours.
cls = np.full((H, W), -1)
conf = alpha > 0.5
cls[conf] = (a[..., 2] > a[..., 0])[conf]
for _ in range(12):
    if (cls >= 0).all(): break
    p = np.pad(cls, 1, constant_values=-1)
    nb = [p[1 + dy:H + 1 + dy, 1 + dx:W + 1 + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1)]
    blue_n = sum((n == 1).astype(int) for n in nb); yel_n = sum((n == 0).astype(int) for n in nb)
    fill = (cls < 0) & ((blue_n + yel_n) > 0)
    cls[fill] = (blue_n >= yel_n)[fill]
is_blue = cls == 1

def mean_color(mask):
    px = a[mask & (a[..., 3] == 255)][:, :3]
    return '#%02X%02X%02X' % tuple(int(round(v)) for v in np.median(px, axis=0))

colors = {"blue": mean_color(is_blue), "yellow": mean_color(~is_blue)}
# The icon carries a dark drop shadow. Blue (R=0) and yellow (B=0) separate cleanly in the
# premultiplied channels, and the near-black shadow contributes ~nothing to either.
blue_rgb = [int(colors["blue"][i:i + 2], 16) for i in (1, 3, 5)]
yel_rgb = [int(colors["yellow"][i:i + 2], 16) for i in (1, 3, 5)]
masks = {"blue": np.clip(a[..., 2] * alpha / blue_rgb[2], 0, 1),
         "yellow": np.clip(a[..., 0] * alpha / yel_rgb[0], 0, 1)}


def upscale(m):
    img = Image.fromarray((m * 255).astype(np.uint8), "L")
    img = img.resize((W * UP, H * UP), Image.LANCZOS).filter(ImageFilter.GaussianBlur(UP * 0.35))
    return np.asarray(img).astype(np.float64) / 255.0


def marching_squares(f, iso=0.5):
    f = np.pad(f, 1)  # closes every contour
    h, w = f.shape
    tl, tr, br, bl = f[:-1, :-1], f[:-1, 1:], f[1:, 1:], f[1:, :-1]
    idx = (tl > iso) * 8 + (tr > iso) * 4 + (br > iso) * 2 + (bl > iso) * 1
    ys, xs = np.nonzero((idx > 0) & (idx < 15))

    def interp(v0, v1):
        return (iso - v0) / (v1 - v0)

    edges_for = {1: [(3, 2)], 2: [(2, 1)], 3: [(3, 1)], 4: [(1, 0)], 5: [(3, 0), (1, 2)], 6: [(2, 0)], 7: [(3, 0)],
                 8: [(0, 3)], 9: [(0, 2)], 10: [(0, 1), (2, 3)], 11: [(0, 1)], 12: [(1, 3)], 13: [(1, 2)], 14: [(2, 3)]}
    nxt = {}
    for y, x in zip(ys, xs):
        c = idx[y, x]
        v = (f[y, x], f[y, x + 1], f[y + 1, x + 1], f[y + 1, x])
        def pt(e):
            if e == 0: return (x + interp(v[0], v[1]), y)            # top
            if e == 1: return (x + 1, y + interp(v[1], v[2]))        # right
            if e == 2: return (x + interp(v[3], v[2]), y + 1)        # bottom
            return (x, y + interp(v[0], v[3]))                       # left
        def key(e):
            return {0: ('h', y, x), 1: ('v', y, x + 1), 2: ('h', y + 1, x), 3: ('v', y, x)}[e]
        for e0, e1 in edges_for[c]:
            nxt[key(e0)] = (key(e1), pt(e1))
    loops, seen = [], set()
    for start in list(nxt):
        if start in seen: continue
        loop, k = [], start
        while k not in seen and k in nxt:
            seen.add(k); k, p = nxt[k]; loop.append((p[0] - 1, p[1] - 1))
        if len(loop) > 8: loops.append(loop)
    return loops


def rdp(pts, eps):
    pts = np.asarray(pts)
    if len(pts) < 3: return pts
    a_, b_ = pts[0], pts[-1]
    d = b_ - a_; n = math.hypot(*d) or 1e-9
    dist = np.abs(d[0] * (pts[:, 1] - a_[1]) - d[1] * (pts[:, 0] - a_[0])) / n
    i = int(np.argmax(dist))
    if dist[i] > eps:
        return np.vstack([rdp(pts[:i + 1], eps)[:-1], rdp(pts[i:], eps)])
    return np.vstack([a_, b_])


def simplify_closed(loop, eps):
    p = np.asarray(loop)
    far = int(np.argmax(np.hypot(*(p - p[0]).T)))
    s = np.vstack([rdp(p[:far + 1], eps)[:-1], rdp(np.vstack([p[far:], p[:1]]), eps)[:-1]])
    return s


def area(p):
    x, y = p[:, 0], p[:, 1]
    return 0.5 * (np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))


def to_path(p, scale):
    n = len(p)
    def ang(i):
        v1 = p[i] - p[i - 1]; v2 = p[(i + 1) % n] - p[i]
        c = np.dot(v1, v2) / ((np.linalg.norm(v1) * np.linalg.norm(v2)) or 1e-9)
        return math.degrees(math.acos(max(-1, min(1, c))))
    corner = [ang(i) > 55 for i in range(n)]
    f = lambda q: f"{q[0] * scale:.2f},{q[1] * scale:.2f}"
    d = [f"M{f(p[0])}"]
    for i in range(n):
        p0, p1, p2, p3 = p[i - 1], p[i], p[(i + 1) % n], p[(i + 2) % n]
        t1 = (p2 - p0) / 6 if not corner[i] else np.zeros(2)
        t2 = (p3 - p1) / 6 if not corner[(i + 1) % n] else np.zeros(2)
        # keep tangents from overshooting on short segments
        seg = np.linalg.norm(p2 - p1)
        for t in (t1, t2):
            m = np.linalg.norm(t)
            if m > seg * 0.5: t *= seg * 0.5 / m
        d.append(f"C{f(p1 + t1)} {f(p2 - t2)} {f(p2)}")
    return "".join(d) + "Z"


paths = []
for name in ("blue", "yellow"):
    loops = marching_squares(upscale(masks[name]))
    ds = []
    for lp in loops:
        s = simplify_closed(lp, eps=0.9)
        print(f"   {name} contour area={abs(area(s)) / UP**2:.1f}px^2 pts={len(s)}")
        if abs(area(s)) < (UP * 2) ** 2: continue
        ds.append(to_path(s, 1.0 / UP))
    paths.append((name, ds))
    print(name, colors[name], "contours:", len(ds), "points:", sum(d.count('C') for d in ds))

ys, xs = np.nonzero(alpha > 0.02)
pad = 2
vb = (xs.min() - pad, ys.min() - pad, xs.max() - xs.min() + 1 + 2 * pad, ys.max() - ys.min() + 1 + 2 * pad)
with open(OUT, "w", encoding="utf-8") as fh:
    fh.write(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb[0]} {vb[1]} {vb[2]} {vb[3]}">\n')
    fh.write('  <title>Aqua Computer emblem (vectorised from aquasuite acicon150.png)</title>\n')
    for name, ds in paths:
        fh.write(f'  <path id="ac-{name}" fill="{colors[name]}" fill-rule="evenodd" d="{"".join(ds)}"/>\n')
    fh.write('</svg>\n')
print("viewBox", vb, "->", OUT)
