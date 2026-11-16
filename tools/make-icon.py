"""Draws LED-Next's app icon: a dark arcade panel with glowing, lit buttons.

    python tools/make-icon.py          (needs Pillow: pip install pillow)

Writes src/LedNext.App/Assets/LedNext.ico (16-256 px, PNG-compressed entries) and LedNext.png (256 px preview).
Small sizes get four big buttons so they still read on the taskbar and tray; 48 px and up get six (two rows of
three, like a fighting-game panel). Each size is drawn on its own at 4x and scaled down, not shrunk from 256.
"""
import io
import os
import struct

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "src", "LedNext.App", "Assets")
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
SS = 4  # supersampling

RED, YELLOW, GREEN = (255, 52, 52), (255, 206, 30), (60, 230, 90)
BLUE, MAGENTA, CYAN = (60, 120, 255), (230, 60, 230), (40, 220, 240)


def lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def panel(n):
    """Rounded square, top-to-bottom gradient, faint light edge."""
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    m = round(n * 0.03)
    r = round(n * 0.2)
    mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle((m, m, n - 1 - m, n - 1 - m), r, fill=255)
    grad = Image.new("RGBA", (n, n))
    top, bottom = (52, 58, 72), (14, 16, 22)
    gd = ImageDraw.Draw(grad)
    for y in range(n):
        gd.line((0, y, n, y), fill=lerp(top, bottom, y / (n - 1)) + (255,))
    img.paste(grad, (0, 0), mask)
    edge = max(1, round(n * 0.012))
    ImageDraw.Draw(img).rounded_rectangle((m, m, n - 1 - m, n - 1 - m), r, outline=(120, 130, 150, 110), width=edge)
    return img


def button(img, cx, cy, rad, color, detail):
    n = img.size[0]
    # Glow: a blurred disc of the colour around the button.
    if detail:
        glow = Image.new("RGBA", (n, n), (0, 0, 0, 0))
        g = rad * 1.55
        ImageDraw.Draw(glow).ellipse((cx - g, cy - g, cx + g, cy + g), fill=color + (150,))
        glow = glow.filter(ImageFilter.GaussianBlur(rad * 0.45))
        img.alpha_composite(glow)
    d = ImageDraw.Draw(img)
    # Dark rim (the button's bezel), then the lit body getting brighter toward the middle.
    rim = lerp(color, (0, 0, 0), 0.55)
    d.ellipse((cx - rad, cy - rad, cx + rad, cy + rad), fill=rim + (255,))
    body = rad * (0.84 if detail else 0.8)
    steps = 24
    for i in range(steps):
        t = i / (steps - 1)
        rr = body * (1 - 0.75 * t)
        c = lerp(color, (255, 255, 255), 0.55 * t * t)
        d.ellipse((cx - rr, cy - rr, cx + rr, cy + rr), fill=c + (255,))
    if detail:
        # Small shine at the top left.
        hl = Image.new("RGBA", (n, n), (0, 0, 0, 0))
        hr = rad * 0.32
        hx, hy = cx - rad * 0.32, cy - rad * 0.36
        ImageDraw.Draw(hl).ellipse((hx - hr, hy - hr * 0.7, hx + hr, hy + hr * 0.7), fill=(255, 255, 255, 150))
        hl = hl.filter(ImageFilter.GaussianBlur(rad * 0.08))
        img.alpha_composite(hl)


def draw(size):
    n = size * SS
    img = panel(n)
    if size < 48:
        # Four big buttons, 2x2.
        rad = n * 0.17
        for (fx, fy), c in zip([(0.31, 0.31), (0.69, 0.31), (0.31, 0.69), (0.69, 0.69)], [RED, GREEN, BLUE, YELLOW]):
            button(img, n * fx, n * fy, rad, c, detail=size >= 24)
    else:
        # Six buttons, two rows of three; the rows curve a little like a real panel.
        rad = n * 0.118
        xs = [0.23, 0.5, 0.77]
        lift = [0.035, 0.0, 0.035]
        for row, (base, colors) in enumerate([(0.36, [RED, YELLOW, GREEN]), (0.66, [BLUE, MAGENTA, CYAN])]):
            for fx, dl, c in zip(xs, lift, colors):
                button(img, n * fx, n * (base + dl), rad, c, detail=True)
    return img.resize((size, size), Image.LANCZOS)


def write_ico(path, images):
    """ICO with PNG entries (Vista+), sizes in ascending order."""
    pngs = []
    for im in images:
        b = io.BytesIO()
        im.save(b, "PNG")
        pngs.append(b.getvalue())
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for im, data in zip(images, pngs):
        w = im.size[0]
        entries += struct.pack("<BBBBHHII", w % 256, w % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(path, "wb") as f:
        f.write(header + entries + b"".join(pngs))


def main():
    os.makedirs(OUT, exist_ok=True)
    images = [draw(s) for s in SIZES]
    write_ico(os.path.join(OUT, "LedNext.ico"), images)
    images[-1].save(os.path.join(OUT, "LedNext.png"))
    print("wrote", os.path.normpath(os.path.join(OUT, "LedNext.ico")))


if __name__ == "__main__":
    main()
