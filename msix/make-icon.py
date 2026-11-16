#!/usr/bin/env python3
"""Draws the CYSMS logo (an original owl peeking over a screen) and writes src/CYSMS.ico + all MSIX logo assets."""
import glob, os, sys
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BROWN, CREAM, GREEN, GREEN_D, BEAK, DARK, TAN = "#4A3423", "#F6EFDF", "#4F8A3B", "#3D6E2D", "#F2A33A", "#2F2418", "#C9A97A"
TILE = BROWN

def master(n=2048, tile=True):
    """An owl peeking over the screen it mirrors to."""
    im = Image.new("RGBA", (n, n), (0, 0, 0, 0)); d = ImageDraw.Draw(im); k = n / 1024.0
    P = lambda *v: [x * k for x in v]
    if tile: d.rounded_rectangle(P(24, 24, 1000, 1000), radius=210 * k, fill=TILE)
    cy = 440
    d.ellipse(P(150, cy - 330, 874, cy + 330), fill=CREAM)                                    # head
    d.polygon(P(150, cy - 250, 240, cy - 330, 512, cy - 90, 784, cy - 330, 874, cy - 250, 874, cy - 120, 512, cy + 10, 150, cy - 120), fill=TAN)  # brow
    for cx in (350, 674):                                                                     # eyes
        d.ellipse(P(cx - 150, 300, cx + 150, 600), fill="white", outline=DARK, width=int(26 * k))
        d.ellipse(P(cx - 70, 380, cx + 70, 520), fill=DARK); d.ellipse(P(cx - 40, 395, cx, 435), fill="white")
    d.polygon(P(462, 560, 562, 560, 512, 670), fill=BEAK)
    if tile:
        m = Image.new("L", (n, n), 0); ImageDraw.Draw(m).rounded_rectangle(P(24, 24, 1000, 1000), radius=210 * k, fill=255)
        scr = Image.new("RGBA", (n, n), (0, 0, 0, 0)); sd = ImageDraw.Draw(scr)
        sd.rounded_rectangle(P(110, 690, 914, 1100), radius=70 * k, fill=GREEN); sd.rounded_rectangle(P(110, 690, 914, 760), radius=34 * k, fill=GREEN_D)
        scr.putalpha(Image.composite(scr.getchannel("A"), Image.new("L", (n, n), 0), m)); im.alpha_composite(scr); d = ImageDraw.Draw(im)
    else:
        d.rounded_rectangle(P(110, 690, 914, 1000), radius=70 * k, fill=GREEN); d.rounded_rectangle(P(110, 690, 914, 760), radius=34 * k, fill=GREEN_D)
    for x in (250, 690): d.rounded_rectangle(P(x, 650, x + 84, 740), radius=30 * k, fill=BEAK)  # feet
    return im

def fit(size, tile=True):
    return master(tile=tile).resize((size, size), Image.LANCZOS)

if __name__ == "__main__":
    fit(256).save(os.path.join(ROOT, "src", "CYSMS.ico"), sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    for f in glob.glob(os.path.join(ROOT, "msix", "Assets", "*.png")):
        w, h = Image.open(f).size
        if w == h: fit(w).save(f)
        else:
            im = Image.new("RGBA", (w, h), TILE); ic = fit(int(h * 0.86), tile=False)
            im.alpha_composite(ic, ((w - ic.width) // 2, (h - ic.height) // 2)); im.save(f)
    fit(512).save(os.path.join(ROOT, "msix", "logo-preview.png"))
    print("ok")
