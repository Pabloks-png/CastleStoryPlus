"""SDF creature design: primitives, smooth union, numpy ray marcher for previews."""
import numpy as np

def hexc(h):
    return np.array([int(h[1:3], 16), int(h[3:5], 16), int(h[5:7], 16)], dtype=np.float32) / 255.0

class Prim:
    def __init__(self, kind, color, part, k, **kw):
        self.kind = kind; self.color = hexc(color) if isinstance(color, str) else color
        self.part = part; self.k = k; self.kw = kw

def rc(a, b, r1, r2, color, part='Body', k=0.08):
    return Prim('rc', color, part, k, a=np.array(a, float), b=np.array(b, float), r1=r1, r2=r2)

def ell(c, r, color, part='Body', k=0.08):
    return Prim('ell', color, part, k, c=np.array(c, float), r=np.array(r, float))

def rbox(c, h, rad, color, part='Body', k=0.04, rot=0.0):
    return Prim('rbox', color, part, k, c=np.array(c, float), h=np.array(h, float), rad=rad, rot=rot)

def d_rc(p, a, b, r1, r2):
    # Round cone (iq): sphere r1 at a, r2 at b, tangent cone between.
    ba = b - a; l2 = ba @ ba; rr = r1 - r2; a2 = l2 - rr * rr; il2 = 1.0 / l2
    pa = p - a
    y = pa @ ba; z = y - l2
    xv = pa * l2 - y[:, None] * ba[None, :]
    x2 = np.einsum('ij,ij->i', xv, xv)
    y2 = y * y * l2; z2 = z * z * l2
    k = np.sign(rr) * rr * rr * x2
    out = np.empty(len(p))
    m1 = np.sign(z) * a2 * z2 > k
    m2 = (~m1) & (np.sign(y) * a2 * y2 < k)
    m3 = ~(m1 | m2)
    out[m1] = np.sqrt(x2[m1] + z2[m1]) * il2 - r2
    out[m2] = np.sqrt(x2[m2] + y2[m2]) * il2 - r1
    out[m3] = (np.sqrt(x2[m3] * a2 * il2) + y[m3] * rr) * il2 - r1
    return out

def d_ell(p, c, r):
    q = (p - c) / r
    k0 = np.linalg.norm(q, axis=1)
    k1 = np.linalg.norm(q / r, axis=1)
    return k0 * (k0 - 1.0) / np.maximum(k1, 1e-6)

def d_rbox(p, c, h, rad, rot):
    q = p - c
    if rot:
        cs, sn = np.cos(rot), np.sin(rot)
        q = np.stack([q[:, 0] * cs + q[:, 1] * sn, -q[:, 0] * sn + q[:, 1] * cs, q[:, 2]], axis=1)
    q = np.abs(q) - (h - rad)
    return np.linalg.norm(np.maximum(q, 0), axis=1) + np.minimum(np.max(q, axis=1), 0) - rad

def dist(prim, p):
    kw = prim.kw
    if prim.kind == 'rc':
        return d_rc(p, kw['a'], kw['b'], kw['r1'], kw['r2'])
    if prim.kind == 'ell':
        return d_ell(p, kw['c'], kw['r'])
    return d_rbox(p, kw['c'], kw['h'], kw['rad'], kw['rot'])

def scene(prims, p, with_color=False, part=None):
    d = None; col = None
    for pr in prims:
        if part is not None and pr.part != part:
            continue
        di = dist(pr, p)
        if d is None:
            d = di; col = np.repeat(pr.color[None, :], len(p), axis=0)
            continue
        k = pr.k
        h = np.clip(0.5 + 0.5 * (d - di) / k, 0, 1)
        d = d * (1 - h) + di * h - k * h * (1 - h)
        if with_color:
            col = col * (1 - h[:, None]) + pr.color[None, :] * h[:, None]
    return (d, col) if with_color else d

def render(prims, yaw=0.6, pitch=0.25, size=360, extent=None, bg=(0.11, 0.14, 0.17), pattern=None):
    pts = np.concatenate([np.stack([p.kw.get('a', p.kw.get('c')), p.kw.get('b', p.kw.get('c'))]) for p in prims])
    lo = pts.min(0) - 0.4; hi = pts.max(0) + 0.4
    center = (lo + hi) / 2
    ext = extent or (hi - lo).max() * 0.62
    cy, sy, cp, sp = np.cos(yaw), np.sin(yaw), np.cos(pitch), np.sin(pitch)
    fwd = np.array([-sy * cp, -sp, -cy * cp])
    right = np.array([cy, 0, -sy])
    up = np.cross(right, fwd)
    u = np.linspace(-ext, ext, size)
    U, V = np.meshgrid(u, -u)
    origin = center[None, :] - fwd[None, :] * 6 + U.reshape(-1, 1) * right[None, :] + V.reshape(-1, 1) * up[None, :]
    n = len(origin)
    t = np.zeros(n); hit = np.zeros(n, bool); alive = np.ones(n, bool)
    for _ in range(90):
        idx = np.nonzero(alive)[0]
        if len(idx) == 0:
            break
        p = origin[idx] + fwd[None, :] * t[idx, None]
        d = scene(prims, p)
        t[idx] += d
        done = d < 0.002
        hit[idx[done]] = True
        alive[idx[done]] = False
        far = t[idx] > 12
        alive[idx[far]] = False
    img = np.zeros((n, 3)); img[:] = bg
    hi_ = np.nonzero(hit)[0]
    p = origin[hi_] + fwd[None, :] * t[hi_, None]
    _, col = scene(prims, p, with_color=True)
    if pattern is not None:
        col = pattern(p, col)
    e = 0.004
    nrm = np.stack([scene(prims, p + np.array([e, 0, 0])) - scene(prims, p - np.array([e, 0, 0])),
                    scene(prims, p + np.array([0, e, 0])) - scene(prims, p - np.array([0, e, 0])),
                    scene(prims, p + np.array([0, 0, e])) - scene(prims, p - np.array([0, 0, e]))], axis=1)
    nrm /= np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9
    light = np.array([-0.4, 0.85, 0.35]); light /= np.linalg.norm(light)
    lam = np.clip(nrm @ light, 0, 1)
    ao = np.clip(scene(prims, p + nrm * 0.08) / 0.08, 0.4, 1.0)
    rim = np.clip(1 - np.abs(nrm @ -fwd), 0, 1) ** 3
    shade = (0.35 + 0.75 * lam) * ao
    img[hi_] = np.clip(col * shade[:, None] + rim[:, None] * 0.18, 0, 1)
    # ground shadow-ish line
    return (img.reshape(size, size, 3) * 255).astype(np.uint8)

def voronoi_edge(p, scale, seed=1):
    """Distance to the nearest cell border (F2 - F1) of a 3D Voronoi pattern."""
    q = p * scale
    cell = np.floor(q)
    f1 = np.full(len(p), 9.0); f2 = np.full(len(p), 9.0)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                c = cell + np.array([dx, dy, dz])
                h = np.sin(c @ np.array([127.1, 311.7, 74.7]) + seed) * 43758.5453
                j = np.stack([h - np.floor(h), (h * 1.37) - np.floor(h * 1.37), (h * 1.91) - np.floor(h * 1.91)], axis=1)
                d = np.linalg.norm(c + j - q, axis=1)
                f2 = np.where(d < f1, f1, np.minimum(f2, d)); f1 = np.minimum(f1, d)
    return f2 - f1
