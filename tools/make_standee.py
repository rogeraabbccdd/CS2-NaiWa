"""
Generate the NaiWa cardboard standee model sources for CS2 ModelDoc.

Input : a render of the character on a white background.
Output: assets/content/{models,materials}/naiwa/...  (copy into content/csgo_addons/<addon>/)

The standee is the character silhouette plus a white cut margin, extruded to a
thin board, with a kickstand on the back. Height is the CS2 standing player
hull height (72 units).
"""
import math
import os
import sys

import cv2
import numpy as np

SRC = sys.argv[1] if len(sys.argv) > 1 else "assets/source/naiwa.jpg"
OUT = sys.argv[2] if len(sys.argv) > 2 else "assets/content"

HEIGHT = 72.0          # standing player hull height
THICKNESS = 0.5        # board thickness
BORDER_RATIO = 0.025   # white cut margin, relative to character height
STAND_HEIGHT = 40.0    # kickstand height on the back
STAND_DEPTH = 16.0     # kickstand reach behind the board
STAND_THICK = 0.75
PHYS_THICK = 2.0       # collision hull thickness
TEX_W, TEX_H = 1024, 2048


def character_mask(img):
    hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
    bg = (hsv[..., 1] < 70) & (hsv[..., 2] > 170)
    fg = (~bg).astype(np.uint8) * 255
    fg = cv2.morphologyEx(fg, cv2.MORPH_OPEN, np.ones((9, 9), np.uint8))
    n, labels, stats, _ = cv2.connectedComponentsWithStats(fg)
    biggest = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    fg = np.where(labels == biggest, 255, 0).astype(np.uint8)
    return fill_holes(fg)


def fill_holes(mask):
    h, w = mask.shape
    flood = mask.copy()
    ff = np.zeros((h + 2, w + 2), np.uint8)
    cv2.floodFill(flood, ff, (0, 0), 255)
    return mask | cv2.bitwise_not(flood)


def border_mask(char, radius):
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * radius + 1, 2 * radius + 1))
    m = cv2.dilate(char, k)
    m = cv2.GaussianBlur(m, (0, 0), radius / 2)  # round off the outline
    m = np.where(m > 127, 255, 0).astype(np.uint8)
    return fill_holes(m)


def polygon_area(p):
    x, y = p[:, 0], p[:, 1]
    return 0.5 * float(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))


def ear_clip(poly):
    """Triangulate a simple CCW polygon (y up). Returns index triples."""
    idx = list(range(len(poly)))
    tris = []

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    def inside(p, a, b, c):
        return cross(a, b, p) >= 0 and cross(b, c, p) >= 0 and cross(c, a, p) >= 0

    guard = 0
    while len(idx) > 3 and guard < 100000:
        guard += 1
        n = len(idx)
        for k in range(n):
            i0, i1, i2 = idx[(k - 1) % n], idx[k], idx[(k + 1) % n]
            a, b, c = poly[i0], poly[i1], poly[i2]
            if cross(a, b, c) <= 1e-9:
                continue
            if any(inside(poly[j], a, b, c) for j in idx if j not in (i0, i1, i2)):
                continue
            tris.append((i0, i1, i2))
            idx.pop(k)
            break
        else:
            raise RuntimeError("ear clipping failed; polygon not simple?")
    tris.append(tuple(idx))
    return tris


class Smd:
    def __init__(self):
        self.tris = []

    def tri(self, mat, verts):
        """verts: 3 x (pos, normal, uv), counter-clockwise seen from the front."""
        self.tris.append((mat, verts))

    def quad(self, mat, p, n, uv):
        self.tri(mat, [(p[0], n, uv[0]), (p[1], n, uv[1]), (p[2], n, uv[2])])
        self.tri(mat, [(p[0], n, uv[0]), (p[2], n, uv[2]), (p[3], n, uv[3])])

    def save(self, path):
        with open(path, "w", newline="\n") as f:
            f.write("version 1\nnodes\n  0 \"root\" -1\nend\nskeleton\n  time 0\n    0 0 0 0 0 0 0\nend\ntriangles\n")
            for mat, verts in self.tris:
                f.write(mat + "\n")
                for p, n, uv in verts:
                    f.write("  0 %.6f %.6f %.6f %.6f %.6f %.6f %.6f %.6f 0\n" % (*p, *n, *uv))
            f.write("end\n")


