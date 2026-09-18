#!/usr/bin/env python3
"""Generate VBL app icons (PNG, ICO, ICNS) for Desktop and WPF resource folders."""
from __future__ import annotations

import json
import math
import os
import shutil
import struct
import subprocess
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# Tray / proxy-state accent colors (NotifyIcon1..4)
ACCENTS = [
    (0x5B, 0x8D, 0xFF),  # clear / default blue
    (0x3D, 0xC9, 0x6E),  # set proxy green
    (0xF5, 0xA6, 0x23),  # pac orange
    (0x9B, 0x59, 0xB6),  # nothing purple
]


def _lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def _draw_icon(size: int, accent: tuple[int, int, int]) -> list[list[tuple[int, int, int, int]]]:
    pixels: list[list[tuple[int, int, int, int]]] = []
    cx = cy = (size - 1) / 2
    r = size * 0.46
    for y in range(size):
        row: list[tuple[int, int, int, int]] = []
        for x in range(size):
            dx = x - cx
            dy = y - cy
            dist = math.sqrt(dx * dx + dy * dy)
            if dist > r:
                row.append((0, 0, 0, 0))
                continue
            t = max(0.0, min(1.0, (dy + r) / (2 * r)))
            bg = (
                int(_lerp(0x1A, 0x2D, t)),
                int(_lerp(0x1F, 0x4A, t)),
                int(_lerp(0x3A, 0x8F, t)),
            )
            mix = 0.22
            col = (
                int(bg[0] * (1 - mix) + accent[0] * mix),
                int(bg[1] * (1 - mix) + accent[1] * mix),
                int(bg[2] * (1 - mix) + accent[2] * mix),
                255,
            )
            row.append(col)
        pixels.append(row)
    return pixels


def _png_chunk(tag: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)


def write_png(path: Path, rgba_pixels: list[list[tuple[int, int, int, int]]]) -> None:
    h = len(rgba_pixels)
    w = len(rgba_pixels[0])
    raw = bytearray()
    for row in rgba_pixels:
        raw.append(0)
        for r, g, b, a in row:
            raw.extend((r, g, b, a))
    compressed = zlib.compress(bytes(raw), 9)
    ihdr = struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)
    png = b"\x89PNG\r\n\x1a\n" + _png_chunk(b"IHDR", ihdr) + _png_chunk(b"IDAT", compressed) + _png_chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def _render_text_layer(size: int, text: str = "VBL") -> list[list[tuple[int, int, int, int]]]:
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError:
        return [[(0, 0, 0, 0) for _ in range(size)] for _ in range(size)]

    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    font_size = int(size * 0.34)
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", font_size)
    except OSError:
        font = ImageFont.load_default()
    bbox = draw.textbbox((0, 0), text, font=font)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]
    draw.text(((size - tw) / 2, (size - th) / 2 - size * 0.02), text, fill=(255, 255, 255, 245), font=font)
    w, h = img.size
    data = list(img.getdata())
    return [data[y * w : (y + 1) * w] for y in range(h)]


def _flatten_rgba(px: list[list[tuple[int, int, int, int]]]) -> list[tuple[int, int, int, int]]:
    return [c for row in px for c in row]


def compose_icon(size: int, accent: tuple[int, int, int]) -> list[list[tuple[int, int, int, int]]]:
    base = _draw_icon(size, accent)
    text = _render_text_layer(size)
    out: list[list[tuple[int, int, int, int]]] = []
    for y in range(size):
        row: list[tuple[int, int, int, int]] = []
        for x in range(size):
            br, bg, bb, ba = base[y][x]
            tr, tg, tb, ta = text[y][x]
            if ta == 0:
                row.append((br, bg, bb, ba))
            else:
                a = ta / 255.0
                row.append(
                    (
                        int(br * (1 - a) + tr * a),
                        int(bg * (1 - a) + tg * a),
                        int(bb * (1 - a) + tb * a),
                        max(ba, ta),
                    )
                )
        out.append(row)
    return out


def write_ico(path: Path, sizes: list[int], accent: tuple[int, int, int]) -> None:
    from PIL import Image

    images = []
    for s in sizes:
        img = Image.new("RGBA", (s, s))
        img.putdata(_flatten_rgba(compose_icon(s, accent)))
        images.append(img)
    path.parent.mkdir(parents=True, exist_ok=True)
    images[0].save(path, format="ICO", sizes=[(im.width, im.height) for im in images], append_images=images[1:])


def write_icns(path: Path, accent: tuple[int, int, int]) -> None:
    from PIL import Image

    iconset = path.with_suffix(".iconset")
    if iconset.exists():
        shutil.rmtree(iconset)
    iconset.mkdir(parents=True)
    size_map = {
        "icon_16x16.png": 16,
        "icon_16x16@2x.png": 32,
        "icon_32x32.png": 32,
        "icon_32x32@2x.png": 64,
        "icon_128x128.png": 128,
        "icon_128x128@2x.png": 256,
        "icon_256x256.png": 256,
        "icon_256x256@2x.png": 512,
        "icon_512x512.png": 512,
        "icon_512x512@2x.png": 1024,
    }
    for name, s in size_map.items():
        img = Image.new("RGBA", (s, s))
        img.putdata(_flatten_rgba(compose_icon(s, accent)))
        img.save(iconset / name, format="PNG")
    subprocess.run(["iconutil", "-c", "icns", str(iconset), "-o", str(path)], check=True)
    shutil.rmtree(iconset)


def main() -> None:
    targets = [
        ROOT / "v2rayN/v2rayN.Desktop/Assets",
        ROOT / "v2rayN/v2rayN/Resources",
    ]
    main_png = ROOT / "v2rayN/v2rayN.Desktop/VBL.png"
    write_png(main_png, compose_icon(512, ACCENTS[0]))
    write_icns(ROOT / "v2rayN/v2rayN.Desktop/VBL.icns", ACCENTS[0])

    for folder in targets:
        write_ico(folder / "VBL.ico", [16, 24, 32, 48, 64, 128, 256], ACCENTS[0])
        for i, accent in enumerate(ACCENTS, start=1):
            write_ico(folder / f"NotifyIcon{i}.ico", [16, 24, 32, 48, 64], accent)

    print(json.dumps({"ok": True, "png": str(main_png)}, indent=2))


if __name__ == "__main__":
    main()
