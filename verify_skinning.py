"""
Verify Python skinning math against Unity's ground truth from chest_runtime_matrices.json.
Unity reports:
  vertex 0: skinned_world = (77.1407394, 74.7016602, -9766.9082)
  vertex 1: skinned_world = (78.4133682, 74.5592804, -9766.21875)
  vertex 2: skinned_world = (70.1449203, 68.3409042, -9754.8916)

If Python matches these, the math is correct and the simulator's coordinate
conversion / visualization is the problem.
"""

import json
import numpy as np
from pathlib import Path

dump = json.loads(Path("extracted_assets/chest_runtime_matrices.json").read_text())

# Parse matrices from row-major flat arrays into 4x4 numpy arrays
def parse_m4(flat):
    """Row-major 16 floats → 4×4 numpy array"""
    return np.array(flat, dtype=np.float64).reshape(4, 4)

# Build bone L2W and bind pose arrays
bones = dump["bones"]
bindposes = dump["bindposes"]  # same as v12_bindposes per the match count

bone_l2w = {}
for b in bones:
    bone_l2w[b["index"]] = parse_m4(b["localToWorldMatrix"])

bp = {}
for b in bindposes:
    bp[b["index"]] = parse_m4(b["matrix"])

# Test vertices (same as C# code)
test_verts = [
    {"pos": [0.108580366, 0.150748387, 1.20352566],
     "bi": [1, 49, 2, 0], "wt": [0.892056406, 0.0624691807, 0.044204291, 0.00127011503]},
    {"pos": [0.167304620, 0.089224882, 1.19044089],
     "bi": [1, 2, 49, 0], "wt": [0.893103480, 0.053666711, 0.053229801, 0.0]},
    {"pos": [-0.0423966870, 0.0548427030, 1.21651220],
     "bi": [8, 7, 3, 2], "wt": [0.502074480, 0.294174800, 0.161498070, 0.042252656]},
]

unity_results = [
    [77.1407394, 74.7016602, -9766.9082],
    [78.4133682, 74.5592804, -9766.21875],
    [70.1449203, 68.3409042, -9754.8916],
]

print("=== Skinning verification: Python vs Unity ===\n")
for i, tv in enumerate(test_verts):
    v = np.array([*tv["pos"], 1.0])  # homogeneous
    skinned = np.zeros(3)
    for j in range(4):
        w = tv["wt"][j]
        if w <= 0:
            continue
        bi = tv["bi"][j]
        # Unity: skinMat = bone_L2W * bindpose; result = skinMat * v
        skin_mat = bone_l2w[bi] @ bp[bi]
        transformed = skin_mat @ v
        skinned += w * transformed[:3]

    unity = np.array(unity_results[i])
    diff = skinned - unity
    print(f"Vertex {i}:")
    print(f"  Python:  ({skinned[0]:.6f}, {skinned[1]:.6f}, {skinned[2]:.6f})")
    print(f"  Unity:   ({unity[0]:.6f}, {unity[1]:.6f}, {unity[2]:.6f})")
    print(f"  Diff:    ({diff[0]:.6f}, {diff[1]:.6f}, {diff[2]:.6f})")
    print(f"  Max err: {np.max(np.abs(diff)):.9f}")
    print()

print("If max error < 0.01 for all vertices, the Python matrix math is correct.")
print("The simulator issue is then in coordinate conversion or Blender visualization.")
