"""
Extract skinned mesh data (vertices, triangles, bone weights, bind poses, bone names)
from Valheim asset bundles for Charred_Melee and Player characters.

UnityPy 1.25 stores vertex data in m_VertexData (packed channels), not m_Vertices.
We parse the raw byte buffer using channel descriptors.
Bone weights are in channels 12 (4xFloat32 weights) and 13 (4xUInt16 indices).

Outputs:
  extracted_assets/charred_body_skinning.json
  extracted_assets/player_body_skinning.json
"""

import UnityPy
import json
import os
import struct

BUNDLE_PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710"
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
    """Compute byte stride for each stream, accounting for Unity alignment padding.

    Unity pads stream strides beyond what channel descriptors indicate.
    We compute raw strides from channels, then derive the actual last-stream
    stride from the total buffer size.
    """
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

    # Earlier streams use raw strides. Compute bytes consumed by all but last stream.
    sorted_streams = sorted(raw_strides.keys())
    last_stream = sorted_streams[-1]
    earlier_bytes = sum(raw_strides[s] * vert_count for s in sorted_streams[:-1])

    # Remaining bytes for last stream (including inter-stream alignment padding)
    remaining = buf_size - earlier_bytes
    # The last stream stride = remaining // vert_count, but allow for padding
    # between earlier streams and last stream (up to 64 bytes).
    for pad in range(0, 64, 2):
        usable = remaining - pad
        if usable > 0 and usable % vert_count == 0:
            actual_stride = usable // vert_count
            if actual_stride >= raw_strides[last_stream]:
                raw_strides[last_stream] = actual_stride
                break

    return raw_strides


def compute_stream_offsets(strides, vert_count, buf_size=0):
    """Compute byte offset of each stream within the data buffer.

    Uses buffer size to find the correct start of the last stream,
    accounting for alignment padding between streams.
    """
    sorted_streams = sorted(strides.keys())
    offsets = {}
    pos = 0

    for i, s in enumerate(sorted_streams):
        if i == len(sorted_streams) - 1 and buf_size > 0:
            # Last stream: compute from end of buffer
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
    """Extract full skinning data from a UnityPy Mesh object."""
    name = getattr(mesh, "m_Name", "unnamed")
    vd = mesh.m_VertexData
    vert_count = vd.m_VertexCount
    channels = vd.m_Channels
    data_buf = bytes(vd.m_DataSize)

    strides = compute_stream_strides(channels, vert_count, len(data_buf))
    stream_offsets = compute_stream_offsets(strides, vert_count, len(data_buf))

    # Channel 0 = Position (3x Float32)
    vertices = read_channel_data(data_buf, channels, strides, stream_offsets, vert_count, 0)

    # Triangles from m_IndexBuffer
    idx_buf = getattr(mesh, "m_IndexBuffer", [])
    triangles = []
    if idx_buf:
        idx_bytes = bytes(idx_buf)
        # Determine index format: uint16 or uint32 based on vertex count
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
    # Dimension can be 1-4 depending on how many bone influences per vertex.
    # When weights channel is missing (dim=0 or absent), single-bone meshes have implicit weight=1.0.
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
                        bw[f"w{i}"] = 1.0  # implicit full weight for single-bone
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
        "triangles": triangles,
        "bone_weights": bone_weights,
        "bindposes": bindposes,
    }

    print(f"  Extracted: {name} — {vert_count} verts, {len(triangles)} tris, "
          f"{len(bindposes)} BPs, {len(bone_weights)} weights, {len(bone_names)} bones")
    return result


