"""Symmetry analysis of Ashlands ruin blueprints (input: recon.tsv from the RuinsReconDump harness).

For each ruin location:
  1. Rebuild the blueprint tree and keep every STRUCTURAL networked piece that the rebuilt
     ruin spawns (every RandomSpawn roll forced on; inactive subtrees stay off).
  2. Find the building's design symmetries: rotations (90/180/270 about a vertical axis) and
     mirror planes (x, z, both diagonals) through candidate centers. A transform is a design
     symmetry when most pieces map onto an identical piece (same prefab, position, orientation).
  3. A piece whose image under a design symmetry is missing (and whose image slot is free)
     is a symmetry completion candidate -> additions.tsv (location-local space).
Mirrors only propose pieces whose own shape is mirror-symmetric under that mirror (checked
via the catalog bounds), so a left arch half never gets "mirrored" into a wrong-handed copy.
Also writes report.json for the HTML report.
"""
import json, math, sys, collections
import numpy as np

SRC = sys.argv[1] if len(sys.argv) > 1 else 'recon.tsv'
RUINS = ['CharredRuins1', 'CharredRuins2', 'CharredRuins3', 'CharredRuins4',
         'CharredTowerRuins1', 'CharredTowerRuins1_dvergr', 'CharredTowerRuins2', 'CharredTowerRuins3']

rows = [l.rstrip('\n').split('\t') for l in open(SRC, encoding='utf-8')]
structural = set()
bounds = {}
for r in rows:
    if r[0] == 'PREFAB' and r[1].startswith('Ashland') and 'hp=' in r[3]:
        structural.add(r[1])
        if r[4].startswith('bmin='):
            a, b = r[4].split(';')
            bounds[r[1]] = (np.array([float(x) for x in a[5:].split(',')]), np.array([float(x) for x in b[5:].split(',')]))


