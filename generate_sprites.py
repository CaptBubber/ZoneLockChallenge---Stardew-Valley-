#!/usr/bin/env python3
"""Generate pixel-art spritesheet for ZoneLockChallenge (64x16, 4 sprites at 16x16)."""
from PIL import Image
import math

img = Image.new('RGBA', (64, 16), (0, 0, 0, 0))
T = (0, 0, 0, 0)


def put(x, y, color):
    if 0 <= x < 64 and 0 <= y < 16:
        img.putpixel((x, y), color)


def draw_grid(grid, palette, ox):
    for y, row in enumerate(grid):
        for x, ch in enumerate(row):
            c = palette.get(ch, T)
            if c != T:
                put(ox + x, y, c)


# ═══════════════════════════════════════════════
# SPRITE 0: Gold Medallion (permanent unlock)
# Shaded circle with sparkle highlight
# ═══════════════════════════════════════════════

cx, cy, radius = 7.5, 7.0, 6.5
outline_c = (75, 50, 15, 255)
gold_shades = [
    (130, 95, 15, 255),      # darkest shadow
    (170, 130, 25, 255),     # dark gold
    (205, 165, 40, 255),     # gold
    (235, 200, 60, 255),     # light gold
    (255, 230, 110, 255),    # bright gold
]
hx, hy = -0.707, -0.707  # highlight from upper-left

for y in range(16):
    for x in range(16):
        px, py = x + 0.5, y + 0.5
        dist = math.sqrt((px - cx) ** 2 + (py - cy) ** 2)
        if dist > radius:
            continue
        if dist > radius - 1.2:
            put(x, y, outline_c)
            continue
        if dist > 0.5:
            dx, dy = (px - cx) / dist, (py - cy) / dist
            dot = dx * hx + dy * hy
        else:
            dot = 0.3
        shade = (dot + 1.0) / 2.0
        shade *= 1.0 - (dist / (radius - 1.2)) * 0.15
        idx = max(0, min(4, int(shade * 4.5)))
        put(x, y, gold_shades[idx])

# Small sparkle cross in the highlight area
put(5, 4, (255, 252, 225, 255))
for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
    put(5 + dx, 4 + dy, (255, 245, 185, 255))


# ═══════════════════════════════════════════════
# SPRITE 1: Blue/Silver Ticket (daily ticket)
# Rectangular card with center highlight + perf dots
# ═══════════════════════════════════════════════

draw_grid([
    #0123456789ABCDEF
    "................",  # 0
    "................",  # 1
    ".OOOOOOOOOOO....",  # 2  outline top
    ".ODMMMMMMMDO....",  # 3  dark inner border
    ".ODLLLLLLLDO.O..",  # 4  light fill + perf dot
    ".ODLLLHHLLDO....",  # 5  center highlight
    ".ODLLLHHLLDO.O..",  # 6  highlight + perf dot
    ".ODLLLLLLLDO....",  # 7  light fill
    ".ODMMMMMMMDO.O..",  # 8  dark border + perf dot
    ".ODDDDDDDDDO....",  # 9  shadow bottom
    ".OOOOOOOOOOO....",  # 10 outline bottom
    "................",  # 11
    "................",  # 12
    "................",  # 13
    "................",  # 14
    "................",  # 15
], {
    'O': (35, 50, 85, 255),       # dark blue outline
    'D': (60, 85, 130, 255),      # dark blue
    'M': (95, 130, 180, 255),     # medium blue
    'L': (135, 175, 220, 255),    # light blue
    'H': (180, 210, 240, 255),    # highlight
}, 16)


# ═══════════════════════════════════════════════
# SPRITE 2: Wooden Arrow Sign (warp point)
# Arrow-shaped signboard on a post
# ═══════════════════════════════════════════════

draw_grid([
    #0123456789ABCDEF
    "................",  # 0
    ".OOOOOOOO.......",  # 1  top edge
    ".ODMMMMMOO......",  # 2  body
    ".ODMLLMMMOO.....",  # 3  arrow starts extending
    ".ODMLBMMMMOO....",  # 4  extending further
    ".ODMLLBMMMMOO...",  # 5  arrow tip (widest)
    ".ODMLBMMMMOO....",  # 6  shrinking (mirror of 4)
    ".ODMLLMMMOO.....",  # 7  (mirror of 3)
    ".ODMMMMMOO......",  # 8  body
    ".OOOOOOOO.......",  # 9  bottom edge
    "....OO..........",  # 10 post
    "....OM..........",  # 11
    "....OM..........",  # 12
    "....OO..........",  # 13
    "...OOOO.........",  # 14 base
    "................",  # 15
], {
    'O': (50, 32, 12, 255),       # dark wood outline
    'D': (95, 65, 30, 255),       # dark wood
    'M': (140, 100, 55, 255),     # medium wood
    'L': (180, 140, 85, 255),     # light wood
    'B': (215, 180, 125, 255),    # bright wood highlight
}, 32)


# ═══════════════════════════════════════════════
# SPRITE 3: Red Padlock (lock icon)
# Iron shackle on top, red body below
# ═══════════════════════════════════════════════

draw_grid([
    #0123456789ABCDEF
    "................",  # 0
    "......SSSS......",  # 1  shackle top
    ".....SIIIIS.....",  # 2  shackle inner
    ".....SI..IS.....",  # 3  shackle arms (transparent center)
    ".....SI..IS.....",  # 4  shackle arms
    "....OOOOOOOOO...",  # 5  body top outline
    "....OLMMMMMDO...",  # 6  body (light-left shading)
    "....OLLMMMDDO...",  # 7
    "....OMLHHLDDO...",  # 8  keyhole highlight
    "....OMLHHLDDO...",  # 9
    "....OLLMMMDDO...",  # 10
    "....OLMMMMMDO...",  # 11
    "....ODDDDDDDO...",  # 12 shadow bottom
    "....OOOOOOOOO...",  # 13 body bottom outline
    "................",  # 14
    "................",  # 15
], {
    'S': (60, 60, 65, 255),       # shackle outline (dark iron)
    'I': (115, 115, 125, 255),    # shackle inner (lighter iron)
    'O': (55, 20, 15, 255),       # body outline (dark red-brown)
    'D': (120, 40, 30, 255),      # body dark red
    'M': (175, 60, 45, 255),      # body medium red
    'L': (215, 90, 65, 255),      # body light red
    'H': (245, 140, 110, 255),    # body highlight (keyhole)
}, 48)


img.save('/home/user/ZoneLockChallenge---Stardew-Valley-/assets/sprites.png')
print("Spritesheet saved!")