def find_ancestor_name(go, depth=5):
    """Walk up the GameObject hierarchy to find a named ancestor."""
    names = []
    current_go = go
    for _ in range(depth):
        try:
            name = getattr(current_go, "m_Name", "")
            names.append(name)
            # Go up: GO -> Transform component -> m_Father -> parent GO
            for cref in getattr(current_go, "m_Component", []):
                p = getattr(cref, "component", cref)
                c = p.read()
                if type(c).__name__ == "Transform":
                    parent_t = c.m_Father.read()
                    current_go = parent_t.m_GameObject.read()
                    break
            else:
                break
        except:
            break
    return names


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    print(f"Loading bundle: {os.path.basename(BUNDLE_PATH)}")
    env = UnityPy.load(BUNDLE_PATH)

    charred_meshes = []
    player_meshes = []
    seen_path_ids = set()

    for obj in env.objects:
        if obj.type.name != "SkinnedMeshRenderer":
            continue
        if obj.path_id in seen_path_ids:
            continue

        try:
            smr_data = obj.read()
            mesh = smr_data.m_Mesh.read()
        except:
            continue

        mesh_name = getattr(mesh, "m_Name", "") or ""
        go_name = ""
        ancestor_names = []
        try:
            go = smr_data.m_GameObject.read()
            go_name = getattr(go, "m_Name", "") or ""
            ancestor_names = find_ancestor_name(go)
        except:
            pass

        ancestor_str = " > ".join(reversed(ancestor_names))

        # Identify Charred_Melee body parts
        is_charred = any("charred_melee" in n.lower() or "thecharred" in n.lower() for n in ancestor_names)
        # Identify Player body (mesh named PlayerCharacter_01)
        is_player = "playercharacter" in mesh_name.lower()

        if not (is_charred or is_player):
            continue

        # For charred, only take the 4 known mesh parts
        if is_charred and go_name.lower() not in ("body", "eyes", "sinew", "skull"):
            continue

        seen_path_ids.add(obj.path_id)
        label = "Charred" if is_charred else "Player"
        print(f"\n{label} SMR: mesh={mesh_name}, GO={go_name}, hierarchy={ancestor_str}")

        result = extract_mesh_skinning(mesh, smr_data)
        if result:
            result["gameobject_name"] = go_name
            result["hierarchy"] = ancestor_str
            if is_charred:
                charred_meshes.append(result)
            else:
                player_meshes.append(result)

    # Deduplicate: keep one of each mesh part from Charred_Melee specifically
    if charred_meshes:
        # Prefer meshes from the Charred_Melee hierarchy
        seen_parts = {}
        for m in charred_meshes:
            part = m["gameobject_name"]  # Body, Eyes, Sinew, Skull
            hier = m.get("hierarchy", "")
            # Prefer Charred_Melee > Visual > TheCharred > X
            is_melee = "charred_melee >" in hier.lower() or hier.lower().startswith("charred_melee")
            if part not in seen_parts or (is_melee and "charred_melee" not in seen_parts[part].get("hierarchy", "").lower()):
                seen_parts[part] = m
        charred_meshes = list(seen_parts.values())
        print(f"\nDeduplicated Charred to {len(charred_meshes)} unique parts: {[m['gameobject_name'] for m in charred_meshes]}")

    # For Player, pick the naked body (lowest vertex count) with the most bones
    if player_meshes:
        min_verts = min(m["vertex_count"] for m in player_meshes)
        naked = [m for m in player_meshes if m["vertex_count"] == min_verts]
        best = max(naked, key=lambda m: m["bone_count"])
        player_meshes = [best]
        print(f"Kept naked Player mesh: {best['vertex_count']} verts, {best['bone_count']} bones")

    # Save
    if charred_meshes:
        out = {"source": "Charred_Melee (c4210710 bundle)", "mesh_count": len(charred_meshes), "meshes": charred_meshes}
        path = os.path.join(OUTPUT_DIR, "charred_body_skinning.json")
        with open(path, "w") as f:
            json.dump(out, f, indent=2)
        print(f"\nSaved {len(charred_meshes)} Charred mesh(es) to {path}")

    if player_meshes:
        out = {"source": "Player (c4210710 bundle)", "mesh_count": len(player_meshes), "meshes": player_meshes}
        path = os.path.join(OUTPUT_DIR, "player_body_skinning.json")
        with open(path, "w") as f:
            json.dump(out, f, indent=2)
        print(f"Saved {len(player_meshes)} Player mesh(es) to {path}")

    # Summary
    print(f"\n=== Summary ===")
    print(f"Charred meshes: {len(charred_meshes)}")
    for m in charred_meshes:
        print(f"  {m['name']} ({m['gameobject_name']}): {m['vertex_count']} verts, {m['bone_count']} bones, {len(m['bone_weights'])} weights")
    print(f"Player meshes: {len(player_meshes)}")
    for m in player_meshes:
        print(f"  {m['name']} ({m['gameobject_name']}): {m['vertex_count']} verts, {m['bone_count']} bones, {len(m['bone_weights'])} weights")

    if not charred_meshes:
        print("\nWARNING: No Charred meshes found!")
    if not player_meshes:
        print("\nWARNING: No Player meshes found!")


if __name__ == "__main__":
    main()
