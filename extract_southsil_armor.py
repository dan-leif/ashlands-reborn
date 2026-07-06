"""
Extract skinned mesh data from SouthsilArmor's embedded asset bundles.

The SouthsilArmor DLL (ItemManagerModTemplate.dll) embeds 120+ Unity asset bundles.
This script finds the knightchest and knightlegs bundles, then extracts full skinning
data: vertices, normals, UVs, triangles, bone weights, bind poses, bone names.

Output format matches player_body_skinning.json for consistency with the existing pipeline.

Outputs:
  extracted_assets/knightchest_skinning.json
  extracted_assets/knightlegs_skinning.json
"""

import UnityPy
import json
import os
import re
import struct

DLL_PATH = r"C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\southsil-SouthsilArmor\ItemManagerModTemplate.dll"
OUTPUT_DIR = r"c:\DEV\ashlands-reborn\extracted_assets"

# Format sizes in bytes per component
FORMAT_SIZES = {0: 4, 1: 2, 2: 1, 3: 1, 4: 2, 5: 2, 6: 1, 7: 1, 10: 2, 11: 2, 12: 4, 13: 4}
# struct format chars for unpacking
FORMAT_CHARS = {0: 'f', 1: 'e', 2: 'B', 6: 'B', 10: 'H', 12: 'I'}


def resolve_bone_name(bone_pptr):
    """Follow PPtr: m_Bones[i] -> Transform -> m_GameObject -> m_Name"""
    try:
        transform = bone_pptr.read()
        go = transform.m_GameObject.read()
        return getattr(go, "m_Name", "unknown")
    except Exception as e:
        return f"bone_err_{e}"


def compute_stream_strides(channels, vert_count=0, buf_size=0):
    """Compute byte stride for each stream, accounting for Unity alignment padding."""
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
    """Compute byte offset of each stream within the data buffer."""
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
    """Read all vertex values for a given channel index."""
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


def extract_mesh_skinning(mesh, smr_data):
    """Extract full skinning data including normals and UVs."""
    name = getattr(mesh, "m_Name", "unnamed")
    vd = mesh.m_VertexData
    vert_count = vd.m_VertexCount
    channels = vd.m_Channels
    data_buf = bytes(vd.m_DataSize)

    strides = compute_stream_strides(channels, vert_count, len(data_buf))
    stream_offsets = compute_stream_offsets(strides, vert_count, len(data_buf))

    # Log channel layout
    print(f"  Channels ({len(channels)}):")
    for ci, ch in enumerate(channels):
        if ch.dimension > 0:
            print(f"    [{ci}] stream={ch.stream} offset={ch.offset} fmt={ch.format} dim={ch.dimension}")

    # Channel 0 = Position (3x Float32)
    vertices = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, 0)

    # Channel 1 = Normal (3x Float32)
    normals = None
    if len(channels) > 1 and channels[1].dimension > 0:
        normals = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, 1)

    # UV0 — typically channel 3 or 4, can be Float32 (fmt=0) or Float16 (fmt=1)
    uvs = None
    for uv_ci in range(2, min(len(channels), 8)):
        ch = channels[uv_ci]
        if ch.dimension == 2 and ch.format in (0, 1):  # 2x Float32 or 2x Float16
            uvs = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, uv_ci)
            fmt_name = "Float32" if ch.format == 0 else "Float16"
            print(f"  UV channel found at index {uv_ci} ({fmt_name})")
            break

    # Triangles from m_IndexBuffer
    idx_buf = getattr(mesh, "m_IndexBuffer", [])
    triangles = []
    if idx_buf:
        idx_bytes = bytes(idx_buf)
        if vert_count > 65535:
            for i in range(0, len(idx_bytes) - 11, 12):
                i0, i1, i2 = struct.unpack_from('<III', idx_bytes, i)
                triangles.append([i0, i1, i2])
        else:
            for i in range(0, len(idx_bytes) - 5, 6):
                i0, i1, i2 = struct.unpack_from('<HHH', idx_bytes, i)
                triangles.append([i0, i1, i2])

    # Bind poses
    raw_bp = getattr(mesh, "m_BindPose", [])
    bindposes = []
    for bp in raw_bp:
        if hasattr(bp, 'e00'):
            mat = [
                bp.e00, bp.e01, bp.e02, bp.e03,
                bp.e10, bp.e11, bp.e12, bp.e13,
                bp.e20, bp.e21, bp.e22, bp.e23,
                bp.e30, bp.e31, bp.e32, bp.e33,
            ]
            bindposes.append(mat)

    # Bone weights from channels 12 (weights) and 13 (indices)
    bone_weights = []
    weights_data = None
    indices_data = None
    weights_dim = 0
    indices_dim = 0

    for ci, ch in enumerate(channels):
        if ch.format == 0 and ci >= 8 and ch.dimension > 0:  # Float32 weights
            weights_data = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, ci)
            weights_dim = ch.dimension
        if ch.format in (10, 2, 6) and ci >= 8 and ch.dimension > 0:  # UInt16/UInt8 bone indices
            indices_data = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, ci)
            indices_dim = ch.dimension

    if indices_data:
        for v in range(vert_count):
            idx = indices_data[v]
            w = weights_data[v] if weights_data else None
            bw = {}
            for i in range(4):
                if i < indices_dim:
                    bw[f"b{i}"] = int(idx[i])
                    if w and i < weights_dim:
                        bw[f"w{i}"] = float(w[i])
                    elif i == 0 and (not w or weights_dim == 0):
                        bw[f"w{i}"] = 1.0
                    else:
                        bw[f"w{i}"] = 0.0
                else:
                    bw[f"b{i}"] = 0
                    bw[f"w{i}"] = 0.0
            bone_weights.append(bw)

    # Bone names from SMR
    bone_names = []
    m_bones = getattr(smr_data, "m_Bones", [])
    for bone_pptr in m_bones:
        bone_names.append(resolve_bone_name(bone_pptr))

    result = {
        "name": name,
        "vertex_count": vert_count,
        "triangle_count": len(triangles),
        "bone_count": len(bindposes),
        "bone_names": bone_names,
        "vertices": vertices,
        "normals": normals,
        "uvs": uvs,
        "triangles": triangles,
        "bone_weights": bone_weights,
        "bindposes": bindposes,
    }

    print(f"  Extracted: {name} — {vert_count} verts, {len(triangles)} tris, "
          f"{len(bindposes)} BPs, {len(bone_weights)} weights, {len(bone_names)} bones")
    if normals:
        print(f"    Normals: {len(normals)}")
    if uvs:
        print(f"    UVs: {len(uvs)}")
    return result


