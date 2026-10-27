"""Renders baked beasts (json + png) with a small software rasterizer, for checking."""
import json, sys, numpy as np
from PIL import Image
def render(path, name, yaw=0.75, pitch=0.2, S=480, zoom=1.0):
    d = json.load(open('%s/%s.json' % (path, name)))
    tex = np.asarray(Image.open('%s/%s.png' % (path, name)).convert('RGB'), float) / 255
    TH, TW = tex.shape[:2]
    V = np.concatenate([np.array(p['v']).reshape(-1, 3) for p in d['parts']])
    N = np.concatenate([np.array(p['n']).reshape(-1, 3) for p in d['parts']])
    U = np.concatenate([np.array(p['uv']).reshape(-1, 2) for p in d['parts']])
    L = np.array([-0.4, 0.85, 0.35]); L /= np.linalg.norm(L)
    cy, sy, cp, sp = np.cos(yaw), np.sin(yaw), np.cos(pitch), np.sin(pitch)
    c = (V.max(0) + V.min(0)) / 2; P = V - c
    x1 = P[:, 0] * cy - P[:, 2] * sy; z1 = P[:, 0] * sy + P[:, 2] * cy
    y2 = P[:, 1] * cp - z1 * sp; z2 = P[:, 1] * sp + z1 * cp
    ext = (V.max(0) - V.min(0)).max() * 0.6 / zoom; sc = S / 2 / ext
    X = S / 2 + x1 * sc; Y = S / 2 - y2 * sc
    zb = np.full((S, S), -1e9); img = np.zeros((S, S, 3)) + np.array([38, 48, 60]) / 255
    lam = np.clip(N @ L, 0, 1) * 0.7 + 0.4
    for t in range(0, len(V), 3):
        idx = [t, t + 1, t + 2]
        xs, ys, zs = X[idx], Y[idx], z2[idx]
        x0, x1_ = int(max(0, np.floor(xs.min()))), int(min(S - 1, np.ceil(xs.max())))
        y0, y1_ = int(max(0, np.floor(ys.min()))), int(min(S - 1, np.ceil(ys.max())))
        if x1_ < x0 or y1_ < y0: continue
        gx, gy = np.meshgrid(np.arange(x0, x1_ + 1) + 0.5, np.arange(y0, y1_ + 1) + 0.5)
        a = xs[1] - xs[0]; b = xs[2] - xs[0]; cc = ys[1] - ys[0]; dd = ys[2] - ys[0]; det = a * dd - b * cc
        if abs(det) < 1e-9: continue
        u = ((gx - xs[0]) * dd - (gy - ys[0]) * b) / det; v = (a * (gy - ys[0]) - cc * (gx - xs[0])) / det; w = 1 - u - v
        m = (u >= 0) & (v >= 0) & (w >= 0)
        if not m.any(): continue
        z = w * zs[0] + u * zs[1] + v * zs[2]
        sub = zb[y0:y1_ + 1, x0:x1_ + 1]; m &= z > sub; sub[m] = z[m]
        uv = w[..., None] * U[t] + u[..., None] * U[t + 1] + v[..., None] * U[t + 2]
        tx = np.clip((uv[..., 0] * TW).astype(int), 0, TW - 1); ty = np.clip(((1 - uv[..., 1]) * TH).astype(int), 0, TH - 1)
        sh = w * lam[t] + u * lam[t + 1] + v * lam[t + 2]
        img[y0:y1_ + 1, x0:x1_ + 1][m] = (tex[ty, tx] * sh[..., None])[m]
    return Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
if __name__ == '__main__':
    path, out = sys.argv[1], sys.argv[2]; names = sys.argv[3:]
    ims = [render(path, n) for n in names] + [render(path, n, yaw=2.0, zoom=2.2) for n in names]
    sheet = Image.new('RGB', (480 * len(ims), 480))
    for i, im in enumerate(ims): sheet.paste(im, (i * 480, 0))
    sheet.save(out); print('ok')
