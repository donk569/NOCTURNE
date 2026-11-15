# -*- coding: utf-8 -*-
"""猫骨链平面 IK 工具。

约定（已用 axes2.py 实测验证）：
- 所有骨骼局部 X 轴 ≈ 世界 +X，roll 均为 0
- 因此所有屈伸都是绕世界 X 的旋转，世界角度可直接线性叠加
- 平面坐标为 (y, z)，角度 = atan2(z, y)，局部 +rx 等价于该平面上 +1 个角度的旋转
"""
import math
from mathutils import Vector

DEG = math.degrees
RAD = math.radians


def wrap(a):
    """把角度归一化到 (-180, 180]"""
    while a > 180.0:
        a -= 360.0
    while a <= -180.0:
        a += 360.0
    return a


class RestInfo:
    """从 armature 读取各骨骼静止姿态"""

    def __init__(self, rig):
        self.bones = {}
        for b in rig.data.bones:
            d = b.tail_local - b.head_local
            self.bones[b.name] = {
                "head": b.head_local.copy(),
                "tail": b.tail_local.copy(),
                "len": d.length,
                "ang": DEG(math.atan2(d.z, d.y)),
            }

    def head_yz(self, name):
        h = self.bones[name]["head"]
        return Vector((0.0, h.y, h.z))

    def ang(self, name):
        return self.bones[name]["ang"]

    def len(self, name):
        return self.bones[name]["len"]


def ik2(p0, p1, l1, l2, prefer_forward):
    """两骨 IK：p0 根、p1 目标点，返回 (第一段世界角, 第二段世界角, 关节点)

    prefer_forward=True 时选择关节点更靠前（-Y）的解。
    """
    v = p1 - p0
    r = v.length
    r = max(min(r, l1 + l2 - 1e-5), abs(l1 - l2) + 1e-5)
    base = math.atan2(v.z, v.y)
    cosA = (l1 * l1 + r * r - l2 * l2) / (2.0 * l1 * r)
    A = math.acos(max(-1.0, min(1.0, cosA)))

    best = None
    for s in (+1.0, -1.0):
        th1 = base + s * A
        joint = p0 + Vector((0.0, math.cos(th1) * l1, math.sin(th1) * l1))
        th2 = math.atan2(p1.z - joint.z, p1.y - joint.y)
        score = joint.y
        if best is None:
            best = (th1, th2, joint, score)
        else:
            better = (score < best[3]) if prefer_forward else (score > best[3])
            if better:
                best = (th1, th2, joint, score)
    return best[0], best[1], best[2]


def hind_leg(rest, root_h, hips_rx, hock_y, hock_z, paw_len=None):
    """后腿：解算 thigh + shin，把 hock（跗关节）放到世界 (hock_y, hock_z)

    返回 dict: thigh.L/R 的 rx（此处返回左腿，右腿由调用方镜像）、以及 foot 世界角
    """
    p0 = rest.head_yz("thigh.L") + Vector((0.0, 0.0, root_h))
    p1 = Vector((0.0, hock_y, hock_z))
    l1 = rest.len("thigh.L")
    l2 = rest.len("shin.L")
    th1, th2, joint = ik2(p0, p1, l1, l2, prefer_forward=True)

    rx_thigh = DEG(th1) - rest.ang("thigh.L") - hips_rx
    rx_shin = DEG(th2) - rest.ang("shin.L") - hips_rx - rx_thigh
    return {
        "rx_thigh": wrap(rx_thigh),
        "rx_shin": wrap(rx_shin),
        "knee": joint,
        "hock": p1,
        "shin_world": DEG(th2),
    }


def front_leg(rest, root_h, chest_rx, wrist_y, wrist_z):
    """前腿：解算 upper_arm + forearm，把 wrist（腕关节）放到世界 (wrist_y, wrist_z)"""
    p0 = rest.head_yz("upper_arm.L") + Vector((0.0, 0.0, root_h))
    p1 = Vector((0.0, wrist_y, wrist_z))
    l1 = rest.len("upper_arm.L")
    l2 = rest.len("forearm.L")
    th1, th2, joint = ik2(p0, p1, l1, l2, prefer_forward=False)

    rx_ua = DEG(th1) - rest.ang("upper_arm.L") - chest_rx
    rx_fa = DEG(th2) - rest.ang("forearm.L") - chest_rx - rx_ua
    return {
        "rx_upper": wrap(rx_ua),
        "rx_fore": wrap(rx_fa),
        "elbow": joint,
        "wrist": p1,
        "fore_world": DEG(th2),
    }


def chain_rx(target_world_ang, rest_ang, parent_sum):
    """由目标世界角反推某根骨的局部 rx"""
    return wrap(target_world_ang - rest_ang - parent_sum)
