"""Bakes each beast's colours into a texture atlas and writes the textured mesh the game loads.

Every triangle of the surface nets mesh (mesh.py) gets its own cell of the atlas (two triangles per square cell, a
small margin round each); every texel is the beast's colour at that point of the surface, with its pattern (spots,
silverback, plates), a little surface noise and ambient occlusion from the shape. The mesh is written with one vertex
per triangle corner (its own UV) and the smooth normals.

  python3 bake.py <out dir> [atlas size]    ->  <name>.json and <name>.png per beast
"""
import json, sys
import numpy as np
from PIL import Image
from sdf import scene
from beasts import ALL, PATTERNS
import mesh

MARGIN = 1.5

def noise3(p, scale, seed):
    q = p * scale
    i = np.floor(q); f = q - i
    f = f * f * (3 - 2 * f)
    def h(c):
        v = np.sin(c @ np.array([127.1, 311.7, 74.7]) + seed) * 43758.5453
        return v - np.floor(v)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * h(i + np.array([dx, dy, dz]))
    return out

def bake(name, size):
    prims = ALL[name]()
    pattern = PATTERNS.get(name)
    parts = []
    for pn in mesh.part_names(prims):
        pp = [p for p in prims if p.part == pn]
        coarse = [p for p in pp if mesh.size_of(p) >= 0.06]
        fine = [p for p in pp if mesh.size_of(p) < 0.06]
        vs, ns, ts, groups = [], [], [], []
        for group, f in ((coarse, False), (fine, True)):
            if not group:
                continue
            v, nr, _, t = mesh.surface_nets(group, prims, None, f)
            ts.append(t + sum(len(x) for x in vs)); vs.append(v); ns.append(nr); groups.append((group, len(t)))
        first = pp[0].kw
        pivot = first['a'] if pp[0].kind == 'rc' else first['c']
        if pn == 'Body':
            pivot = np.zeros(3)
        parts.append({'name': pn, 'pivot': pivot, 'v': np.concatenate(vs), 'n': np.concatenate(ns), 't': np.concatenate(ts),
                      'prims': [g for g, c in groups for _ in range(c)]})
    total = sum(len(p['t']) for p in parts)
    grid = int(np.ceil(np.sqrt(total / 2.0)))
    cell = size // grid
    print(name, 'triangles', total, 'grid', grid, 'cell', cell, 'px')
    tex = np.zeros((size, size, 3), np.float32)
    out_parts = []
    k = 0
    for part in parts:
        V, N, T = part['v'], part['n'], part['t']
        verts, norms, uvs = [], [], []
        # Texel jobs of this part, grouped by the primitives that colour them.
        for ti, tri in enumerate(T):
            slot = k // 2; upper = k % 2; k += 1
            cx, cy = (slot % grid) * cell, (slot // grid) * cell
            if upper:
                corners = [(cx + cell - MARGIN, cy + cell - MARGIN), (cx + MARGIN * 2, cy + cell - MARGIN), (cx + cell - MARGIN, cy + MARGIN * 2)]
            else:
                corners = [(cx + MARGIN, cy + MARGIN), (cx + cell - MARGIN * 2, cy + MARGIN), (cx + MARGIN, cy + cell - MARGIN * 2)]
            for j in range(3):
                verts.append(V[tri[j]]); norms.append(N[tri[j]])
                uvs.append((corners[j][0] / size, 1.0 - corners[j][1] / size))
            part.setdefault('jobs', []).append((slot, upper, tri, ti))
        out_parts.append((part, np.array(verts), np.array(norms), np.array(uvs)))
    # Texels: every texel of a cell belongs to its lower or upper triangle; its barycentric coordinates (clamped into
    # the triangle, so the margins repeat the edge) give the surface point.
    for part, _, _, _ in out_parts:
        V, N = part['v'], part['n']
        jobs = part['jobs']
        texel = np.arange(cell) + 0.5
        gu, gv = np.meshgrid(texel / cell, texel / cell)
        gu = gu.reshape(-1); gv = gv.reshape(-1)
        lower = gu + gv <= 1.0
        for start in range(0, len(jobs), 400):
            batch = jobs[start:start + 400]
            P, NN, where, owners = [], [], [], []
            for slot, upper, tri, ti in batch:
                m = ~lower if upper else lower
                u = gu[m]; v = gv[m]
                if upper:
                    a = 1.0 - u; b = 1.0 - v
                else:
                    a = u; b = v
                # Inset like the UV corners, then clamp into the triangle.
                s = 1.0 - 3.0 * MARGIN / cell
                a = np.clip((a - MARGIN / cell) / s, 0, 1); b = np.clip((b - MARGIN / cell) / s, 0, 1)
                over = a + b > 1
                tot = a[over] + b[over]; a[over] /= tot; b[over] /= tot
                c = 1 - a - b
                pa, pb, pc = V[tri[0]], V[tri[1]], V[tri[2]]
                P.append(pa[None] * c[:, None] + pb[None] * a[:, None] + pc[None] * b[:, None])
                na, nb, nc = N[tri[0]], N[tri[1]], N[tri[2]]
                NN.append(na[None] * c[:, None] + nb[None] * a[:, None] + nc[None] * b[:, None])
                cx, cy = (slot % grid) * cell, (slot // grid) * cell
                ix = (np.arange(cell * cell) % cell)[m] + cx
                iy = (np.arange(cell * cell) // cell)[m] + cy
                where.append((iy, ix)); owners.append(len(P[-1]))
            P = np.concatenate(P); NN = np.concatenate(NN)
            NN /= np.linalg.norm(NN, axis=1, keepdims=True) + 1e-9
            col = colour(prims, part, P, NN, pattern)
            o = 0
            for (iy, ix), n in zip(where, owners):
                tex[iy, ix] = col[o:o + n]; o += n
    img = Image.fromarray((np.clip(tex, 0, 1) * 255).astype(np.uint8))
    data = {'name': name, 'texture': name + '.png', 'parts': []}
    for part, verts, norms, uvs in out_parts:
        data['parts'].append({'name': part['name'], 'pivot': [round(float(x), 4) for x in part['pivot']],
                              'v': [round(float(x), 4) for x in verts.reshape(-1)],
                              'n': [round(float(x), 3) for x in norms.reshape(-1)],
                              'uv': [round(float(x), 5) for x in uvs.reshape(-1)]})
    return data, img

def colour(prims, part, P, N, pattern):
    own = [p for p in prims if p.part == part['name']]
    _, col = scene(own, P, with_color=True)
    if pattern is not None:
        col = pattern(P, col)
    # Ambient occlusion from the whole beast's shape: darker in folds and where parts meet.
    ao = 1.0
    for dist, weight in ((0.03, 0.5), (0.08, 0.35), (0.16, 0.25)):
        d = scene(prims, P + N * dist)
        ao = ao - weight * np.clip(dist - d, 0, dist) / dist
    ao = np.clip(ao, 0.45, 1.0)
    # A little surface variation (fur, hide, stone grain).
    grain = 0.92 + 0.08 * noise3(P, 40.0, 3.0) + 0.05 * (noise3(P, 9.0, 7.0) - 0.5)
    return np.clip(col * (ao * grain)[:, None], 0, 1)

if __name__ == '__main__':
    out = sys.argv[1]
    size = int(sys.argv[2]) if len(sys.argv) > 2 else 2048
    names = sys.argv[3:] or list(ALL)
    for name in names:
        data, img = bake(name, size)
        img.save('%s/%s.png' % (out, name), optimize=True)
        with open('%s/%s.json' % (out, name), 'w') as f:
            json.dump(data, f, separators=(',', ':'))
        print(name, 'done')