def quat_to_mat(q):
    x, y, z, w = q
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def mat_to_quat(m):
    t = np.trace(m)
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        return ((m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, 0.25 * s)
    i = int(np.argmax([m[0, 0], m[1, 1], m[2, 2]]))
    if i == 0:
        s = math.sqrt(1 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        return (0.25 * s, (m[0, 1] + m[1, 0]) / s, (m[0, 2] + m[2, 0]) / s, (m[2, 1] - m[1, 2]) / s)
    if i == 1:
        s = math.sqrt(1 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        return ((m[0, 1] + m[1, 0]) / s, 0.25 * s, (m[1, 2] + m[2, 1]) / s, (m[0, 2] - m[2, 0]) / s)
    s = math.sqrt(1 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
    return ((m[0, 2] + m[2, 0]) / s, (m[1, 2] + m[2, 1]) / s, 0.25 * s, (m[1, 0] - m[0, 1]) / s)


def load_pieces(loc):
    """Structural pieces of the rebuilt blueprint (forced rolls), location-local space."""
    nodes = [r for r in rows if r[0] == 'NODE' and r[1] == loc]
    out = []
    stack = []  # (depth, active)
    for r in nodes:
        depth = int(r[2]); active = r[5] == '1'
        while stack and stack[-1][0] >= depth:
            stack.pop()
        parent_active = all(a for _, a in stack)
        stack.append((depth, active))
        if depth == 0 or not (parent_active and active):
            continue
        if r[6] == '1' and r[4] in structural:
            p = np.array([float(x) for x in r[7].split(',')])
            q = tuple(float(x) for x in r[8].split(','))
            out.append((r[4], p, quat_to_mat(q)))
    # dedupe exact duplicates (alternative rolls stacked in the same slot)
    ded = []
    for pc in out:
        if not any(pc[0] == o[0] and np.linalg.norm(pc[1] - o[1]) < 0.15 and np.allclose(pc[2], o[2], atol=0.05) for o in ded):
            ded.append(pc)
    return ded


def rot_y(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


MIRRORS = {
    'mirror_x': np.diag([-1.0, 1, 1]),   # plane x = cx
    'mirror_z': np.diag([1.0, 1, -1]),   # plane z = cz
    'mirror_d1': np.array([[0, 0, 1.0], [0, 1, 0], [1, 0, 0]]),
    'mirror_d2': np.array([[0, 0, -1.0], [0, 1, 0], [-1, 0, 0]]),
}


def transforms(center):
    for deg in (90, 180, 270):
        yield f'rot{deg}', rot_y(deg), center, False
    for name, m in MIRRORS.items():
        yield name, m, center, True


def apply(T, c, piece, mirror):
    name, p, R = piece
    p2 = c + T @ (p - c)
    R2 = T @ R @ (T if mirror else np.eye(3))  # reflection conjugation keeps a proper rotation
    return name, p2, R2


def shape_mirror_ok(name, R, T):
    """Is the piece's own shape symmetric under the world mirror T (expressed in its local frame)?"""
    if name not in bounds:
        return False
    lo, hi = bounds[name]
    Ml = R.T @ T @ R   # local-frame reflection
    # Only axis-aligned local reflections can be checked against an AABB.
    if not np.allclose(np.abs(Ml), np.round(np.abs(Ml)), atol=0.05):
        return False
    ax = [i for i in range(3) if Ml[i, i] < -0.5]
    if len(ax) != 1:
        return False
    i = ax[0]
    return abs(lo[i] + hi[i]) < 0.25  # bounds centered on the mirrored axis


def same(a, b, tol=0.3):
    return a[0] == b[0] and np.linalg.norm(a[1] - b[1]) < tol and np.allclose(a[2], b[2], atol=0.08)


def occupied(pieces, p, tol=0.6):
    return any(np.linalg.norm(o[1] - p) < tol for o in pieces)


def analyze(loc):
    pcs = load_pieces(loc)
    if not pcs:
        return None
    P = np.array([p[1] for p in pcs])
    lo, hi = P.min(0), P.max(0)
    centers = {'bbox': np.array([(lo[0] + hi[0]) / 2, 0, (lo[2] + hi[2]) / 2]),
               'origin': np.zeros(3),
               'centroid': np.array([P[:, 0].mean(), 0, P[:, 2].mean()])}
    results = []
    for cname, c in centers.items():
        c = np.round(c * 2) / 2  # snap to the half-meter build grid
        for tname, T, cc, mirror in transforms(c):
            imgs = [apply(T, cc, pc, mirror) for pc in pcs]
            hits = sum(1 for im in imgs if any(same(im, o) for o in pcs))
            results.append((hits / len(pcs), cname, tname, c, T, mirror))
    results.sort(key=lambda x: -x[0])
    syms = [r for r in results if r[0] >= 0.6]
    adds = []
    for score, cname, tname, c, T, mirror in syms:
        for pc in pcs:
            im = apply(T, c, pc, mirror)
            if any(same(im, o) for o in pcs) or occupied(pcs + adds, im[1]):
                continue
            if mirror and not shape_mirror_ok(pc[0], pc[2], T):
                continue
            if im[1][1] < -1.5:  # never below the foundation
                continue
            adds.append(im)
    return {
        'loc': loc, 'pieces': len(pcs), 'size': (hi - lo).round(1).tolist(),
        'top': [(round(s, 3), cn, tn, c.tolist()) for s, cn, tn, c, _, _ in results[:6]],
        'symmetries': [(round(s, 3), cn, tn, c.tolist()) for s, cn, tn, c, _, _ in syms],
        'additions': adds,
        'counts': collections.Counter(p[0] for p in pcs),
    }


report = {}
lines = ['# location\tprefab\tx,y,z\tqx,qy,qz,qw  (generated by scripts/ruins/analyze.py)']
for loc in RUINS:
    a = analyze(loc)
    if a is None:
        print(loc, 'no pieces'); continue
    print(f"\n== {loc}: {a['pieces']} pieces, size {a['size']}")
    for t in a['top']:
        print('   ', t)
    print(f"   -> {len(a['additions'])} completions: {collections.Counter(x[0] for x in a['additions'])}")
    for name, p, R in a['additions']:
        q = mat_to_quat(R)
        lines.append(f"{loc}\t{name}\t{p[0]:.3f},{p[1]:.3f},{p[2]:.3f}\t{q[0]:.4f},{q[1]:.4f},{q[2]:.4f},{q[3]:.4f}")
    report[loc] = {k: (v if k != 'additions' else [(n, p.round(2).tolist()) for n, p, _ in v]) for k, v in a.items() if k != 'counts'}
    report[loc]['counts'] = dict(a['counts'])

# cross-building module comparison: which piece kinds each building uses
kinds = {loc: set(report[loc]['counts']) for loc in report}
report['_kit'] = {loc: sorted(k) for loc, k in kinds.items()}
open('additions.tsv', 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
json.dump(report, open('report.json', 'w'), indent=1, default=str)
print('\nwrote additions.tsv', len(lines) - 1)
