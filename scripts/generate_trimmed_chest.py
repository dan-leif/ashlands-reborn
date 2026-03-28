"""
Generate knightchest_trimmed.bin — pre-built torso-only mesh with arm triangles removed.

Extracts vertex data (including colors) from the SouthsilArmor DLL's embedded knightchest
asset bundle, removes triangles weighted to arm bones (but keeps shoulders), strips
unreferenced vertices, and writes a compressed binary for embedding in AshlandsReborn.dll.

Binary format (little-endian, zlib-compressed):
  int32  vertCount
  int32  triIndexCount
  int32  boneCount
  float32[vertCount * 3]   vertices (x,y,z)
  float32[vertCount * 3]   normals (x,y,z)
  float32[vertCount * 2]   uvs (u,v)
  byte[vertCount * 4]      colors32 (r,g,b,a)   <-- NEW
  {int32 b0,b1,b2,b3; float32 w0,w1,w2,w3}[vertCount]  boneWeights
  uint16[triIndexCount]    triangle indices
  int32  bindPoseCount
  float32[bindPoseCount * 16]  bind poses (column-major)
"""

import UnityPy
import json
import os
import re
import struct
import zlib

DLL_PATH = r"C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\southsil-SouthsilArmor\ItemManagerModTemplate.dll"
OUTPUT_BIN = r"c:\DEV\ashlands-reborn\AshlandsReborn\Data\knightchest_trimmed.bin"
JSON_PATH = r"c:\DEV\ashlands-reborn\extracted_assets\knightchest_skinning.json"

# Arm bones to REMOVE — shoulders are intentionally excluded to keep pauldrons
ARM_BONE_NAMES = {
    "LeftArm", "LeftForeArm", "LeftHand",
    "LeftHandThumb1", "LeftHandThumb2", "LeftHandThumb3",
    "LeftHandIndex1", "LeftHandIndex2", "LeftHandIndex3",
    "LeftHandMiddle1", "LeftHandMiddle2", "LeftHandMiddle3",
    "LeftHandRing1", "LeftHandRing2", "LeftHandRing3",
    "LeftHandPinky1", "LeftHandPinky2", "LeftHandPinky3",
    "RightArm", "RightForeArm", "RightHand",
    "RightHandThumb1", "RightHandThumb2", "RightHandThumb3",
    "RightHandIndex1", "RightHandIndex2", "RightHandIndex3",
    "RightHandMiddle1", "RightHandMiddle2", "RightHandMiddle3",
    "RightHandRing1", "RightHandRing2", "RightHandRing3",
    "RightHandPinky1", "RightHandPinky2", "RightHandPinky3",
}

# Format sizes and unpack chars (same as extract_southsil_armor.py)
FORMAT_SIZES = {0: 4, 1: 2, 2: 1, 3: 1, 4: 2, 5: 2, 6: 1, 7: 1, 10: 2, 11: 2, 12: 4, 13: 4}
FORMAT_CHARS = {0: 'f', 1: 'e', 2: 'B', 6: 'B', 10: 'H', 12: 'I'}


def compute_stream_strides(channels, vert_count=0, buf_size=0):
    raw_strides = {}
    for ch in channels:
        if ch.dimension == 0:
            continue
        s = ch.stream
        byte_size = ch.dimension * FORMAT_SIZES.get(ch.format, 4)
        end = ch.offset + byte_size
        if s not in raw_strides or end > raw_strides[s]:
            raw_strides[s] = end

    if vert_count <= 0 or buf_size <= 0 or len(raw_strides) < 2:
        return raw_strides

    sorted_streams = sorted(raw_strides.keys())
    last_stream = sorted_streams[-1]
    earlier_bytes = sum(raw_strides[s] * vert_count for s in sorted_streams[:-1])
    remaining = buf_size - earlier_bytes
    for pad in range(0, 64, 2):
        usable = remaining - pad
        if usable > 0 and usable % vert_count == 0:
            actual_stride = usable // vert_count
            if actual_stride >= raw_strides[last_stream]:
                raw_strides[last_stream] = actual_stride
                break
    return raw_strides


def compute_stream_offsets(strides, vert_count, buf_size=0):
    sorted_streams = sorted(strides.keys())
    offsets = {}
    pos = 0
    for i, s in enumerate(sorted_streams):
        if i == len(sorted_streams) - 1 and buf_size > 0:
            offsets[s] = buf_size - strides[s] * vert_count
        else:
            offsets[s] = pos
            pos += strides[s] * vert_count
    return offsets


def read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, channel_idx):
    ch = channels[channel_idx]
    if ch.dimension == 0:
        return None
    stream = ch.stream
    stride = strides[stream]
    base = stream_offsets[stream]
    fmt_char = FORMAT_CHARS.get(ch.format, 'f')
    comp_size = FORMAT_SIZES.get(ch.format, 4)
    dim = ch.dimension
    offset = ch.offset

    result = []
    for v in range(vert_count):
        start = base + v * stride + offset
        components = []
        for d in range(dim):
            val = struct.unpack_from(f'<{fmt_char}', data_buf, start + d * comp_size)[0]
            components.append(val)
        result.append(components)
    return result


