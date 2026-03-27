"""Convert playdough_mesh_data.json to a compact binary format for C# embedding.

Performs Blender → Unity coordinate conversion:
  - Transforms vertices/normals from Blender world space to armature-local space
  - Negates X axis (right-handed → left-handed)
  - Reverses triangle winding (CCW → CW)

Binary format (little-endian):
  int32   vertexCount
  int32   boneCount
  int32   submesh0IndexCount  (number of indices, not triangles)
  int32   submesh1IndexCount
  float32[vertexCount * 3]   vertices (x,y,z interleaved)
  float32[vertexCount * 3]   normals
  float32[vertexCount * 2]   uvs
  int32  [vertexCount * 4]   boneWeightIndices
  float32[vertexCount * 4]   boneWeightWeights
  int32  [submesh0IndexCount] submesh0Triangles
  int32  [submesh1IndexCount] submesh1Triangles
  For each bone:
    int32   nameLength
    byte[nameLength]  nameUTF8
"""

import json
import struct
import sys
import numpy as np
from pathlib import Path


def mat4_from_flat(flat):
    """Convert 16 floats (row-major) to 4x4 numpy array."""
    return np.array(flat, dtype=np.float64).reshape(4, 4)


def invert_mat4(m):
    return np.linalg.inv(m)


def main():
    src = Path("extracted_assets/playdough_mesh_data.json")
    dst = Path("AshlandsReborn/playdough_mesh.bin")

    print(f"Reading {src} ...")
    data = json.loads(src.read_text())

    verts_raw = np.array(data["vertices"], dtype=np.float64)      # (N, 3)
    normals_raw = np.array(data["normals"], dtype=np.float64)      # (N, 3)
    uvs_raw = np.array(data["uvs"], dtype=np.float64)              # (N, 2)
    bone_names = data["bone_names"]
    bone_weights = data["bone_weights"]  # list of {indices: [4], weights: [4]}
    submesh0 = data["submeshes"][0]      # flat index list
    submesh1 = data["submeshes"][1]

    arm_world = mat4_from_flat(data["armature_world_matrix"])
    arm_world_inv = invert_mat4(arm_world)

    vert_count = len(verts_raw)
    bone_count = len(bone_names)
    print(f"Vertices: {vert_count}, Bones: {bone_count}")
    print(f"Submesh0: {len(submesh0)} indices, Submesh1: {len(submesh1)} indices")

    # --- Transform vertices to armature-local space ---
    # Add w=1 for affine transform
    ones = np.ones((vert_count, 1), dtype=np.float64)
    verts_h = np.hstack([verts_raw, ones])  # (N, 4)
    verts_local = (arm_world_inv @ verts_h.T).T[:, :3]  # (N, 3)

    # --- Transform normals (rotation only, no translation) ---
    # For normals, use the upper-left 3x3 of the inverse transpose
    # Since arm_world is rotation + translation (no scale), inv transpose rotation = rotation
    rot_inv = arm_world_inv[:3, :3]
    normals_local = (rot_inv @ normals_raw.T).T  # (N, 3)
    # Normalize
    norms = np.linalg.norm(normals_local, axis=1, keepdims=True)
    norms[norms < 1e-8] = 1.0
    normals_local = normals_local / norms

    # --- Blender → Unity: (-x, z, -y) ---
    # Blender Z-up right-handed → Unity Y-up left-handed
    # Negate X for handedness, swap Y↔Z for up-axis, negate new Z (old Y)
    verts_unity = np.column_stack([
        -verts_local[:, 0],   # Unity X = -Blender X
         verts_local[:, 2],   # Unity Y = Blender Z (height)
        -verts_local[:, 1],   # Unity Z = -Blender Y (forward)
    ])

    normals_unity = np.column_stack([
        -normals_local[:, 0],
         normals_local[:, 2],
        -normals_local[:, 1],
    ])

    # --- Reverse triangle winding (Blender CCW → Unity CW) ---
    # Swap indices 1 and 2 in each triangle
    def reverse_winding(indices):
        result = list(indices)
        for i in range(0, len(result), 3):
            result[i + 1], result[i + 2] = result[i + 2], result[i + 1]
        return result

    sub0_unity = reverse_winding(submesh0)
    sub1_unity = reverse_winding(submesh1)

    # --- Sanity checks ---
    print(f"Vertex range X: [{verts_unity[:, 0].min():.3f}, {verts_unity[:, 0].max():.3f}]")
    print(f"Vertex range Y: [{verts_unity[:, 1].min():.3f}, {verts_unity[:, 1].max():.3f}]")
    print(f"Vertex range Z: [{verts_unity[:, 2].min():.3f}, {verts_unity[:, 2].max():.3f}]")

    # --- Write binary ---
    print(f"Writing {dst} ...")
    with open(dst, "wb") as f:
        # Header
        f.write(struct.pack("<i", vert_count))
        f.write(struct.pack("<i", bone_count))
        f.write(struct.pack("<i", len(sub0_unity)))
        f.write(struct.pack("<i", len(sub1_unity)))

        # Vertices (float32)
        for v in verts_unity:
            f.write(struct.pack("<fff", float(v[0]), float(v[1]), float(v[2])))

        # Normals (float32)
        for n in normals_unity:
            f.write(struct.pack("<fff", float(n[0]), float(n[1]), float(n[2])))

        # UVs (float32) — unchanged between Blender and Unity
        for uv in uvs_raw:
            f.write(struct.pack("<ff", float(uv[0]), float(uv[1])))

        # Bone weight indices (int32 × 4)
        for bw in bone_weights:
            for idx in bw["indices"]:
                f.write(struct.pack("<i", idx))

        # Bone weight values (float32 × 4)
        for bw in bone_weights:
            for w in bw["weights"]:
                f.write(struct.pack("<f", float(w)))

        # Submesh 0 triangles (int32)
        for idx in sub0_unity:
            f.write(struct.pack("<i", idx))

        # Submesh 1 triangles (int32)
        for idx in sub1_unity:
            f.write(struct.pack("<i", idx))

        # Bone names
        for name in bone_names:
            name_bytes = name.encode("utf-8")
            f.write(struct.pack("<i", len(name_bytes)))
            f.write(name_bytes)

    size_kb = dst.stat().st_size / 1024
    print(f"Done: {dst} ({size_kb:.1f} KB)")


if __name__ == "__main__":
    main()
