from PIL import Image

src = Image.open(
    r"C:\Users\kirkxia\.cursor\projects\c-Code-PSM\assets\___1.2-e2099955-dc1c-4297-9c94-175ab07b062d.png"
).convert("RGBA")
sp = src.load()

# Crop original content x=2..13, y=2..29, drop 4 duplicate cylinder rows (orig 18-21)
rows = list(range(2, 18)) + list(range(22, 30))  # 24 rows
cols = list(range(2, 14))  # 12 cols
filt = Image.new("RGBA", (12, 24), (0, 0, 0, 0))
fp = filt.load()
for dy, sy in enumerate(rows):
    for dx, sx in enumerate(cols):
        p = sp[sx, sy]
        fp[dx, dy] = p if p[3] >= 128 else (0, 0, 0, 0)

# Palette — jar matches filter_tool steel/teal; filters keep vanilla colors
OUT = (24, 28, 32, 255)
DK = (36, 48, 54, 255)
MD = (52, 66, 74, 255)
STEEL_DK = (84, 90, 96, 255)
STEEL = (116, 122, 128, 255)
STEEL_HI = (132, 152, 158, 255)
STEEL_BR = (176, 184, 188, 255)
STEEL_W = (208, 214, 218, 255)
TEAL_DK = (36, 102, 100, 255)
TEAL = (67, 170, 165, 255)
TEAL_HI = (150, 214, 206, 255)
GOLD = (196, 164, 68, 255)
GOLD_DK = (148, 120, 40, 255)
GOLD_SH = (92, 72, 24, 255)
GLASS_BG = (28, 40, 48, 255)
GLASS_HI = (90, 150, 158, 255)
GLASS_SH = (22, 30, 36, 255)
NONE = (0, 0, 0, 0)

img = Image.new("RGBA", (32, 32), NONE)
px = img.load()


def fill(x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            px[x, y] = c


def hline(y, x0, x1, c):
    for x in range(x0, x1 + 1):
        px[x, y] = c


def vline(x, y0, y1, c):
    for y in range(y0, y1 + 1):
        px[x, y] = c


# --- body fill: glass cavity ---
fill(2, 5, 29, 28, GLASS_BG)

# --- blit two filters ---
def blit_filter(ox, oy):
    for y in range(24):
        for x in range(12):
            p = fp[x, y]
            if p[3] >= 128:
                px[ox + x, oy + y] = p


blit_filter(4, 5)
blit_filter(16, 5)

# --- left / right metal frame + glass edge ---
for y in range(5, 29):
    px[1, y] = STEEL_BR if y in (6, 7) else STEEL
    px[2, y] = TEAL_HI if y <= 9 else (GLASS_HI if y <= 16 else STEEL)
    px[29, y] = DK
    px[30, y] = MD

# --- lid y=0..4 ---
hline(0, 0, 31, OUT)
hline(1, 1, 30, STEEL_W)
hline(2, 1, 30, STEEL_BR)
hline(3, 1, 30, STEEL)
hline(4, 1, 30, OUT)

put = lambda x, y, c: px.__setitem__((x, y), c)

put(1, 1, STEEL_W)
put(30, 1, STEEL)
put(1, 2, STEEL_BR)
put(30, 2, STEEL_DK)
put(1, 3, STEEL)
put(30, 3, MD)

# lid bevel: right side darker
for x in range(24, 30):
    px[x, 1] = STEEL_BR
    px[x, 2] = STEEL
    px[x, 3] = STEEL_DK

# latch / handle in lid center
for x in range(12, 20):
    px[x, 1] = GOLD
    px[x, 2] = GOLD_DK
    px[x, 3] = GOLD_SH
put(12, 1, OUT)
put(19, 1, OUT)
put(12, 2, OUT)
put(19, 2, OUT)
put(12, 3, OUT)
put(19, 3, DK)
put(13, 1, STEEL_W)
put(14, 1, GOLD)

# teal LED on right of lid
put(25, 2, TEAL_DK)
put(26, 2, TEAL_HI)
put(27, 2, TEAL)
put(25, 3, TEAL_DK)
put(26, 3, TEAL)
put(27, 3, TEAL_DK)

# left rivet
put(2, 2, DK)
put(3, 2, STEEL_DK)
put(2, 3, MD)
put(3, 3, DK)

# --- base y=29..31 ---
hline(29, 1, 30, STEEL)
hline(30, 1, 30, STEEL_DK)
hline(31, 0, 31, OUT)
for x in range(20, 31):
    px[x, 29] = STEEL_DK
    px[x, 30] = MD
put(1, 29, STEEL_HI)
put(1, 30, STEEL)
put(30, 29, MD)
put(30, 30, DK)

# gold strip on base
hline(29, 10, 21, GOLD)
hline(30, 10, 21, GOLD_DK)
put(10, 29, OUT)
put(21, 29, OUT)

# --- corner brackets on glass ---
put(2, 5, OUT)
put(3, 5, STEEL_BR)
put(4, 5, STEEL)
put(2, 6, STEEL_BR)
put(2, 7, STEEL)

put(29, 5, OUT)
put(28, 5, STEEL)
put(27, 5, STEEL_DK)
put(29, 6, STEEL_DK)
put(29, 7, MD)

put(2, 28, OUT)
put(3, 28, STEEL)
put(4, 28, STEEL_DK)
put(2, 27, STEEL)
put(2, 26, STEEL_DK)

put(29, 28, OUT)
put(28, 28, MD)
put(27, 28, DK)
put(29, 27, MD)
put(29, 26, DK)

# outer outline last
hline(0, 0, 31, OUT)
hline(31, 0, 31, OUT)
vline(0, 0, 31, OUT)
vline(31, 0, 31, OUT)

# Unity GetPixel y=0 is bottom; PIL y=0 is top
# top-left cell center: Unity (8, 24) -> PIL (8, 7)
centers = [(8, 7), (24, 7), (8, 23), (24, 23)]
print("cell centers (PIL):")
for x, y in centers:
    print(x, y, px[x, y])

colors = set()
for y in range(32):
    for x in range(32):
        colors.add(px[x, y])
semis = [c for c in colors if 0 < c[3] < 255]
print("unique", len(colors), "semi", len(semis))

out = r"c:\Code\PSM\EasierFiltering\Textures\Items\filter_jar.png"
img.save(out, "PNG")
preview = img.resize((256, 256), Image.NEAREST)
prev_path = r"c:\Code\PSM\EasierFiltering\Textures\Items\filter_jar_preview.png"
preview.save(prev_path, "PNG")
print("saved", out, img.size)
