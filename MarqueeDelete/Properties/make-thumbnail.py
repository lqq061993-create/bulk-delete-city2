"""Generate Properties/Thumbnail.png (1024x1024) for Paradox Mods.

Run:  py -3 Properties/make-thumbnail.py
"""
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import os

W = H = 1024
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Thumbnail.png")


def load_font(size, bold=False):
    cands = [
        "C:/Windows/Fonts/msyhbd.ttc" if bold else "C:/Windows/Fonts/msyh.ttc",
        "C:/Windows/Fonts/simhei.ttf",
        "C:/Windows/Fonts/seguisb.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf",
    ]
    for c in cands:
        if os.path.exists(c):
            try:
                return ImageFont.truetype(c, size)
            except OSError:
                pass
    return ImageFont.load_default()


# 1. Deep night city-blueprint background with soft radial glows
base = Image.new("RGBA", (W, H), (12, 16, 24, 255))
bg_draw = ImageDraw.Draw(base, "RGBA")
for y in range(H):
    t = y / (H - 1)
    r = int(12 + 12 * t)
    g = int(17 + 20 * t)
    b = int(28 + 34 * t)
    bg_draw.line([(0, y), (W, y)], fill=(r, g, b, 255))

glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow, "RGBA")
gd.ellipse([-180, -160, 560, 520], fill=(235, 58, 52, 95))
gd.ellipse([480, 260, 1180, 960], fill=(38, 152, 235, 85))
gd.ellipse([180, 420, 840, 1060], fill=(56, 220, 118, 55))
glow = glow.filter(ImageFilter.GaussianBlur(110))
img = Image.alpha_composite(base, glow)
d = ImageDraw.Draw(img, "RGBA")

# Subtle translucent background city grid via alpha_composite
grid_ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
gd_grid = ImageDraw.Draw(grid_ov)
for x in range(0, W, 64):
    gd_grid.line([(x, 0), (x, H)], fill=(140, 180, 235, 18), width=1)
for y in range(0, H, 64):
    gd_grid.line([(0, y), (W, y)], fill=(140, 180, 235, 18), width=1)
img = Image.alpha_composite(img, grid_ov)
d = ImageDraw.Draw(img)

MARGIN = 54

# 2. Top Header Section
f_badge = load_font(20, True)
f_title_zh = load_font(62, True)
f_title_en = load_font(34, True)
f_sub = load_font(22, False)

# Top tag pill (auto-sized to text)
badge_txt = "CITIES: SKYLINES II · 推土机增强模组"
badge_w = int(d.textlength(badge_txt, font=f_badge)) + 34
d.rounded_rectangle([MARGIN, 42, MARGIN + badge_w, 78], radius=8,
                    fill=(232, 64, 52, 52), outline=(255, 116, 102, 215), width=2)
d.text((MARGIN + 17, 46), badge_txt, font=f_badge, fill=(255, 222, 214, 255))

# Version pill right
ver_txt = "v2.2.0 · 1.6.x"
ver_w = int(d.textlength(ver_txt, font=f_badge)) + 34
d.rounded_rectangle([W - MARGIN - ver_w, 42, W - MARGIN, 78], radius=8,
                    fill=(32, 148, 218, 60), outline=(88, 204, 255, 215), width=2)
d.text((W - MARGIN - ver_w + 17, 46), ver_txt, font=f_badge, fill=(224, 246, 255, 255))

# Main Title
d.rectangle([MARGIN, 96, MARGIN + 10, 218], fill=(255, 88, 76, 255))
d.text((MARGIN + 28, 88), "框选批量删除", font=f_title_zh, fill=(255, 255, 255, 255))
d.text((MARGIN + 418, 108), "MARQUEE DELETE", font=f_title_en, fill=(255, 122, 108, 255))
d.text((MARGIN + 28, 168), "贴边长宽标尺 · 住/商/工/办四色精准筛选 · 重要建筑弹窗保护 · 地表/地下隔离",
       font=f_sub, fill=(196, 214, 236, 255))

# Zone Filter Legend Pills Row
zones = [
    ("住宅 Residential", (59, 232, 117), (22, 88, 46)),
    ("商业 Commercial", (69, 178, 255), (22, 68, 104)),
    ("工业 Industrial", (255, 201, 51), (98, 74, 16)),
    ("办公 Office", (189, 107, 255), (72, 36, 104)),
    ("★ 重要建筑保护", (255, 212, 74), (102, 42, 58)),
]
f_pill = load_font(18, True)
px = MARGIN
py = 240
for label, fg, bg in zones:
    tw = int(d.textlength(label, font=f_pill))
    pw = tw + 36
    d.rounded_rectangle([px, py, px + pw, py + 38], radius=8,
                        fill=(bg[0], bg[1], bg[2], 215), outline=(fg[0], fg[1], fg[2], 240), width=2)
    d.ellipse([px + 11, py + 13, px + 23, py + 25], fill=(fg[0], fg[1], fg[2], 255))
    d.text((px + 28, py + 7), label, font=f_pill, fill=(248, 252, 255, 255))
    px += pw + 12

