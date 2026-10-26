"""Fast symmetry-center search (voxel-hash lookups) for the facade pieces of each ruin."""
import importlib.util, sys, io, contextlib, collections, itertools
import numpy as np
ARGS = sys.argv[1:]
spec = importlib.util.spec_from_file_location('A', 'analyze.py'); A = importlib.util.module_from_spec(spec)
sys.argv = ['x', 'recon.tsv']
with contextlib.redirect_stdout(io.StringIO()): spec.loader.exec_module(A)
INTERIOR = {'Ashland_Steepstair', 'Ashlands_Altar', 'Ashlands_Ruins_Floor_1point5x1point5', 'Ashlands_Ruins_Floor_3x3_broken1',
            'Ashlands_Ruins_Floor_3x3_broken2', 'Ashlands_Ruins_Floor_3x3_broken3', 'Ashlands_Ruins_Floor_6x6_broken1'}
def key(p): return tuple(np.round(p * 2).astype(int))
NB = list(itertools.product((-1, 0, 1), repeat=3))
for loc in ARGS or ['CharredRuins1', 'CharredRuins2', 'CharredRuins3', 'CharredRuins4']:
    pcs = [p for p in A.load_pieces(loc) if p[0] not in INTERIOR]
    occ = set(key(p[1]) for p in pcs)
    P = np.array([p[1] for p in pcs]); lo, hi = P.min(0), P.max(0); bc = (lo + hi) / 2
    res = []
    for dx in np.arange(-4, 4.5, 0.5):
        for dz in np.arange(-4, 4.5, 0.5):
            c = np.array([round(bc[0] * 2) / 2 + dx, 0, round(bc[2] * 2) / 2 + dz])
            for t, T, _, m in A.transforms(c):
                img = (T @ (P - c).T).T + c
                holes = [i for i, q in enumerate(img) if not any((k[0] + a, k[1] + b, k[2] + d) in occ for a, b, d in NB for k in [key(q)])]
                res.append((1 - len(holes) / len(pcs), t, c, holes))
    res.sort(key=lambda r: -r[0])
    print(f"\n{loc}: facade={len(pcs)} bbox x[{lo[0]:.1f},{hi[0]:.1f}] z[{lo[2]:.1f},{hi[2]:.1f}] y[{lo[1]:.1f},{hi[1]:.1f}]")
    for cov, t, c, h in res[:4]:
        print(f"  {cov:.3f} {t} c=({c[0]},{c[2]}) holes={len(h)} {dict(collections.Counter(pcs[i][0].replace('Ashlands_', '') for i in h))}")
