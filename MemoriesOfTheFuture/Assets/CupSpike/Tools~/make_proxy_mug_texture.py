# Usage (from the Unity project folder): python3 Assets/CupSpike/Tools~/make_proxy_mug_texture.py "$PWD/Assets/CupSpike/Textures/ProxyMug_Albedo.png"
# Needs Pillow and numpy. Also writes a *_design_preview.png next to the output; delete it or move it out of Assets.
# Builds the proxy mug albedo (RGB) + smoothness (A) texture.
# Design is drawn in millimetre space on the unrolled outer wall, then remapped
# into the body UV strip of zipinkent-cambridge-mug.blend.
import sys, math
import numpy as np
from PIL import Image, ImageDraw

out = sys.argv[1]
PX = 12                      # px per mm in design space
R = 40.0                     # outer radius mm
C = 2 * math.pi * R          # circumference mm
H = 95.0                     # wall height mm (y -45.1 .. 49.9)
Y0 = -45.1                   # mm, bottom of wall in model space

# Measured from the photos (estimates, see README)
BAND_BOTTOM, BAND_TOP = 6.0, 89.5   # mm from base
SEAM_DEG = 157.5                    # model angle at UV u0
HANDLE_DEG = 180.0
LOGO_DEG = 90.0                     # logo faces +Z (handle on -X)
HANDLE_GAP = 22.0                   # mm white gap centred on handle
LOGO_W, LOGO_H = 50.0, 53.0
LOGO_CY = 48.5                      # mm from base, centre of shield

WHITE = (242, 242, 238); BLACK = (28, 29, 31); GOLD = (201, 166, 92)
S_WHITE, S_BLACK, S_GOLD = 220, 110, 90   # smoothness in alpha

def arc_of(deg):  # mm along unrolled strip from the seam
    return ((deg - SEAM_DEG) % 360) / 360 * C

W, Hh = int(C * PX), int(H * PX)
rgb = Image.new('RGB', (W, Hh), WHITE); sm = Image.new('L', (W, Hh), S_WHITE)
dr, ds = ImageDraw.Draw(rgb), ImageDraw.Draw(sm)
def P(x, y): return (x * PX, (H - y) * PX)   # mm (arc, height-from-base) -> px

# Black band, with a white gap around the handle
ha = arc_of(HANDLE_DEG)
g0, g1 = ha - HANDLE_GAP / 2, ha + HANDLE_GAP / 2
band = [(g1, C + g0)] if g0 >= 0 else [(g1, C + g0)]
for a0, a1 in band:
    for seg0, seg1 in ([(a0, min(a1, C))] + ([(0, a1 - C)] if a1 > C else [])):
        box = [P(seg0, BAND_TOP), P(seg1, BAND_BOTTOM)]
        dr.rectangle(box, fill=BLACK); ds.rectangle(box, fill=S_BLACK)

# Shield: fractions of the logo box (x right, y down from top)
lx = arc_of(LOGO_DEG) - LOGO_W / 2; ly = LOGO_CY + LOGO_H / 2
def L(fx, fy): return P(lx + fx * LOGO_W, ly - fy * LOGO_H)
BASE = [(0.15, 0), (0.85, 0), (1, 0.175), (1, 0.57), (0.5, 1), (0, 0.57), (0, 0.175)]
def shield(inset):
    # true inward offset of the convex outline, in mm
    pts = [np.array([fx * LOGO_W, fy * LOGO_H]) for fx, fy in BASE]
    n = len(pts); cen = sum(pts) / n; lines = []
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]; d = (b - a) / np.linalg.norm(b - a)
        nrm = np.array([-d[1], d[0]])
        if np.dot(cen - a, nrm) < 0: nrm = -nrm
        lines.append((a + nrm * inset, d))
    out = []
    for i in range(n):
        (p1, d1), (p2, d2) = lines[i - 1], lines[i]
        t = np.linalg.solve(np.array([d1, -d2]).T, p2 - p1)[0]
        q = p1 + d1 * t; out.append(L(q[0] / LOGO_W, q[1] / LOGO_H))
    return out
for inset, col, s in [(0, GOLD, S_GOLD), (2.6, BLACK, S_BLACK), (3.6, GOLD, S_GOLD), (4.5, BLACK, S_BLACK)]:
    dr.polygon(shield(inset), fill=col); ds.polygon(shield(inset), fill=s)
# Serif H
def rect(x0, y0, x1, y1):
    b = [L(x0, y0), L(x1, y1)]; dr.rectangle(b, fill=GOLD); ds.rectangle(b, fill=S_GOLD)
for sx in (0.245, 0.625):                 # stems
    rect(sx, 0.215, sx + 0.13, 0.70)
    rect(sx - 0.05, 0.205, sx + 0.18, 0.24)   # top serif
    rect(sx - 0.05, 0.675, sx + 0.18, 0.71)   # bottom serif
rect(0.245, 0.43, 0.755, 0.49)            # crossbar

design = np.dstack([np.asarray(rgb), np.asarray(sm)])

# Remap into the UV texture
T = 2048
U0, U1 = 0.14741, 0.91243
def v_to_y(v): return -42.4 + (v - 0.018) * 299.0     # mm, model space
tex = np.zeros((T, T, 4), np.uint8); tex[...] = (*WHITE, S_WHITE)
pad = 8 / T
us = (np.arange(T) + 0.5) / T; vs = (np.arange(T) + 0.5) / T
uc = us[(us > U0 - pad) & (us < U1 + pad)]
vc = vs[(vs > 0.009 - pad) & (vs < 0.3394 + pad)]
arc = ((uc - U0) / (U1 - U0) * C) % C
hgt = np.clip(v_to_y(vc) - Y0, 0, H - 1e-3)
xi = np.clip((arc * PX).astype(int), 0, W - 1)
yi = np.clip(((H - hgt) * PX).astype(int), 0, Hh - 1)
block = design[yi][:, xi]
ui0 = int(round((uc[0] - 0.5 / T) * T)); vi0 = int(round((vc[0] - 0.5 / T) * T))
tex[vi0:vi0 + len(vc), ui0:ui0 + len(uc)] = block
img = Image.fromarray(tex[::-1], 'RGBA')    # row 0 = top of image = v 1
img.save(out)
Image.fromarray(design[..., :3]).resize((W // 4, Hh // 4)).save(out.replace('.png', '_design_preview.png'))
print('saved', out, 'logo arc mm', round(arc_of(LOGO_DEG), 1), 'handle arc mm', round(ha, 1))
