"""Symmetry completions -> additions.tsv. Rules per location: which mirror, center, and the
minimum storey height (ground-floor gaps can be doorways, so CharredRuins1 completes from y>=3).
Mirrored fragments use the conjugated rotation T R T: every wall fragment set forms a full
wall whose shape is symmetric under its local length-axis flip, so the set's union mirrors."""
import importlib.util, sys, io, contextlib, itertools, collections
import numpy as np
spec = importlib.util.spec_from_file_location('A', 'analyze.py'); A = importlib.util.module_from_spec(spec)
sys.argv = ['x', 'recon.tsv']
with contextlib.redirect_stdout(io.StringIO()): spec.loader.exec_module(A)
INTERIOR = {'Ashland_Steepstair', 'Ashlands_Altar', 'Ashlands_Ruins_Floor_1point5x1point5', 'Ashlands_Ruins_Floor_3x3_broken1',
            'Ashlands_Ruins_Floor_3x3_broken2', 'Ashlands_Ruins_Floor_3x3_broken3', 'Ashlands_Ruins_Floor_6x6_broken1'}
RULES = {'CharredRuins1': ('mirror_x', (0, 0), 3.0), 'CharredRuins3': ('mirror_z', (0, 0), -99), 'CharredRuins4': ('mirror_z', (0, 0), -99)}
def key(p): return tuple(np.round(p * 2).astype(int))
NB = list(itertools.product((-1, 0, 1), repeat=3))
lines = ['# location\tprefab\tx,y,z\tqx,qy,qz,qw  (scripts/ruins/gen_additions.py: symmetry completions)']
summary = {}
for loc, (t, (cx, cz), ymin) in RULES.items():
    pcs = [p for p in A.load_pieces(loc) if p[0] not in INTERIOR]
    occ = set(key(p[1]) for p in pcs)
    T = A.MIRRORS[t]; c = np.array([cx, 0, cz])
    adds = []
    for name, p, R in pcs:
        q = c + T @ (p - c)
        if q[1] < ymin: continue
        k = key(q)
        if any((k[0] + a, k[1] + b, k[2] + d) in occ for a, b, d in NB): continue
        R2 = T @ R @ T
        if any(n == name and np.linalg.norm(q - p2) < 0.2 for n, p2, _ in adds): continue
        adds.append((name, q, R2))
    for name, q, R2 in adds:
        qq = A.mat_to_quat(R2)
        lines.append(f"{loc}\t{name}\t{q[0]:.3f},{q[1]:.3f},{q[2]:.3f}\t{qq[0]:.4f},{qq[1]:.4f},{qq[2]:.4f},{qq[3]:.4f}")
    summary[loc] = collections.Counter(n for n, _, _ in adds)
    print(loc, len(adds), dict(summary[loc]))
open('../../AshlandsReborn/Data/ruins_additions.tsv', 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