def resolve_bone_name(bone_pptr):
    try:
        transform = bone_pptr.read()
        go = transform.m_GameObject.read()
        return getattr(go, "m_Name", "unknown")
    except Exception as e:
        return f"bone_err_{e}"


def extract_knightchest():
    """Extract knightchest mesh data including vertex colors from the DLL."""
    print(f"Loading DLL: {os.path.basename(DLL_PATH)}")
    with open(DLL_PATH, 'rb') as f:
        dll_data = f.read()

    positions = [m.start() for m in re.finditer(b'UnityFS', dll_data)]
    print(f"  Found {len(positions)} embedded bundles")

    # Find knightchest bundle
    env = None
    for i, pos in enumerate(positions):
        end = positions[i + 1] if i + 1 < len(positions) else len(dll_data)
        bundle_bytes = dll_data[pos:end]
        try:
            candidate = UnityPy.load(bundle_bytes)
            for obj in candidate.objects:
                if obj.type.name == "AssetBundle":
                    d = obj.read()
                    name = getattr(d, 'm_Name', '')
                    if name == 'knightchest':
                        env = candidate
                        print(f"  Found knightchest at bundle index {i}")
        except:
            continue
        if env:
            break

    if not env:
        raise RuntimeError("knightchest bundle not found in DLL")

    # Find SkinnedMeshRenderer -> Mesh
    for obj in env.objects:
        if obj.type.name != "SkinnedMeshRenderer":
            continue
        smr_data = obj.read()
        mesh = smr_data.m_Mesh.read()
        if len(getattr(mesh, "m_BindPose", [])) == 0:
            continue

        vd = mesh.m_VertexData
        vert_count = vd.m_VertexCount
        channels = vd.m_Channels
        data_buf = bytes(vd.m_DataSize)
        strides = compute_stream_strides(channels, vert_count, len(data_buf))
        stream_offsets = compute_stream_offsets(strides, vert_count, len(data_buf))

        print(f"  Mesh: {getattr(mesh, 'm_Name', '?')}, {vert_count} verts")
        for ci, ch in enumerate(channels):
            if ch.dimension > 0:
                print(f"    [{ci}] stream={ch.stream} offset={ch.offset} fmt={ch.format} dim={ch.dimension}")

        # Channel 0: Position (3x Float32)
        vertices = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, 0)
        # Channel 1: Normal (3x Float32)
        normals = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, 1)
        # Channel 3: Color (4x UInt8 RGBA) — channel 2 is tangent
        colors = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, 3)
        # Channel 4: UV0 (2x Float16)
        uvs = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, 4)

        print(f"  Colors: {'yes' if colors else 'no'} ({len(colors) if colors else 0})")
        if colors:
            print(f"  Sample colors[0]: {colors[0]}")

        # Triangles
        idx_buf = getattr(mesh, "m_IndexBuffer", [])
        triangles = []
        if idx_buf:
            idx_bytes = bytes(idx_buf)
            for i in range(0, len(idx_bytes) - 5, 6):
                i0, i1, i2 = struct.unpack_from('<HHH', idx_bytes, i)
                triangles.append([i0, i1, i2])

        # Bind poses
        raw_bp = getattr(mesh, "m_BindPose", [])
        bindposes = []
        for bp in raw_bp:
            if hasattr(bp, 'e00'):
                bindposes.append([
                    bp.e00, bp.e01, bp.e02, bp.e03,
                    bp.e10, bp.e11, bp.e12, bp.e13,
                    bp.e20, bp.e21, bp.e22, bp.e23,
                    bp.e30, bp.e31, bp.e32, bp.e33,
                ])

        # Bone weights (channels 12=weights, 13=indices)
        bone_weights = []
        weights_data = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, 12)
        indices_data = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, 13)
        if indices_data:
            for v in range(vert_count):
                idx = indices_data[v]
                w = weights_data[v] if weights_data else [1, 0, 0, 0]
                bone_weights.append({
                    "b0": int(idx[0]), "b1": int(idx[1]), "b2": int(idx[2]), "b3": int(idx[3]),
                    "w0": float(w[0]), "w1": float(w[1]), "w2": float(w[2]), "w3": float(w[3]),
                })

        # Bone names
        bone_names = []
        for bone_pptr in getattr(smr_data, "m_Bones", []):
            bone_names.append(resolve_bone_name(bone_pptr))

        print(f"  Extracted: {vert_count} verts, {len(triangles)} tris, {len(bone_names)} bones")
        return {
            "vertices": vertices,
            "normals": normals,
            "uvs": uvs,
            "colors": colors,
            "triangles": triangles,
            "bone_weights": bone_weights,
            "bone_names": bone_names,
            "bindposes": bindposes,
        }

    raise RuntimeError("No SkinnedMeshRenderer with bind poses found")


