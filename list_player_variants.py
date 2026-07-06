"""
List all PlayerCharacter_01 mesh variants in the c4210710 bundle.
Shows vertex count, triangle count, bone count, and hierarchy for each.
Purpose: identify which variant is the naked body vs clothed variants.
"""

import UnityPy
import os
import struct

BUNDLE_PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710"

FORMAT_SIZES = {0: 4, 1: 2, 2: 1, 3: 1, 4: 2, 5: 2, 6: 1, 7: 1, 10: 2, 11: 2, 12: 4, 13: 4}


def find_ancestor_name(go, depth=5):
    names = []
    current_go = go
    for _ in range(depth):
        try:
            name = getattr(current_go, "m_Name", "")
            names.append(name)
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


def get_channel_info(mesh):
    """Get a summary of vertex data channels for debugging."""
    vd = mesh.m_VertexData
    channels = vd.m_Channels
    info = []
    for ci, ch in enumerate(channels):
        if ch.dimension == 0:
            continue
        info.append(f"ch{ci}(dim={ch.dimension},fmt={ch.format},stream={ch.stream},off={ch.offset})")
    return ", ".join(info)


def main():
    print(f"Loading bundle: {os.path.basename(BUNDLE_PATH)}")
    env = UnityPy.load(BUNDLE_PATH)

    variants = []

    for obj in env.objects:
        if obj.type.name != "SkinnedMeshRenderer":
            continue
        try:
            smr_data = obj.read()
            mesh = smr_data.m_Mesh.read()
        except:
            continue

        mesh_name = getattr(mesh, "m_Name", "") or ""
        if "playercharacter" not in mesh_name.lower():
            continue

        go_name = ""
        ancestor_names = []
        try:
            go = smr_data.m_GameObject.read()
            go_name = getattr(go, "m_Name", "") or ""
            ancestor_names = find_ancestor_name(go)
        except:
            pass

        vd = mesh.m_VertexData
        vert_count = vd.m_VertexCount

        # Count triangles from index buffer
        idx_buf = getattr(mesh, "m_IndexBuffer", [])
        tri_count = 0
        if idx_buf:
            idx_bytes = bytes(idx_buf)
            if vert_count > 65535:
                tri_count = len(idx_bytes) // 12
            else:
                tri_count = len(idx_bytes) // 6

        # Bone count from bind poses
        raw_bp = getattr(mesh, "m_BindPose", [])
        bone_count = len(raw_bp)

        # Bone names from SMR
        m_bones = getattr(smr_data, "m_Bones", [])
        bone_name_count = len(m_bones)

        # Channel info for debugging weight extraction
        ch_info = get_channel_info(mesh)

        ancestor_str = " > ".join(reversed(ancestor_names))

        variants.append({
            "path_id": obj.path_id,
            "mesh_name": mesh_name,
            "go_name": go_name,
            "vert_count": vert_count,
            "tri_count": tri_count,
            "bone_count": bone_count,
            "bone_name_count": bone_name_count,
            "hierarchy": ancestor_str,
            "channels": ch_info,
        })

    # Sort by vertex count
    variants.sort(key=lambda v: v["vert_count"])

    print(f"\nFound {len(variants)} PlayerCharacter variants:\n")
    print(f"{'#':>3}  {'Verts':>6}  {'Tris':>6}  {'Bones':>5}  {'GO Name':<20}  {'Hierarchy'}")
    print("-" * 120)
    for i, v in enumerate(variants):
        print(f"{i+1:>3}  {v['vert_count']:>6}  {v['tri_count']:>6}  {v['bone_count']:>5}  {v['go_name']:<20}  {v['hierarchy']}")

    # Show channel info for first and last (to compare formats)
    if variants:
        print(f"\nChannel layout (lowest verts): {variants[0]['channels']}")
        print(f"Channel layout (highest verts): {variants[-1]['channels']}")


if __name__ == "__main__":
    main()
