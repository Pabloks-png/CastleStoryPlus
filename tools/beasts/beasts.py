"""The four guardians as SDF primitives. Units: blocks; x forward, y up, z side."""
import numpy as np
from sdf import rc, ell, rbox

def mirror(fn):
    return fn(1) + fn(-1)

def giraffe():
    tan, dark, hoof, cry, light, eye = '#d9a441', '#b8862f', '#3b2a1e', '#5fd8ff', '#c9f6ff', '#ff4a2c'
    P = []
    P += [rc((-0.55, 1.42, 0), (0.42, 1.6, 0), 0.34, 0.4, tan, k=0.1)]
    P += [ell((-0.05, 1.42, 0), (0.55, 0.3, 0.3), dark, k=0.15)]
    def legs(s):
        out = []
        for x, part, c in ((0.36, 'LegF', tan), (-0.48, 'LegB', dark)):
            z = 0.16 * s; pn = part + ('L' if s < 0 else 'R')
            out += [rc((x, 1.45, z), (x + 0.03, 0.78, z), 0.11, 0.065, c, pn, k=0.06),
                    rc((x + 0.03, 0.78, z), (x, 0.1, z), 0.06, 0.05, c, pn, k=0.03),
                    rc((x, 0.1, z), (x + 0.02, 0.03, z), 0.065, 0.06, hoof, pn, k=0.02)]
        return out
    P += mirror(legs)
    P += [rc((0.42, 1.72, 0), (0.98, 2.88, 0), 0.19, 0.1, tan, 'Head', k=0.08),
          rc((0.98, 2.95, 0), (1.3, 2.84, 0), 0.14, 0.08, tan, 'Head', k=0.06),
          rc((1.22, 2.86, 0), (1.34, 2.82, 0), 0.08, 0.07, dark, 'Head', k=0.03)]
    for t in (0.2, 0.42, 0.64, 0.84):
        base = np.array((0.42, 1.72, 0)) + t * (np.array((0.98, 2.88, 0)) - np.array((0.42, 1.72, 0)))
        back = base + np.array((-0.13, 0.07, 0))
        tip = back + np.array((-0.17, 0.12, 0))
        P += [rc(tuple(back), tuple(tip), 0.05, 0.012, cry, 'Head', k=0.02)]
    def head_bits(s):
        z = 0.07 * s
        return [rc((0.97, 3.04, z), (0.93, 3.28, z * 1.2), 0.03, 0.022, cry, 'Head', k=0.02),
                ell((0.93, 3.3, z * 1.2), (0.045, 0.06, 0.045), light, 'Head', k=0.015),
                rc((0.9, 3.0, 0.1 * s), (0.8, 3.08, 0.24 * s), 0.035, 0.015, tan, 'Head', k=0.02),
                ell((1.12, 2.98, 0.11 * s), (0.03, 0.03, 0.02), eye, 'Head', k=0.01)]
    P += mirror(head_bits)
    P += [rc((-0.58, 1.62, 0), (-0.78, 1.0, 0), 0.04, 0.02, dark, 'Tail', k=0.03),
          ell((-0.8, 0.93, 0), (0.05, 0.1, 0.05), cry, 'Tail', k=0.02)]
    return P

