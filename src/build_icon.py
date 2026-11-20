import os
import sys
from PIL import Image, ImageDraw

def make_icon(out_path):
    size = 256
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # Crisp UU-style modern royal blue rounded icon (#1677FF -> #0E5CE0)
    pad = 16
    radius = 56
    for y in range(pad, size - pad):
        t = (y - pad) / float(size - 2 * pad)
        r = int(22 + (14 - 22) * t)
        g = int(125 + (92 - 125) * t)
        b = int(255 + (224 - 255) * t)
        draw.line([(pad, y), (size - pad, y)], fill=(r, g, b, 255))

    mask = Image.new("L", (size, size), 0)
    mdraw = ImageDraw.Draw(mask)
    mdraw.rounded_rectangle([pad, pad, size - pad, size - pad], radius=radius, fill=255)
    bg = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bg.paste(img, (0, 0), mask)
    img = bg
    draw = ImageDraw.Draw(img)

    # Minimal clean desktop screen + subtle corner link mark (white)
    draw.rounded_rectangle([54, 62, 202, 162], radius=16, outline=(255, 255, 255, 255), width=13)
    draw.rounded_rectangle([114, 166, 142, 190], radius=4, fill=(255, 255, 255, 245))
    draw.rounded_rectangle([88, 186, 168, 200], radius=7, fill=(255, 255, 255, 255))

    # Clean geometric 4-dot/link center (minimalist, non-cluttered)
    draw.rounded_rectangle([84, 88, 122, 136], radius=8, fill=(255, 255, 255, 245))
    draw.rounded_rectangle([134, 88, 172, 114], radius=7, fill=(147, 197, 253, 255))
    draw.rounded_rectangle([134, 122, 172, 136], radius=6, fill=(255, 255, 255, 210))

    sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    img.save(out_path, format="ICO", sizes=sizes)
    print("Icon saved:", out_path)

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "app.ico")
    make_icon(out)
