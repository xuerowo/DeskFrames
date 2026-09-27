"""產生 app.ico（多尺寸）。執行：python tools/make_icon.py"""
from PIL import Image, ImageDraw
import os

def render(size: int) -> Image.Image:
    s = 4  # supersampling
    W = size * s
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # 背景：圓角方塊漸層
    grad = Image.new("RGBA", (W, W))
    gd = ImageDraw.Draw(grad)
    top, bot = (91, 140, 255), (124, 77, 255)
    for y in range(W):
        t = y / (W - 1)
        c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)) + (255,)
        gd.line([(0, y), (W, y)], fill=c)
    mask = Image.new("L", (W, W), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, W - 1, W - 1], radius=int(W * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    # 框線（L 形）與標籤
    lw = max(int(W * 0.06), 2)
    m = W * 0.2
    pts = [(m, W * 0.36), (W * 0.62, W * 0.36), (W * 0.62, W * 0.56), (W - m, W * 0.56),
           (W - m, W - m), (m, W - m)]
    d.line(pts + [pts[0]], fill=(255, 255, 255, 255), width=lw, joint="curve")
    # 標籤
    d.rounded_rectangle([m - lw / 2, W * 0.2, W * 0.5, W * 0.36 + lw / 2],
                        radius=int(W * 0.05), fill=(255, 255, 255, 255))
    return img.resize((size, size), Image.LANCZOS)

here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, "..", "app.ico")
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
imgs = [render(sz) for sz in sizes]
imgs[-1].save(out, format="ICO", sizes=[(sz, sz) for sz in sizes], append_images=imgs[:-1])
print("wrote", os.path.abspath(out))