def trim_and_generate(data):
    """Remove arm triangles, strip unreferenced verts, write binary."""
    bone_names = data["bone_names"]
    vertices = data["vertices"]
    normals = data["normals"]
    uvs = data["uvs"]
    colors = data["colors"]
    triangles = data["triangles"]
    bone_weights = data["bone_weights"]
    bindposes = data["bindposes"]

    vert_count = len(vertices)

    # Build set of arm bone indices
    arm_bone_indices = set()
    for i, name in enumerate(bone_names):
        if name in ARM_BONE_NAMES:
            arm_bone_indices.add(i)
    print(f"\nArm bone indices ({len(arm_bone_indices)}): {sorted(arm_bone_indices)}")
    print(f"Kept bones (shoulders etc): {[n for n in bone_names if 'Shoulder' in n]}")

    # Mark vertices as arm if dominant weight is on an arm bone
    is_arm_vert = [False] * vert_count
    for vi in range(vert_count):
        bw = bone_weights[vi]
        # Sum weight on arm bones
        arm_weight = 0.0
        for j in range(4):
            if bw[f"b{j}"] in arm_bone_indices:
                arm_weight += bw[f"w{j}"]
        # If majority of weight is on arm bones, mark as arm vertex
        if arm_weight > 0.5:
            is_arm_vert[vi] = True

    # Remove triangles where ALL three verts are arm verts
    kept_tris = []
    for tri in triangles:
        if is_arm_vert[tri[0]] and is_arm_vert[tri[1]] and is_arm_vert[tri[2]]:
            continue
        kept_tris.append(tri)

    print(f"Triangles: {len(triangles)} -> {len(kept_tris)} (removed {len(triangles) - len(kept_tris)})")

    # Find referenced vertices
    used_verts = set()
    for tri in kept_tris:
        used_verts.update(tri)

    # Build remapping
    old_to_new = {}
    new_vertices = []
    new_normals = []
    new_uvs = []
    new_colors = []
    new_boneweights = []

    for old_idx in sorted(used_verts):
        new_idx = len(new_vertices)
        old_to_new[old_idx] = new_idx
        new_vertices.append(vertices[old_idx])
        new_normals.append(normals[old_idx])
        new_uvs.append(uvs[old_idx])
        new_colors.append(colors[old_idx] if colors else [255, 255, 255, 255])
        new_boneweights.append(bone_weights[old_idx])

    # Remap triangle indices
    new_tris = []
    for tri in kept_tris:
        new_tris.append([old_to_new[tri[0]], old_to_new[tri[1]], old_to_new[tri[2]]])

    new_vert_count = len(new_vertices)
    new_tri_count = len(new_tris)
    tri_index_count = new_tri_count * 3
    bone_count = len(bone_names)

    print(f"Vertices: {vert_count} -> {new_vert_count}")
    print(f"Triangles: {len(triangles)} -> {new_tri_count}")

    # Pack binary
    parts = []

    # Header: vertCount, triIndexCount, boneCount
    parts.append(struct.pack('<iii', new_vert_count, tri_index_count, bone_count))

    # Vertices (float32 x3)
    for v in new_vertices:
        parts.append(struct.pack('<fff', v[0], v[1], v[2]))

    # Normals (float32 x3)
    for n in new_normals:
        parts.append(struct.pack('<fff', n[0], n[1], n[2]))

    # UVs (float32 x2)
    for uv in new_uvs:
        parts.append(struct.pack('<ff', uv[0], uv[1]))

    # Colors32 (4 bytes RGBA per vertex)
    for c in new_colors:
        parts.append(struct.pack('<BBBB', int(c[0]), int(c[1]), int(c[2]), int(c[3])))

    # Bone weights (int32 x4 indices + float32 x4 weights)
    for bw in new_boneweights:
        parts.append(struct.pack('<iiii', bw["b0"], bw["b1"], bw["b2"], bw["b3"]))
        parts.append(struct.pack('<ffff', bw["w0"], bw["w1"], bw["w2"], bw["w3"]))

    # Triangle indices (uint16)
    for tri in new_tris:
        parts.append(struct.pack('<HHH', tri[0], tri[1], tri[2]))

    # Bind poses
    parts.append(struct.pack('<i', len(bindposes)))
    for bp in bindposes:
        for val in bp:
            parts.append(struct.pack('<f', val))

    raw = b''.join(parts)
    compressed = zlib.compress(raw)

    os.makedirs(os.path.dirname(OUTPUT_BIN), exist_ok=True)
    with open(OUTPUT_BIN, 'wb') as f:
        f.write(compressed)

    print(f"\nWrote {OUTPUT_BIN}")
    print(f"  Raw: {len(raw)} bytes, Compressed: {len(compressed)} bytes")
    print(f"  {new_vert_count} verts, {new_tri_count} tris, {bone_count} bones")
    print(f"  Colors: yes ({len(new_colors)} entries)")


if __name__ == "__main__":
    data = extract_knightchest()
    trim_and_generate(data)