def find_embedded_bundles(dll_data):
    """Find all UnityFS bundle offsets within a .NET DLL."""
    positions = [m.start() for m in re.finditer(b'UnityFS', dll_data)]
    return positions


def load_bundle_at_offset(dll_data, positions, bundle_idx):
    """Extract and load a single embedded bundle by index."""
    pos = positions[bundle_idx]
    end = positions[bundle_idx + 1] if bundle_idx + 1 < len(positions) else len(dll_data)
    bundle_bytes = dll_data[pos:end]
    return UnityPy.load(bundle_bytes)


def extract_from_bundle(env, bundle_name):
    """Find the SkinnedMeshRenderer in a bundle and extract its mesh data."""
    for obj in env.objects:
        if obj.type.name != "SkinnedMeshRenderer":
            continue
        try:
            smr_data = obj.read()
            mesh = smr_data.m_Mesh.read()
            mesh_name = getattr(mesh, "m_Name", "")
            bones = len(getattr(smr_data, "m_Bones", []))
            bp_count = len(getattr(mesh, "m_BindPose", []))
            print(f"\n  SMR found: mesh={mesh_name}, bones={bones}, bindposes={bp_count}")

            if bp_count == 0:
                print(f"  Skipping {mesh_name} — no bind poses (static mesh)")
                continue

            return extract_mesh_skinning(mesh, smr_data)
        except Exception as e:
            print(f"  Error reading SMR: {e}")
    return None


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    print(f"Loading DLL: {os.path.basename(DLL_PATH)}")
    print(f"  Size: {os.path.getsize(DLL_PATH) / 1024 / 1024:.1f} MB")

    with open(DLL_PATH, 'rb') as f:
        dll_data = f.read()

    positions = find_embedded_bundles(dll_data)
    print(f"  Found {len(positions)} embedded UnityFS bundles")

    # Find knightchest and knightlegs bundles by scanning for their AssetBundle names
    target_bundles = {}
    for i, pos in enumerate(positions):
        end = positions[i + 1] if i + 1 < len(positions) else len(dll_data)
        bundle_bytes = dll_data[pos:end]

        try:
            env = UnityPy.load(bundle_bytes)
            for obj in env.objects:
                if obj.type.name == "AssetBundle":
                    d = obj.read()
                    name = getattr(d, "m_Name", "")
                    if name in ("knightchest", "knightlegs"):
                        target_bundles[name] = (i, env)
                        print(f"  Found '{name}' at bundle index {i} (offset {pos})")
                    break  # AssetBundle is always the first/only one
        except:
            continue

        if len(target_bundles) == 2:
            break

    if not target_bundles:
        print("ERROR: No knight bundles found!")
        return

    # Extract each target
    for bundle_name in ("knightchest", "knightlegs"):
        if bundle_name not in target_bundles:
            print(f"\nWARNING: {bundle_name} bundle not found!")
            continue

        idx, env = target_bundles[bundle_name]
        print(f"\n=== Extracting {bundle_name} (bundle {idx}) ===")

        result = extract_from_bundle(env, bundle_name)
        if result:
            result["source"] = f"SouthsilArmor ({bundle_name} bundle)"
            out = {
                "source": result["source"],
                "mesh_count": 1,
                "meshes": [result],
            }
            path = os.path.join(OUTPUT_DIR, f"{bundle_name}_skinning.json")
            with open(path, "w") as f:
                json.dump(out, f, indent=2)
            print(f"\nSaved to {path}")
        else:
            print(f"\nERROR: Failed to extract {bundle_name}")

    # Summary
    print(f"\n=== Summary ===")
    for bundle_name in ("knightchest", "knightlegs"):
        path = os.path.join(OUTPUT_DIR, f"{bundle_name}_skinning.json")
        if os.path.exists(path):
            with open(path) as f:
                data = json.load(f)
            for m in data["meshes"]:
                print(f"  {bundle_name}: {m['name']} — {m['vertex_count']} verts, "
                      f"{m['triangle_count']} tris, {m['bone_count']} bones, "
                      f"{len(m['bone_weights'])} weights, "
                      f"normals={'yes' if m.get('normals') else 'no'}, "
                      f"uvs={'yes' if m.get('uvs') else 'no'}")


if __name__ == "__main__":
    main()
