"""Generate filter_jar.png: slim case, vanilla-style cap, glass sheen."""
from PIL import Image

SRC = r"C:\Users\kirkxia\.cursor\projects\c-Code-PSM\assets\___1.2-e2099955-dc1c-4297-9c94-175ab07b062d.png"
OUT_PATH = r"c:\Code\PSM\EasierFiltering\Textures\Items\filter_jar.png"
PREVIEW_PATH = r"c:\Code\PSM\EasierFiltering\filter_jar_preview.png"

OUT = (24, 28, 32, 255)
DK = (36, 48, 54, 255)
MD = (52, 66, 74, 255)
STEEL_DK = (84, 90, 96, 255)
STEEL = (116, 122, 128, 255)
STEEL_HI = (132, 152, 158, 255)
STEEL_BR = (176, 184, 188, 255)
GLASS = (32, 46, 54, 255)
GLASS2 = (48, 70, 80, 255)
SHEEN = (176, 220, 224, 255)
TEAL_HI = (150, 214, 206, 255)
NONE = (0, 0, 0, 0)

src = Image.open(SRC).convert("RGBA")
sp = src.load()


def crop(x0, y0, x1, y1):
    w, h = x1 - x0 + 1, y1 - y0 + 1
    im = Image.new("RGBA", (w, h), NONE)
    p = im.load()
    for y in range(h):
        for x in range(w):
            pix = sp[x0 + x, y0 + y]
            p[x, y] = pix if pix[3] >= 128 else NONE
    return im


def blit(dst, src_im, ox, oy, overwrite=True):
    p = src_im.load()
    w, h = src_im.size
    for y in range(h):
        for x in range(w):
            pix = p[x, y]
            if pix[3] < 128:
                continue
            tx, ty = ox + x, oy + y
            if 0 <= tx < 32 and 0 <= ty < 32:
                if overwrite or dst[tx, ty][3] == 0:
                    dst[tx, ty] = pix


img = Image.new("RGBA", (32, 32), NONE)
px = img.load()


def put(x, y, c):
    if 0 <= x < 32 and 0 <= y < 32:
        px[x, y] = c


def fill(x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            put(x, y, c)


# Exact 2x width of vanilla mushroom (through the lower rings).
head = crop(2, 2, 13, 12).resize((24, 11), Image.NEAREST)

# Inner filters keep the mushroom rows
rows = list(range(2, 16)) + list(range(22, 30))
raw = Image.new("RGBA", (12, 22), NONE)
rp = raw.load()
for dy, sy in enumerate(rows):
    for dx, sx in enumerate(range(2, 14)):
        pix = sp[sx, sy]
        rp[dx, dy] = pix if pix[3] >= 128 else NONE
filt = raw.resize((8, 16), Image.NEAREST)

# Head y=1..11. Body tucked 1px under cap: x=5..26, y=12..28
# 2px bezel, window x=7..24, y=12..24

# region MARK: glass chamber
fill(7, 12, 24, 24, GLASS)
fill(7, 12, 10, 15, GLASS2)
# endregion

# region MARK: two filters, shoulders fully in the window
blit(px, filt, 8, 12)
blit(px, filt, 16, 12)
# endregion

# region MARK: simple glass sheen (left gutter only, do not cover filters)
put(7, 13, SHEEN)
put(7, 14, SHEEN)
put(7, 15, TEAL_HI)
put(7, 16, TEAL_HI)
put(8, 13, SHEEN)
# endregion

# region MARK: slim steel sides + base (covers filter bottoms)
for y in range(12, 25):
    put(5, y, STEEL_HI if y < 17 else STEEL)
    put(6, y, DK)
    put(25, y, DK)
    put(26, y, STEEL_DK)

fill(5, 25, 26, 28, STEEL)
fill(6, 25, 25, 25, OUT)
fill(6, 26, 25, 26, STEEL_BR)
fill(6, 27, 25, 27, STEEL)
fill(6, 28, 25, 28, STEEL_DK)
# endregion

# region MARK: vanilla-style head on top
blit(px, head, 4, 1)
# endregion

# region MARK: outline
for y in range(12, 29):
    put(5, y, OUT)
    put(26, y, OUT)
for x in range(5, 27):
    put(x, 28, OUT)
# inner lip (sides + bottom only — keep shoulders)
for y in range(12, 25):
    put(6, y, OUT)
    put(25, y, OUT)
for x in range(6, 26):
    put(x, 24, OUT)

# tuck sides under cap
put(5, 11, OUT)
put(26, 11, OUT)
put(6, 11, STEEL)
put(25, 11, STEEL_DK)

put(5, 28, NONE)
put(26, 28, NONE)
put(6, 28, OUT)
put(25, 28, OUT)
put(5, 27, OUT)
put(26, 27, OUT)
# endregion

centers = [(8, 7), (24, 7), (8, 23), (24, 23)]
print("cell centers:")
for x, y in centers:
    c = px[x, y]
    print(" ", x, y, c, "OK" if c[3] == 255 else "EMPTY")

colors = {px[x, y] for y in range(32) for x in range(32)}
print("unique", len(colors), "semi", sum(1 for c in colors if 0 < c[3] < 255))

img.save(OUT_PATH, "PNG")
img.resize((256, 256), Image.NEAREST).save(PREVIEW_PATH, "PNG")
print("saved", OUT_PATH)