# 3. Hero Interactive Marquee Box Visualization (Center)
bx0, by0 = 76, 312
bx1, by1 = W - 128, 792

ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
od = ImageDraw.Draw(ov, "RGBA")
od.rectangle([bx0, by0, bx1, by1], fill=(224, 48, 42, 40))

# Internal 8m Cell Grid Lines inside the marquee box
step_x = (bx1 - bx0) / 10.0
step_y = (by1 - by0) / 6.0
for i in range(1, 10):
    gx = int(bx0 + i * step_x)
    od.line([(gx, by0), (gx, by1)], fill=(255, 118, 102, 42), width=1)
for j in range(1, 6):
    gy = int(by0 + j * step_y)
    od.line([(bx0, gy), (bx1, gy)], fill=(255, 118, 102, 42), width=1)

# Colored building footprints inside the marquee
buildings = [
    # Residential (Green)
    (bx0 + 32, by0 + 32, 112, 74, (59, 232, 117), "住宅"),
    (bx0 + 160, by0 + 32, 96, 74, (59, 232, 117), "住宅"),
    (bx0 + 32, by0 + 122, 102, 82, (59, 232, 117), "住宅"),
    (bx0 + 148, by0 + 122, 108, 82, (59, 232, 117), "住宅"),
    # Commercial (Blue)
    (bx0 + 284, by0 + 32, 128, 86, (69, 178, 255), "商业"),
    (bx0 + 428, by0 + 32, 116, 86, (69, 178, 255), "商业"),
    (bx0 + 284, by0 + 134, 120, 70, (69, 178, 255), "商业"),
    # Industrial (Yellow)
    (bx0 + 32, by0 + 232, 146, 106, (255, 201, 51), "工业"),
    (bx0 + 194, by0 + 232, 132, 106, (255, 201, 51), "工业"),
    (bx0 + 32, by0 + 354, 160, 88, (255, 201, 51), "工业"),
    # Office (Purple)
    (bx0 + 572, by0 + 32, 118, 96, (189, 107, 255), "办公"),
    (bx0 + 704, by0 + 32, 92, 96, (189, 107, 255), "办公"),
    (bx0 + 572, by0 + 144, 224, 84, (189, 107, 255), "办公"),
    (bx0 + 212, by0 + 354, 168, 88, (189, 107, 255), "办公"),
]

f_bld = load_font(17, True)
for rx, ry, rw, rh, col, tag in buildings:
    od.rounded_rectangle([rx, ry, rx + rw, ry + rh], radius=6,
                         fill=(col[0], col[1], col[2], 82),
                         outline=(col[0], col[1], col[2], 238), width=3)
    tw = d.textlength(tag, font=f_bld)
    od.text((rx + (rw - tw) / 2, ry + (rh - 22) / 2), tag, font=f_bld, fill=(255, 255, 255, 240))

# Important / Landmark Building with Gold + Magenta Double Pulsing Frame & Cross
ix, iy, iw, ih = bx0 + 408, by0 + 250, 388, 196
od.rounded_rectangle([ix - 6, iy - 6, ix + iw + 6, iy + ih + 6], radius=10,
                     fill=(0, 0, 0, 0), outline=(255, 64, 140, 220), width=3)
od.rounded_rectangle([ix, iy, ix + iw, iy + ih], radius=8,
                     fill=(255, 196, 46, 82), outline=(255, 218, 76, 255), width=4)
od.line([(ix + 8, iy + 8), (ix + iw - 8, iy + ih - 8)], fill=(255, 218, 76, 130), width=2)
od.line([(ix + iw - 8, iy + 8), (ix + 8, iy + ih - 8)], fill=(255, 218, 76, 130), width=2)

# Modal callout badge inside the Important Building
f_imp_title = load_font(21, True)
f_imp_sub = load_font(16, False)
od.rounded_rectangle([ix + 16, iy + 28, ix + iw - 16, iy + ih - 28], radius=8,
                     fill=(16, 20, 30, 242), outline=(255, 208, 68, 255), width=2)