def extrude(smd, poly2d, tris, x_front, x_back, front_mat, back_mat, edge_mat, front_uv=None):
    """poly2d: CCW (y, z) points. Board lies in the YZ plane facing +X."""
    for a, b, c in tris:
        pts = [poly2d[a], poly2d[b], poly2d[c]]
        uvs = [front_uv(p) if front_uv else (0, 0) for p in pts]
        # Front (+X): viewer at +X sees +Y to the right, so (y,z) CCW is CCW on screen.
        smd.tri(front_mat, [((x_front, p[0], p[1]), (1, 0, 0), uv) for p, uv in zip(pts, uvs)])
        back = [((x_back, p[0], p[1]), (-1, 0, 0), (p[0] / 16.0, p[1] / 16.0)) for p in pts]
        smd.tri(back_mat, [back[0], back[2], back[1]])
    n = len(poly2d)
    for i in range(n):
        p0, p1 = poly2d[i], poly2d[(i + 1) % n]
        dy, dz = p1[0] - p0[0], p1[1] - p0[1]
        ln = math.hypot(dy, dz) or 1.0
        nrm = (0.0, dz / ln, -dy / ln)  # outward for a CCW loop
        smd.quad(edge_mat,
                 [(x_back, p0[0], p0[1]), (x_back, p1[0], p1[1]), (x_front, p1[0], p1[1]), (x_front, p0[0], p0[1])],
                 nrm, [(0, 0), (ln / 16, 0), (ln / 16, 0.05), (0, 0.05)])


def box(smd, mat, lo, hi):
    (x0, y0, z0), (x1, y1, z1) = lo, hi
    faces = [
        ((1, 0, 0), [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)]),
        ((-1, 0, 0), [(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)]),
        ((0, 1, 0), [(x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1)]),
        ((0, -1, 0), [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)]),
        ((0, 0, 1), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]),
        ((0, 0, -1), [(x0, y1, z0), (x1, y1, z0), (x1, y0, z0), (x0, y0, z0)]),
    ]
    for n, p in faces:
        smd.quad(mat, p, n, [(0, 0), (1, 0), (1, 1), (0, 1)])


def kickstand(smd, mat):
    """Triangle in the XZ plane behind the board, extruded along Y."""
    xb = -THICKNESS / 2
    tri = [(xb, 0.0), (xb - STAND_DEPTH, 0.0), (xb, STAND_HEIGHT)]  # (x, z)
    hy = STAND_THICK / 2
    # +Y side: viewer at +Y facing -Y sees -X to the right
    for y, n, order in ((hy, (0, 1, 0), (0, 1, 2)), (-hy, (0, -1, 0), (0, 2, 1))):
        smd.tri(mat, [((tri[i][0], y, tri[i][1]), n, (tri[i][0] / 16, tri[i][1] / 16)) for i in order])
    for i in range(3):
        (x0, z0), (x1, z1) = tri[i], tri[(i + 1) % 3]
        # triangle is CW in (x,z) as listed, so outward normal is the left-hand side
        dx, dz = x1 - x0, z1 - z0
        ln = math.hypot(dx, dz)
        nrm = (-dz / ln, 0.0, dx / ln)
        smd.quad(mat, [(x0, -hy, z0), (x1, -hy, z1), (x1, hy, z1), (x0, hy, z0)], nrm,
                 [(0, 0), (ln / 16, 0), (ln / 16, 0.05), (0, 0.05)])


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", newline="\n", encoding="utf-8") as f:
        f.write(text)


