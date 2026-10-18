"""Builds the Windows .ico (and a PNG) from the Gemini icon: crops the rounded square, makes the corners transparent."""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

src, out_dir = Path(sys.argv[1]), Path(sys.argv[2])
img = Image.open(src).convert("RGBA")
w, h = img.size
# The rounded square spans ~10%..90% of the 1024 px Gemini canvas.
box = (int(w * 0.103), int(h * 0.098), int(w * 0.899), int(h * 0.894))
square = img.crop(box).resize((1024, 1024), Image.LANCZOS)
mask = Image.new("L", (1024 * 4, 1024 * 4), 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, 1024 * 4 - 1, 1024 * 4 - 1), radius=230 * 4, fill=255)
square.putalpha(mask.resize((1024, 1024), Image.LANCZOS))
out_dir.mkdir(parents=True, exist_ok=True)
square.resize((256, 256), Image.LANCZOS).save(out_dir / "app.png")
square.save(out_dir / "app.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
print("ok", box)
