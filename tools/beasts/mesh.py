"""Surface nets meshing of each part of a beast's SDF, exported as JSON for the game and the 3D page."""
import json, sys
import numpy as np
from sdf import scene
from beasts import ALL, PATTERNS

STEP = 0.03

def size_of(p):
    kw = p.kw
    if p.kind == 'rc':
        return max(kw['r1'], kw['r2'])
    if p.kind == 'ell':
        return float(np.max(kw['r']))
    return float(np.max(kw['h']))

def part_step(prims, fine=False):
    if fine:
        return 0.024
    sizes = []
    for p in prims:
        kw = p.kw
        if p.kind == 'rc':
            sizes += [r for r in (kw['r1'], kw['r2']) if r >= 0.025]
        elif p.kind == 'ell':
            sizes += [r for r in kw['r'] if r >= 0.025]
        else:
            sizes += [h for h in kw['h'] if h >= 0.025]
    smallest = min(sizes) if sizes else 0.05
    return float(np.clip(smallest * 0.75, 0.04, 0.085))

def part_names(prims):
    out = []
    for p in prims:
        if p.part not in out:
            out.append(p.part)
    return out

def bounds(prims):
    lo = np.full(3, 1e9); hi = np.full(3, -1e9)
    for p in prims:
        kw = p.kw
        if p.kind == 'rc':
            r = max(kw['r1'], kw['r2'])
            for q in (kw['a'], kw['b']):
                lo = np.minimum(lo, q - r); hi = np.maximum(hi, q + r)
        elif p.kind == 'ell':
            lo = np.minimum(lo, kw['c'] - kw['r']); hi = np.maximum(hi, kw['c'] + kw['r'])
        else:
            e = np.linalg.norm(kw['h'])
            lo = np.minimum(lo, kw['c'] - e); hi = np.maximum(hi, kw['c'] + e)
    return lo - 0.12, hi + 0.12

def surface_nets(prims, all_prims, pattern, fine=False):
    STEP = part_step(prims, fine)
    lo, hi = bounds(prims)
    n = np.ceil((hi - lo) / STEP).astype(int) + 1
    xs = [lo[i] + np.arange(n[i]) * STEP for i in range(3)]
    G = np.stack(np.meshgrid(*xs, indexing='ij'), axis=-1).reshape(-1, 3)
    d = scene(prims, G).reshape(n)
    inside = d < 0
    # Cells with a sign change get one vertex at the mean of their edge crossings.
    nc = n - 1
    corners = [(i, j, k) for i in (0, 1) for j in (0, 1) for k in (0, 1)]
    cd = np.stack([d[i:i + nc[0], j:j + nc[1], k:k + nc[2]] for (i, j, k) in corners], axis=-1)
    active = (cd.min(-1) < 0) & (cd.max(-1) >= 0)
    idx = np.full(nc, -1, dtype=np.int64)
    cells = np.argwhere(active)
    idx[tuple(cells.T)] = np.arange(len(cells))
    edges = [(a, b) for a in range(8) for b in range(a + 1, 8) if sum(abs(np.array(corners[a]) - np.array(corners[b]))) == 1]
    acc = np.zeros((len(cells), 3)); cnt = np.zeros(len(cells))
    cdv = cd[tuple(cells.T)]
    for a, b in edges:
        da = cdv[:, a]; db = cdv[:, b]
        m = (da < 0) != (db < 0)
        t = da[m] / (da[m] - db[m])
        pa = np.array(corners[a], float); pb = np.array(corners[b], float)
        acc[m] += pa + (pb - pa) * t[:, None]
        cnt[m] += 1
    verts = lo + (cells + acc / cnt[:, None]) * STEP
    # Quads across each grid edge with a sign change.
    quads = []
    for axis in range(3):
        o1, o2 = [a for a in range(3) if a != axis]
        sl0 = [slice(1, n[0] - 1), slice(1, n[1] - 1), slice(1, n[2] - 1)]
        sl0[axis] = slice(0, n[axis] - 1)
        sl1 = list(sl0); sl1[axis] = slice(1, n[axis])
        s0 = inside[tuple(sl0)]; s1 = inside[tuple(sl1)]
        flip = s0 != s1
        pts = np.argwhere(flip)
        base = pts + np.array([1 if a != axis else 0 for a in range(3)])
        def cell(da, db):
            c = base.copy(); c[:, o1] -= da; c[:, o2] -= db
            return idx[tuple(c.T)]
        c00, c10, c11, c01 = cell(1, 1), cell(0, 1), cell(0, 0), cell(1, 0)
        ok = (c00 >= 0) & (c10 >= 0) & (c11 >= 0) & (c01 >= 0)
        outward = s0[tuple(pts.T)][ok]  # inside then outside along +axis
        q = np.stack([c00[ok], c10[ok], c11[ok], c01[ok]], axis=1)
        quads.append((q, outward))
    tris = []
    for q, outward in quads:
        for qi, o in zip(q, outward):
            tris.append((qi[0], qi[1], qi[2], o)); tris.append((qi[0], qi[2], qi[3], o))
    # Normals from the gradient of the whole beast (smooth across parts), colours from this part's primitives.
    e = 0.004
    def grad(p):
        return np.stack([scene(all_prims, p + np.array([e, 0, 0])) - scene(all_prims, p - np.array([e, 0, 0])),
                         scene(all_prims, p + np.array([0, e, 0])) - scene(all_prims, p - np.array([0, e, 0])),
                         scene(all_prims, p + np.array([0, 0, e])) - scene(all_prims, p - np.array([0, 0, e]))], axis=1)
    nrm = grad(verts); nrm /= np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9
    _, col = scene(prims, verts, with_color=True)
    if pattern is not None:
        col = pattern(verts, col)
    # Orient each triangle so its geometric normal agrees with the surface normal.
    out = []
    for a, b, c, _ in tris:
        cr = np.cross(verts[b] - verts[a], verts[c] - verts[a])
        if np.dot(cr, nrm[a] + nrm[b] + nrm[c]) < 0:
            b, c = c, b
        out.append((a, b, c))
    return verts, nrm, col, np.array(out, dtype=np.int64)

