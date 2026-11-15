from PIL import Image, ImageFilter, ImageEnhance
import numpy as np
import math, os

S = 256

def ring(path, inner, outer, soft, color, alpha=1.0):
    """画一个圆环：inner/outer 是像素半径，soft 是边缘羽化像素"""
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    cx = cy = (S - 1) / 2.0
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    a_in = np.clip((d - inner) / max(0.5, soft), 0, 1)
    a_out = np.clip((outer - d) / max(0.5, soft), 0, 1)
    a = np.clip(a_in * a_out, 0, 1) * alpha
    out = np.zeros((S, S, 4), np.float32)
    out[..., 0] = color[0]
    out[..., 1] = color[1]
    out[..., 2] = color[2]
    out[..., 3] = a
    Image.fromarray((out * 255).astype(np.uint8), "RGBA").save(path)
    print(path, "alpha mean %.3f" % a.mean())

outdir = r"D:/unity/NOCTURNE/Assets/Art/Environment/Bedroom/Textures/UI"
os.makedirs(outdir, exist_ok=True)

# 底环：暗灰细环（轨道）
ring(os.path.join(outdir, "UI_RingTrack.png"), 46, 54, 1.5, (0.55, 0.54, 0.51), 0.55)
# 填充环：旧白，稍粗
ring(os.path.join(outdir, "UI_RingFill.png"), 44, 56, 1.5, (0.86, 0.85, 0.80), 0.95)

# ---------- 镜中画面：玩家身后的人形 ----------
# 构图：旧卧室微弱倒影 + 门旁站着一个僵硬人形（头部/肩/上身可辨）
W, H = 768, 1024
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)

# 背景：昏暗房间的模糊倒影（墙面渐变 + 地板线）
bg = np.zeros((H, W, 3), np.float32)
# 墙面：暗绿灰，上暗下略亮
wallLum = 0.075 + 0.040 * (yy / H)
bg[..., 0] = wallLum * 0.85
bg[..., 1] = wallLum * 1.0
bg[..., 2] = wallLum * 0.88
# 门框（左侧）：更暗的竖条
doorX0, doorX1 = int(W * 0.30), int(W * 0.52)
doorMask = ((xx >= doorX0) & (xx <= doorX1)).astype(np.float32)
bg[..., 0] *= (1.0 - doorMask * 0.55)
bg[..., 1] *= (1.0 - doorMask * 0.55)
bg[..., 2] *= (1.0 - doorMask * 0.55)
# 地板线（下半部）
floorY = int(H * 0.72)
floor = (yy > floorY).astype(np.float32)
bg[..., 0] *= (1.0 - floor * 0.45)
bg[..., 1] *= (1.0 - floor * 0.45)
bg[..., 2] *= (1.0 - floor * 0.45)

# 人形：站在门框位置（门内），头 + 肩 + 上身
# 用椭圆叠加：头（小）、颈（窄）、肩胸（宽椭圆）、下半身融入黑暗
def blob(cx, cy, rx, ry, soft):
    d2 = ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2
    return np.clip((1.0 - d2) / max(0.25, soft), 0, 1)

# 人形亮度略高于背景，形成可辨认的轮廓反差
figure = np.zeros((H, W), np.float32)
figure += blob(W * 0.422, H * 0.298, W * 0.034, H * 0.044, 0.50) * 1.0   # 头（略歪）
figure += blob(W * 0.415, H * 0.365, W * 0.014, H * 0.030, 0.55) * 0.85  # 颈
figure += blob(W * 0.415, H * 0.480, W * 0.075, H * 0.115, 0.70) * 0.92  # 肩+上身
figure += blob(W * 0.415, H * 0.610, W * 0.060, H * 0.110, 0.80) * 0.55  # 下身渐暗
figure = np.clip(figure, 0, 1)
# 整体压暗 + 边缘柔化（模糊一次但保留轮廓）
figImg = Image.fromarray((figure * 255).astype(np.uint8))
figSoft = np.asarray(figImg.filter(ImageFilter.GaussianBlur(2.2))).astype(np.float32) / 255.0
figure = np.maximum(figure * 0.55, figSoft)   # 保留轮廓 + 柔边
figure = np.clip(figure, 0, 1)

# 人形颜色：极暗的灰青（比背景亮一点点、冷一点）
figLum = 0.20 + figure * 0.24
figR, figG, figB = figLum * 0.92, figLum * 1.00, figLum * 1.06

# 合成：背景被人形覆盖（软权重）
w = np.clip(figure, 0, 1)[..., None]
out = bg * (1.0 - w) + np.stack([figR, figG, figB], -1) * w

# 整体调暗 + 颗粒 + 轻微晕影
out *= 0.58
grain = np.random.default_rng(88).normal(0, 0.020, (H, W, 1)).astype(np.float32)
out = out + grain
vig = 1.0 - 0.35 * np.clip(np.sqrt(((xx - W/2)/(W/2))**2 + ((yy - H/2)/(H/2))**2) - 0.55, 0, 1)
out *= vig[..., None]
out = np.clip(out, 0, 1)

img = Image.fromarray((out * 255).astype(np.uint8))
# 轻微整体模糊（镜面感）
img = img.filter(ImageFilter.GaussianBlur(1.0))
path = os.path.join(outdir, "T_Mirror_Figure.png")
img.save(path)
print(path, "mean %.3f" % (np.asarray(img).astype(np.float32).mean() / 255.0))