VMAT = """// THIS FILE IS AUTO-GENERATED

Layer0
{{
	shader "csgo_complex.vfx"

	//---- Color ----
	g_flModelTintAmount "1.000"
	g_vColorTint "[1.000000 1.000000 1.000000 0.000000]"
	TextureColor "{color}"

	//---- Fog ----
	g_bFogEnabled "1"

	//---- Lighting ----
	g_flMetalness "0.000"
	TextureRoughness "materials/default/default_rough.tga"

	//---- Normal Map ----
	TextureNormal "materials/default/default_normal.tga"

	//---- Texture Address Mode ----
	g_nTextureAddressModeU "{addr}"
	g_nTextureAddressModeV "{addr}"
}}
"""

VMDL = """<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:modeldoc39:version{{1ceecb0d-4093-4729-ab18-b76886c64cde}} -->
{{
	rootNode =
	{{
		_class = "RootNode"
		children =
		[
			{{
				_class = "RenderMeshList"
				children =
				[
					{{
						_class = "RenderMeshFile"
						filename = "models/naiwa/naiwa_standee/naiwa_standee.smd"
						import_scale = 1.0
						import_filter =
						{{
							exclude_by_default = false
							exception_list = [  ]
						}}
					}},
				]
			}},
			{{
				_class = "MaterialGroupList"
				children =
				[
					{{
						_class = "DefaultMaterialGroup"
						remaps =
						[
							{{
								from = "naiwa_front.vmat"
								to = "materials/naiwa/naiwa_front.vmat"
							}},
							{{
								from = "naiwa_back.vmat"
								to = "materials/naiwa/naiwa_back.vmat"
							}},
						]
						use_global_default = false
						global_default_material = ""
					}},
				]
			}},
			{{
				_class = "PhysicsShapeList"
				children =
				[
					{{
						_class = "PhysicsHullFile"
						name = "naiwa_standee_phys"
						parent_bone = ""
						surface_prop = "cardboard"
						collision_prop = "default"
						tool_material = ""
						recenter_on_parent_bone = false
						offset_origin = [ 0.0, 0.0, 0.0 ]
						offset_angles = [ 0.0, 0.0, 0.0 ]
						filename = "models/naiwa/naiwa_standee/naiwa_standee_phys.smd"
						import_scale = 1.0
						faceMergeAngle = 10.0
						maxHullVertices = 0
						import_mode = "SingleHull"
						small_element_threshold = 0.0
						thin_element_threshold = 0.0
						optimization_algorithm = "QEM"
						disable_region_svm = false
						import_filter =
						{{
							exclude_by_default = false
							exception_list = [  ]
						}}
					}},
				]
				leave_body_collision_unmodified = false
			}},
		]
		model_archetype = ""
		primary_associated_entity = ""
		anim_graph_name = ""
		document_sub_type = "ModelDocSubType_None"
	}}
}}
"""