def lion():
    coat, dark, belly, mane, hot, ember, face, nose, eye = '#d6a04a', '#b98535', '#e8c47e', '#a8330c', '#e8601e', '#ffd23f', '#e7bb66', '#3a2410', '#ffe94d'
    P = []
    P += [rc((-0.5, 0.74, 0), (0.28, 0.8, 0), 0.25, 0.3, coat, k=0.1),
          ell((0.22, 0.84, 0), (0.24, 0.3, 0.27), coat, k=0.12),
          ell((-0.45, 0.78, 0), (0.24, 0.26, 0.25), dark, k=0.12),
          ell((-0.1, 0.6, 0), (0.42, 0.14, 0.2), belly, k=0.14)]
    def legs(s):
        z = 0.16 * s; o = []
        for x, nm, top, c, r in ((0.26, 'LegF', 0.8, coat, 0.14), (-0.46, 'LegB', 0.82, dark, 0.17)):
            pn = nm + ('L' if s < 0 else 'R')
            o += [rc((x, top, z), (x + 0.03, 0.34, z), r, 0.085, c, pn, k=0.07),
                  rc((x + 0.03, 0.34, z), (x, 0.09, z), 0.08, 0.075, c, pn, k=0.03),
                  ell((x + 0.05, 0.065, z), (0.12, 0.065, 0.095), belly, pn, k=0.04)]
        return o
    P += mirror(legs)
    lobes = [((0.46, 1.16, 0.0), (0.2, 0.2, 0.24), mane), ((0.42, 1.0, 0.22), (0.18, 0.24, 0.14), mane),
             ((0.42, 1.0, -0.22), (0.18, 0.24, 0.14), mane), ((0.5, 0.76, 0.0), (0.16, 0.24, 0.2), mane),
             ((0.36, 1.2, 0.16), (0.14, 0.14, 0.12), hot), ((0.36, 1.2, -0.16), (0.14, 0.14, 0.12), hot),
             ((0.34, 0.86, 0.24), (0.14, 0.16, 0.1), hot), ((0.34, 0.86, -0.24), (0.14, 0.16, 0.1), hot),
             ((0.3, 1.06, 0.0), (0.2, 0.22, 0.26), mane)]
    for (c, r, col) in lobes:
        P += [ell(c, r, col, 'Head', k=0.08)]
    P += [ell((0.66, 0.98, 0), (0.16, 0.16, 0.15), face, 'Head', k=0.06),
          rc((0.72, 0.92, 0), (0.86, 0.87, 0), 0.105, 0.08, face, 'Head', k=0.04),
          ell((0.89, 0.91, 0), (0.03, 0.025, 0.045), nose, 'Head', k=0.01),
          ell((0.8, 0.8, 0), (0.07, 0.04, 0.07), face, 'Head', k=0.03)]
    for (a, bb, r) in (((0.32, 1.32, 0), (0.18, 1.52, -0.02), 0.07), ((0.3, 1.26, 0.17), (0.16, 1.4, 0.27), 0.05), ((0.3, 1.26, -0.17), (0.16, 1.4, -0.27), 0.05)):
        P += [rc(a, bb, r, 0.008, ember, 'Head', k=0.05)]
    def eyes(s):
        return [ell((0.79, 1.03, 0.075 * s), (0.02, 0.022, 0.02), eye, 'Head', k=0.005),
                ell((0.66, 1.13, 0.12 * s), (0.04, 0.05, 0.03), dark, 'Head', k=0.02)]
    P += mirror(eyes)
    P += [rc((-0.62, 0.88, 0), (-0.95, 0.64, 0), 0.045, 0.03, coat, 'Tail', k=0.03),
          rc((-0.95, 0.64, 0), (-1.08, 0.76, 0), 0.03, 0.025, coat, 'Tail', k=0.02),
          rc((-1.07, 0.74, 0), (-1.16, 0.92, 0), 0.075, 0.01, hot, 'Tail', k=0.03)]
    return P

def gorilla():
    fur, furd, skin, stone, stonel, glow = '#4a4550', '#34303a', '#7a6458', '#8e9196', '#b9bcc1', '#ffb02e'
    P = []
    P += [ell((0.02, 1.12, 0), (0.4, 0.5, 0.42), fur, k=0.12),
          rc((0.12, 1.46, -0.36), (0.12, 1.46, 0.36), 0.28, 0.28, fur, k=0.16),
          ell((-0.3, 0.9, 0), (0.24, 0.3, 0.3), fur, k=0.14),
          ell((0.3, 1.1, 0), (0.16, 0.28, 0.28), skin, k=0.12)]
    P += [ell((-0.1, 1.58, 0.04), (0.24, 0.09, 0.22), stone, k=0.04),
          ell((-0.34, 1.36, -0.05), (0.18, 0.08, 0.2), stonel, k=0.04),
          ell((0.06, 1.68, -0.08), (0.11, 0.07, 0.11), stonel, k=0.03)]
    def limbs(s):
        z = s; o = []
        pa = 'Arm' + ('L' if s < 0 else 'R')
        o += [ell((0.16, 1.5, 0.44 * z), (0.18, 0.18, 0.17), fur, pa, k=0.08),
              rc((0.16, 1.48, 0.46 * z), (0.4, 0.82, 0.56 * z), 0.17, 0.14, fur, pa, k=0.06),
              rc((0.4, 0.82, 0.56 * z), (0.5, 0.18, 0.5 * z), 0.13, 0.15, fur, pa, k=0.05),
              ell((0.53, 0.1, 0.5 * z), (0.16, 0.1, 0.14), furd, pa, k=0.04),
              ell((0.14, 1.62, 0.46 * z), (0.14, 0.07, 0.12), stone, pa, k=0.03)]
        pl = 'LegB' + ('L' if s < 0 else 'R')
        o += [rc((-0.3, 0.8, 0.24 * z), (-0.16, 0.42, 0.27 * z), 0.18, 0.13, fur, pl, k=0.06),
              rc((-0.16, 0.42, 0.27 * z), (-0.18, 0.09, 0.27 * z), 0.12, 0.1, fur, pl, k=0.04),
              ell((-0.12, 0.05, 0.27 * z), (0.17, 0.06, 0.11), furd, pl, k=0.03)]
        return o
    P += mirror(limbs)
    P += [ell((0.44, 1.5, 0), (0.2, 0.21, 0.19), fur, 'Head', k=0.08),
          rc((0.34, 1.68, 0), (0.46, 1.72, 0), 0.06, 0.035, furd, 'Head', k=0.06),
          rc((0.53, 1.6, -0.12), (0.53, 1.6, 0.12), 0.05, 0.05, furd, 'Head', k=0.04),
          ell((0.58, 1.42, 0), (0.1, 0.11, 0.13), skin, 'Head', k=0.05)]
    def eyes(s):
        return [ell((0.6, 1.55, 0.065 * s), (0.02, 0.02, 0.025), glow, 'Head', k=0.005)]
    P += mirror(eyes)
    return P

