# MD plugin icons - MD Search to Selection - Author: MD (Mayank Dholariya)
# python3 make_icons.py  -> <NAME>_16/32/256.png, <NAME>.ico, wizard_large.bmp (164x314), wizard_small.bmp (55x55)
# Change NAME, LETTERS, TAGLINE, ACCENT. Keep the MD look: rounded teal tile, white glyph, one accent colour.
from PIL import Image, ImageDraw, ImageFont
NAME = "MDSearchToSelection"; TITLE = "MD Search to Selection"; LETTERS = "SS"; TAGLINE = ["Search Sets to fast", "Selection Sets - Navisworks"]
TEAL = (0, 100, 125); WHITE = (255, 255, 255); ACCENT = (245, 158, 40)

def font(sz, bold=False):
    try: return ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf' % ('-Bold' if bold else ''), sz)
    except Exception: return ImageFont.load_default()

def draw(S):
    # glyph: white magnifier (search) + orange tick (selection) = "search set -> selection set"
    k = 4; s = S * k
    im = Image.new('RGBA', (s, s), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.18), fill=TEAL)
    w = max(int(s * .085), 3)
    cx, cy, r = s * .40, s * .40, s * .22
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=WHITE, width=w)
    d.line([(cx + r * .72, cy + r * .72), (s * .86, s * .86)], fill=WHITE, width=int(w * 1.25))
    t = [(s * .28, s * .40), (s * .37, s * .50), (s * .53, s * .30)]
    d.line(t, fill=ACCENT, width=int(w * .95), joint='curve')
    return im.resize((S, S), Image.LANCZOS)

for S in (16, 32, 256): draw(S).save('%s_%d.png' % (NAME, S))
draw(256).save(NAME + '.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
L = Image.new('RGB', (164, 314), TEAL); d = ImageDraw.Draw(L)
for y in range(314):
    d.line([(0, y), (164, y)], fill=tuple(int(TEAL[i] * (1 - y / 314 * .45)) for i in range(3)))
L.paste(draw(110), (27, 40), draw(110))
fs = 17
while fs > 9 and d.textlength(TITLE, font=font(fs, True)) > 154: fs -= 1
d.text((82, 180), TITLE, fill=WHITE, font=font(fs, True), anchor='mm')
for i, t in enumerate(TAGLINE):
    ts = 11
    while ts > 8 and d.textlength(t, font=font(ts)) > 156: ts -= 1
    d.text((82, 204 + 18 * i), t, fill=(215, 235, 240), font=font(ts), anchor='mm')
d.text((82, 296), "MD (Mayank Dholariya)", fill=(190, 215, 225), font=font(9), anchor='mm')
L.save('wizard_large.bmp')
Sm = Image.new('RGB', (55, 55), WHITE); Sm.paste(draw(48), (3, 3), draw(48)); Sm.save('wizard_small.bmp')