def main():
    img = cv2.imread(SRC)
    if img is None:
        sys.exit("cannot read " + SRC)

    char = character_mask(img)
    ys, xs = np.nonzero(char)
    radius = max(4, int((ys.max() - ys.min()) * BORDER_RATIO))
    pad = radius * 3
    img = cv2.copyMakeBorder(img, pad, pad, pad, pad, cv2.BORDER_CONSTANT, value=(255, 255, 255))
    char = cv2.copyMakeBorder(char, pad, pad, pad, pad, cv2.BORDER_CONSTANT, value=0)
    board = border_mask(char, radius)

    # Printed face: character over white, so shadows inside the margin disappear.
    alpha = cv2.GaussianBlur(char, (0, 0), 1.2).astype(np.float32)[..., None] / 255.0
    face = (img.astype(np.float32) * alpha + 255.0 * (1 - alpha)).astype(np.uint8)

    x, y, w, h = cv2.boundingRect(board)
    s = min(TEX_W / w, TEX_H / h)
    ox, oy = (TEX_W - w * s) / 2, (TEX_H - h * s) / 2
    tex = np.full((TEX_H, TEX_W, 3), 255, np.uint8)
    crop = cv2.resize(face[y:y + h, x:x + w], (round(w * s), round(h * s)), interpolation=cv2.INTER_AREA)
    tex[round(oy):round(oy) + crop.shape[0], round(ox):round(ox) + crop.shape[1]] = crop

    contours, _ = cv2.findContours(board, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    contour = max(contours, key=cv2.contourArea)
    contour = cv2.approxPolyDP(contour, 0.0015 * h, True)[:, 0, :].astype(np.float64)

    unit = HEIGHT / h  # units per source pixel
    cx = x + w / 2
    bottom = y + h

    def to_yz(px):
        return ((px[0] - cx) * unit, (bottom - px[1]) * unit)

    poly = np.array([to_yz(p) for p in contour])
    if polygon_area(poly) < 0:
        poly = poly[::-1]
    tris = ear_clip(poly)

    def front_uv(p):
        px = p[0] / unit + cx
        py = bottom - p[1] / unit
        u = (ox + (px - x) * s) / TEX_W
        v = (oy + (py - y) * s) / TEX_H
        return (u, 1.0 - v)  # SMD v is bottom-up

    mdl_dir = os.path.join(OUT, "models", "naiwa", "naiwa_standee")
    mat_dir = os.path.join(OUT, "materials", "naiwa")
    os.makedirs(mdl_dir, exist_ok=True)
    os.makedirs(mat_dir, exist_ok=True)

    smd = Smd()
    extrude(smd, poly, tris, THICKNESS / 2, -THICKNESS / 2, "naiwa_front", "naiwa_back", "naiwa_back", front_uv)
    kickstand(smd, "naiwa_back")
    smd.save(os.path.join(mdl_dir, "naiwa_standee.smd"))

    phys = Smd()
    extrude(phys, poly, tris, PHYS_THICK / 2, -PHYS_THICK / 2, "phys", "phys", "phys")
    phys.save(os.path.join(mdl_dir, "naiwa_standee_phys.smd"))

    cv2.imwrite(os.path.join(mat_dir, "naiwa_front_color.png"), tex)
    rng = np.random.default_rng(7)
    kraft = np.empty((256, 256, 3), np.float32)
    kraft[:] = (95, 140, 180)  # BGR kraft cardboard
    kraft += rng.normal(0, 6, (256, 256, 1))
    kraft += cv2.GaussianBlur(rng.normal(0, 10, (256, 256)).astype(np.float32), (0, 0), 3)[..., None]
    cv2.imwrite(os.path.join(mat_dir, "naiwa_back_color.png"), np.clip(kraft, 0, 255).astype(np.uint8))

    write(os.path.join(mat_dir, "naiwa_front.vmat"), VMAT.format(color="materials/naiwa/naiwa_front_color.png", addr=1))
    write(os.path.join(mat_dir, "naiwa_back.vmat"), VMAT.format(color="materials/naiwa/naiwa_back_color.png", addr=0))
    write(os.path.join(mdl_dir, "..", "naiwa_standee.vmdl"), VMDL.format())

    width = w * unit
    print(f"polygon verts={len(poly)} tris={len(tris)} size={width:.1f}w x {HEIGHT:.1f}h units, border={radius}px")

    # Debug preview: board outline over the texture
    prev = tex.copy()
    pts = np.array([[ox + (p[0] - x) * s, oy + (p[1] - y) * s] for p in contour], np.int32)
    cv2.polylines(prev, [pts], True, (0, 0, 255), 3)
    cv2.imwrite(os.path.join(OUT, "..", "preview_outline.png"), prev)


if __name__ == "__main__":
    main()
