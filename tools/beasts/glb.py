"""Writes each baked beast (json + png from bake.py) as a glTF binary (.glb): one node per part at its pivot, its
mesh relative to it, the baked texture embedded. Blocks are metres; x forward becomes glTF +z (forward), y up, z side
becomes x.

  python3 glb.py <baked dir> <out dir> [names...]
"""
import json, struct, sys
import numpy as np

def to_gltf(v):
    v = np.asarray(v, np.float32).reshape(-1, 3)
    return np.stack([v[:, 2], v[:, 1], v[:, 0]], axis=1)

def write(path, name, out):
    d = json.load(open('%s/%s.json' % (path, name)))
    png = open('%s/%s' % (path, d['texture']), 'rb').read()
    blob = bytearray()
    views, accessors, meshes, nodes = [], [], [], []

    def add(data, target=None, kind='VEC3', comp=5126, minmax=False):
        nonlocal blob
        while len(blob) % 4:
            blob += b'\0'
        raw = data.tobytes()
        view = {'buffer': 0, 'byteOffset': len(blob), 'byteLength': len(raw)}
        if target:
            view['target'] = target
        views.append(view)
        blob += raw
        acc = {'bufferView': len(views) - 1, 'componentType': comp, 'count': len(data), 'type': kind}
        if minmax:
            acc['min'] = data.min(0).tolist(); acc['max'] = data.max(0).tolist()
        accessors.append(acc)
        return len(accessors) - 1

    for part in d['parts']:
        pivot = np.array(part['pivot'], np.float32)
        pos = to_gltf(np.asarray(part['v'], np.float32).reshape(-1, 3) - pivot)
        nrm = to_gltf(part['n'])
        nrm /= np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9
        uv = np.asarray(part['uv'], np.float32).reshape(-1, 2).copy()
        uv[:, 1] = 1.0 - uv[:, 1]
        count = len(pos) - len(pos) % 3
        # Swapping x and z mirrors the shape: each triangle's corners are listed the other way round.
        idx = np.arange(count, dtype=np.uint32).reshape(-1, 3)[:, [0, 2, 1]].reshape(-1)
        a_pos = add(pos.astype(np.float32), 34962, minmax=True)
        a_nrm = add(nrm.astype(np.float32), 34962)
        a_uv = add(uv.astype(np.float32), 34962, 'VEC2')
        a_idx = add(idx, 34963, 'SCALAR', 5125)
        meshes.append({'name': part['name'], 'primitives': [{'attributes': {'POSITION': a_pos, 'NORMAL': a_nrm, 'TEXCOORD_0': a_uv}, 'indices': a_idx, 'material': 0}]})
        nodes.append({'name': part['name'], 'mesh': len(meshes) - 1, 'translation': to_gltf(pivot)[0].tolist()})
    while len(blob) % 4:
        blob += b'\0'
    views.append({'buffer': 0, 'byteOffset': len(blob), 'byteLength': len(png)})
    blob += png
    root = {'name': name, 'children': list(range(len(nodes)))}
    nodes.append(root)
    gltf = {
        'asset': {'version': '2.0', 'generator': 'Castle Story Plus tools/beasts'},
        'scene': 0, 'scenes': [{'name': name, 'nodes': [len(nodes) - 1]}], 'nodes': nodes, 'meshes': meshes,
        'materials': [{'name': name, 'pbrMetallicRoughness': {'baseColorTexture': {'index': 0}, 'metallicFactor': 0.0, 'roughnessFactor': 0.85}}],
        'textures': [{'source': 0, 'sampler': 0}],
        'samplers': [{'magFilter': 9729, 'minFilter': 9987, 'wrapS': 33071, 'wrapT': 33071}],
        'images': [{'bufferView': len(views) - 1, 'mimeType': 'image/png'}],
        'buffers': [{'byteLength': len(blob)}], 'bufferViews': views, 'accessors': accessors,
    }
    js = json.dumps(gltf, separators=(',', ':')).encode()
    while len(js) % 4:
        js += b' '
    with open('%s/%s.glb' % (out, name), 'wb') as f:
        f.write(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(blob)))
        f.write(struct.pack('<II', len(js), 0x4E4F534A)); f.write(js)
        f.write(struct.pack('<II', len(blob), 0x004E4942)); f.write(bytes(blob))

if __name__ == '__main__':
    path, out = sys.argv[1], sys.argv[2]
    for name in (sys.argv[3:] or ['Spirestalker', 'Cindermane', 'Boulderback', 'Ironhorn']):
        write(path, name, out)
        print(name, 'ok')