def export(name):
    prims = ALL[name]()
    pattern = PATTERNS.get(name)
    palette = {}
    parts = []
    for pn in part_names(prims):
        pp = [p for p in prims if p.part == pn]
        coarse = [p for p in pp if size_of(p) >= 0.06]
        fine = [p for p in pp if size_of(p) < 0.06]
        vs, ns, cs, ts = [], [], [], []
        for group, f in ((coarse, False), (fine, True)):
            if not group:
                continue
            v, nr, col, t = surface_nets(group, prims, pattern, f)
            ts.append(t + sum(len(x) for x in vs)); vs.append(v); ns.append(nr); cs.append(col)
        v = np.concatenate(vs); nr = np.concatenate(ns); col = np.concatenate(cs); t = np.concatenate(ts)
        q = np.clip(np.round(col * 31), 0, 31).astype(int)
        ci = []
        for c in q:
            key = '#%02x%02x%02x' % tuple(int(round(x * 255 / 31)) for x in c)
            if key not in palette:
                palette[key] = len(palette)
            ci.append(palette[key])
        first = pp[0].kw
        pivot = first['a'] if pp[0].kind == 'rc' else first['c']
        if pn == 'Body':
            pivot = np.zeros(3)
        parts.append({'name': pn, 'pivot': [round(float(x), 4) for x in pivot],
                      'v': [round(float(x), 4) for x in v.reshape(-1)],
                      'n': [round(float(x), 3) for x in nr.reshape(-1)],
                      'c': ci, 't': [int(x) for x in t.reshape(-1)]})
    return {'name': name, 'palette': list(palette), 'parts': parts}

if __name__ == '__main__':
    outdir = sys.argv[1]
    for name in ALL:
        data = export(name)
        tris = sum(len(p['t']) // 3 for p in data['parts'])
        with open('%s/%s.json' % (outdir, name), 'w') as f:
            json.dump(data, f, separators=(',', ':'))
        print(name, 'parts', len(data['parts']), 'tris', tris, 'palette', len(data['palette']))
