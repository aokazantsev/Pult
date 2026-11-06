import os

from PIL import Image, ImageDraw

S = 1024
base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(base)
d.rounded_rectangle((32, 32, S - 32, S - 32), radius=220, fill=(47, 111, 235, 255))
d.rounded_rectangle((300, 200, 724, 824), radius=90, fill=(255, 255, 255, 255))
for cx, cy in ((412, 360), (612, 360), (412, 530), (612, 530)):
    d.ellipse((cx - 55, cy - 55, cx + 55, cy + 55), fill=(47, 111, 235, 255))
d.rounded_rectangle((370, 680, 654, 740), radius=30, fill=(47, 111, 235, 255))
base.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "app.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