def rhino():
    hide, hided, rivet, steel, steell, eye = '#80858c', '#666a71', '#4a4e54', '#d7dde5', '#f4f7fa', '#ff3b2f'
    P = []
    P += [ell((-0.05, 0.86, 0), (0.72, 0.42, 0.42), hide, k=0.1),
          ell((0.32, 1.0, 0), (0.36, 0.34, 0.4), hide, k=0.14),
          ell((-0.4, 0.92, 0), (0.34, 0.3, 0.38), hide, k=0.14),
          ell((-0.05, 0.62, 0), (0.6, 0.2, 0.34), hided, k=0.15)]
    def legs(s):
        z = 0.25 * s; o = []
        for x, nm, c in ((0.42, 'LegF', hide), (-0.46, 'LegB', hided)):
            pn = nm + ('L' if s < 0 else 'R')
            o += [rc((x, 0.8, z), (x, 0.1, z), 0.19, 0.145, c, pn, k=0.07),
                  rc((x, 0.1, z), (x + 0.02, 0.03, z), 0.155, 0.15, rivet, pn, k=0.02)]
        return o
    P += mirror(legs)
    P += [rc((0.62, 0.95, 0), (1.16, 0.6, 0), 0.26, 0.15, hide, 'Head', k=0.09),
          ell((1.12, 0.56, 0), (0.12, 0.1, 0.14), hided, 'Head', k=0.05),
          rc((1.1, 0.68, 0), (1.3, 1.14, 0), 0.105, 0.008, steel, 'Head', k=0.03),
          rc((0.9, 0.92, 0), (0.99, 1.12, 0), 0.065, 0.006, steell, 'Head', k=0.025)]
    def bits(s):
        return [rc((0.68, 1.1, 0.13 * s), (0.62, 1.27, 0.2 * s), 0.05, 0.02, hide, 'Head', k=0.03),
                ell((0.88, 0.86, 0.18 * s), (0.025, 0.025, 0.02), eye, 'Head', k=0.006)]
    P += mirror(bits)
    P += [rc((-0.72, 0.98, 0), (-0.88, 0.62, 0), 0.035, 0.02, hided, 'Tail', k=0.02)]
    return P

def giraffe_spots(p, col):
    from sdf import voronoi_edge, hexc
    tan = hexc('#d9a441'); spot = hexc('#8a5a2b')
    is_tan = np.linalg.norm(col - tan, axis=1) < 0.12
    edge = voronoi_edge(p, 6.0)
    m = is_tan & (edge > 0.17) & (p[:, 1] > 0.75)
    col = col.copy(); col[m] = spot
    return col

def silverback(p, col):
    from sdf import hexc
    fur = hexc('#4a4550'); silver = hexc('#a3a1ac')
    near = np.linalg.norm(col - fur, axis=1) < 0.1
    w = np.clip((p[:, 1] - 0.95) / 0.3, 0, 1) * np.clip((0.3 - p[:, 0]) / 0.3, 0, 1) * np.clip((0.4 - np.abs(p[:, 2])) / 0.15, 0, 1)
    w = np.where(near, w * 0.8, 0)
    return col * (1 - w[:, None]) + silver[None, :] * w[:, None]

def iron_plates(p, col):
    from sdf import hexc
    hide = hexc('#80858c'); plate = hexc('#b3b9c2'); seam = hexc('#5a5e64'); rivet = hexc('#3f4348')
    upper = (np.linalg.norm(col - hide, axis=1) < 0.1) & (p[:, 1] > 0.98) & (p[:, 0] < 0.62) & (p[:, 0] > -0.8)
    col = col.copy()
    col[upper] = plate
    u = np.mod(p[:, 0] + 0.8, 0.48)
    col[upper & (u < 0.03)] = seam
    col[upper & (p[:, 1] < 1.03)] = seam
    rv = upper & (np.abs(u - 0.24) < 0.028) & (np.abs(p[:, 1] - 1.09) < 0.028)
    col[rv] = rivet
    return col

PATTERNS = {'Spirestalker': giraffe_spots, 'Boulderback': silverback, 'Ironhorn': iron_plates}

ALL = {'Spirestalker': giraffe, 'Cindermane': lion, 'Boulderback': gorilla, 'Ironhorn': rhino}
