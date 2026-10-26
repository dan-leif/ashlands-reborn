"""Entrance analysis for restored ruins (input: recon.tsv).

For every ruin location and camp room, take the FULLY ROLLED blueprint (every RandomSpawn on,
as the restoration builds it) and rasterize the walls + pillars that stand in the ground-floor
band (0.4..2.0 m above the location origin) onto a 0.5 m grid, using each prefab's bounds from the
catalog. Flood-fill from outside: if the fill reaches the building's centre, it has an entrance.

Sealed buildings get a door: try removing one wall group (all fragments sharing a pivot) at a time,
preferring the +Z face (it faces the trail in the park) and the slot nearest the centre line, until
the centre becomes reachable. Output: ../../AshlandsReborn/Data/ruins_doors.tsv
(location \t x,y,z of the door pivot, location-local) and a summary.
"""
import importlib.util, sys, io, contextlib, collections
import numpy as np

spec = importlib.util.spec_from_file_location('A', 'analyze.py'); A = importlib.util.module_from_spec(spec)
sys.argv = ['x', 'recon.tsv']
with contextlib.redirect_stdout(io.StringIO()): spec.loader.exec_module(A)

LOCS = ['CharredRuins1', 'CharredRuins2', 'CharredRuins3', 'CharredRuins4',
        'CharredTowerRuins1', 'CharredTowerRuins1_dvergr', 'CharredTowerRuins2', 'CharredTowerRuins3']
ROOMS = sorted({r[1] for r in A.rows if r[0] == 'NODE' and r[1].startswith('room:')})
CELL = 0.5
BAND = (0.4, 2.0)


def blocking(name):
    return ('Wall' in name or 'Pillar' in name or 'Gate' in name) and 'Arch' not in name and 'Roof' not in name


def cells_of(name, p, R):
    if name not in A.bounds: return set()
    lo, hi = A.bounds[name]
    xs = np.arange(lo[0], hi[0] + 1e-6, 0.3); ys = np.arange(lo[1], hi[1] + 1e-6, 0.3); zs = np.arange(lo[2], hi[2] + 1e-6, 0.3)
    g = np.stack(np.meshgrid(xs, ys, zs, indexing='ij'), -1).reshape(-1, 3)
    w = (R @ g.T).T + p
    w = w[(w[:, 1] >= BAND[0]) & (w[:, 1] <= BAND[1])]
    return set(map(tuple, np.floor(w[:, [0, 2]] / CELL).astype(int)))


def reachable(blocked, lo, hi, center):
    from collections import deque
    x0, z0, x1, z1 = lo[0], lo[1], hi[0], hi[1]
    seen = set(); q = deque()
    for x in range(x0, x1 + 1):
        for z in (z0, z1): q.append((x, z))
    for z in range(z0, z1 + 1):
        for x in (x0, x1): q.append((x, z))
    while q:
        c = q.popleft()
        if c in seen or c in blocked or not (x0 <= c[0] <= x1 and z0 <= c[1] <= z1): continue
        seen.add(c)
        if abs(c[0] - center[0]) <= 1 and abs(c[1] - center[1]) <= 1: return True
        x, z = c
        q.extend([(x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1)])
    return False


def analyze(loc):
    pcs = A.load_pieces(loc)
    walls = [(n, p, R) for n, p, R in pcs if blocking(n)]
    if not walls: return 'open (no walls)', []
    groups = collections.defaultdict(list)
    for n, p, R in walls: groups[tuple(np.round(p, 2))].append((n, p, R))
    gcells = {k: set().union(*(cells_of(n, p, R) for n, p, R in v)) for k, v in groups.items()}
    blocked = set().union(*gcells.values()) if gcells else set()
    if not blocked: return 'open (no ground walls)', []
    P = np.array([p for _, p, _ in pcs])
    c = (np.floor(np.array([(P[:, 0].min() + P[:, 0].max()) / 2, (P[:, 2].min() + P[:, 2].max()) / 2]) / CELL)).astype(int)
    xs = [k[0] for k in blocked]; zs = [k[1] for k in blocked]
    lo = (min(xs) - 4, min(zs) - 4); hi = (max(xs) + 4, max(zs) + 4)
    if tuple(c) in blocked:  # centre sits in a wall/pillar: use the nearest free cell
        free = [(x, z) for x in range(lo[0], hi[0]) for z in range(lo[1], hi[1]) if (x, z) not in blocked]
        c = min(free, key=lambda f: (f[0] - c[0]) ** 2 + (f[1] - c[1]) ** 2)
    if reachable(blocked, lo, hi, c): return 'open', []
    # sealed: rank ground wall groups, +Z face first, nearest the centre line
    cand = [k for k, v in gcells.items() if v]
    zmax = max(k[2] for k in cand)
    cand.sort(key=lambda k: (0 if k[2] > zmax - 1.5 else 1, abs(float(k[0])), -float(k[2])))
    for k in cand:
        if reachable(blocked - gcells[k], lo, hi, c):
            return 'sealed -> door', [k]
    for i, k in enumerate(cand):  # two adjacent groups (e.g. wall + inner wall)
        for k2 in cand[i + 1:i + 8]:
            if reachable(blocked - gcells[k] - gcells[k2], lo, hi, c):
                return 'sealed -> double door', [k, k2]
    return 'sealed, no single door found', []


lines = ['# location\tdoor pivot x,y,z (location/room-local)  (scripts/ruins/doors.py)']
for loc in LOCS + ROOMS:
    status, doors = analyze(loc)
    print(f'{loc:40s} {status} {doors}')
    for d in doors:
        lines.append(f'{loc}\t{d[0]:.3f},{d[1]:.3f},{d[2]:.3f}')
open('../../AshlandsReborn/Data/ruins_doors.tsv', 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
print('doors:', len(lines) - 1)