od.text((ix + 32, iy + 42), "★ 服务 / 独特 / 重点建筑保护", font=f_imp_title, fill=(255, 224, 102, 255))
od.text((ix + 32, iy + 76), "松手自动拦截 · 弹窗列出建筑名称二次确认", font=f_imp_sub, fill=(224, 236, 250, 255))
od.rounded_rectangle([ix + 32, iy + 114, ix + 158, iy + 146], radius=5,
                     fill=(52, 62, 78, 255), outline=(160, 178, 204, 220), width=1)
od.text((ix + 60, iy + 119), "取消保留", font=f_bld, fill=(235, 242, 255, 255))
od.rounded_rectangle([ix + 174, iy + 114, ix + 346, iy + 146], radius=5,
                     fill=(218, 48, 38, 255), outline=(255, 168, 156, 240), width=1)
od.text((ix + 204, iy + 119), "确认强制删除", font=f_bld, fill=(255, 255, 255, 255))

# Dual-layer smooth crimson marquee border + L-corners
od.rectangle([bx0 - 3, by0 - 3, bx1 + 3, by1 + 3], outline=(255, 92, 78, 95), width=6)
od.rectangle([bx0, by0, bx1, by1], outline=(255, 96, 82, 245), width=3)

CL, CW = 38, 6
for cx, cy, sx, sy in [(bx0, by0, 1, 1), (bx1, by0, -1, 1), (bx0, by1, 1, -1), (bx1, by1, -1, -1)]:
    od.line([(cx, cy), (cx + sx * CL, cy)], fill=(255, 232, 226, 255), width=CW)
    od.line([(cx, cy), (cx, cy + sy * CL)], fill=(255, 232, 226, 255), width=CW)

# 4. Border-Aligned Dimension Badges (flush against bottom horizontal border & right vertical border!)
f_dim = load_font(20, True)
f_dim_m = load_font(17, False)

# Bottom border Width badge: "75 格 (600m)"
w_center_x = (bx0 + bx1) // 2
w_edge_y = by1 + 26
od.rounded_rectangle([w_center_x - 102, w_edge_y - 19, w_center_x + 102, w_edge_y + 19], radius=7,
                     fill=(18, 22, 30, 245), outline=(255, 112, 92, 245), width=2)
od.text((w_center_x - 82, w_edge_y - 13), "75 格", font=f_dim, fill=(255, 255, 255, 255))
od.text((w_center_x - 16, w_edge_y - 11), "(600m)", font=f_dim_m, fill=(255, 206, 196, 255))

# Right border Depth badge: "38 格 (304m)"
d_edge_x = bx1 + 60
d_center_y = (by0 + by1) // 2
od.rounded_rectangle([d_edge_x - 54, d_center_y - 32, d_edge_x + 54, d_center_y + 32], radius=7,
                     fill=(18, 22, 30, 245), outline=(255, 112, 92, 245), width=2)
od.text((d_edge_x - 34, d_center_y - 25), "38 格", font=f_dim, fill=(255, 255, 255, 255))
od.text((d_edge_x - 38, d_center_y + 2), "(304m)", font=f_dim_m, fill=(255, 206, 196, 255))

img = Image.alpha_composite(img, ov)
d = ImageDraw.Draw(img, "RGBA")

# 5. Bottom Co-Creator Credits Banner
f_credit_hdr = load_font(17, True)
f_credit_team = load_font(17, True)

d.rounded_rectangle([MARGIN, 854, W - MARGIN, 972], radius=12,
                    fill=(18, 24, 36, 238), outline=(82, 138, 210, 175), width=2)
d.text((MARGIN + 22, 868), "AI 联合造城施工队 · CO-CREATED BY THREE AI ARCHITECTS",
       font=f_credit_hdr, fill=(116, 198, 255, 255))

team_line = "★ 大肥鱼 (DeepSeekv4.1Flash1017)  ×  ★ 哈基米 (Gemini4Argon+Hydrogen)  ×  ★ GPT (gpt6.1sol / 6.2)"
# Auto-fit team_line inside banner width
avail_w = (W - MARGIN * 2) - 44
sz = 18
while sz > 12:
    f_credit_team = load_font(sz, True)
    if d.textlength(team_line, font=f_credit_team) <= avail_w:
        break
    sz -= 1
d.text((MARGIN + 22, 902), team_line, font=f_credit_team, fill=(255, 255, 255, 255))

f_sub_credit = load_font(16, False)
d.text((MARGIN + 22, 936), "支持随镜头旋转框选 · 级联附属清理 · 差集高亮丝滑帧率 · 中英双语自适应",
       font=f_sub_credit, fill=(176, 196, 220, 255))

img.convert("RGB").save(OUT, "PNG", optimize=True)
print(f"Saved {OUT} ({img.size[0]}x{img.size[1]}, {os.path.getsize(OUT):,} bytes, team_font_sz={sz})")
