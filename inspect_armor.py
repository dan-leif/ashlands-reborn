import UnityPy
import os

BUNDLE_PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710"
OUTPUT_DIR = r"c:\DEV\ashlands-reborn\extracted_assets"

INSPECT_TARGETS = ["armorwolf", "charred_melee"]

def inspect_gameobject(data, obj, indent=0):
    prefix = "  " * indent
    name = getattr(data, "m_Name", "") or ""
    print(f"{prefix}[GameObject] {name}  (path_id={obj.path_id})")

    components = getattr(data, "m_Component", [])
    for comp_ref in components:
        try:
            pptr = getattr(comp_ref, "component", comp_ref)
            comp_obj = pptr.read()
            comp_type = type(comp_obj).__name__
            comp_name = getattr(comp_obj, "m_Name", "") or ""

            print(f"{prefix}  -> [{comp_type}] {comp_name}")

            if comp_type == "SkinnedMeshRenderer":
                try:
                    mesh = comp_obj.m_Mesh.read()
                    mesh_name = getattr(mesh, "m_Name", "") or "unnamed"
                    # Vertex count stored as flat list (x,y,z per vert)
                    verts = getattr(mesh, "m_Vertices", [])
                    vert_count = len(verts) // 3 if verts else "?"
                    print(f"{prefix}     Mesh name : {mesh_name}")
                    print(f"{prefix}     Vert count: {vert_count}")
                    # Export it
                    label = mesh_name.replace(" ", "_") if mesh_name != "unnamed" else f"mesh_{abs(pptr.path_id) % 99999}"
                    out_path = os.path.join(OUTPUT_DIR, f"{label}_from_{name.replace(' ','_')}.obj")
                    os.makedirs(OUTPUT_DIR, exist_ok=True)
                    try:
                        obj_text = mesh.export()
                        with open(out_path, "w") as f:
                            f.write(obj_text)
                        size_kb = os.path.getsize(out_path) // 1024
                        print(f"{prefix}     Exported : {os.path.basename(out_path)}  ({size_kb} KB)")
                    except Exception as ex:
                        print(f"{prefix}     Export failed: {ex}")
                except Exception as me:
                    print(f"{prefix}     (mesh read error: {me})")

            elif comp_type == "MeshFilter":
                try:
                    mesh = comp_obj.m_Mesh.read()
                    mesh_name = getattr(mesh, "m_Name", "") or "unnamed"
                    print(f"{prefix}     Mesh name : {mesh_name}")
                except Exception as me:
                    print(f"{prefix}     (mesh read error: {me})")

        except Exception as e:
            print(f"{prefix}  -> [component error: {e}]")


def main():
    print(f"Loading bundle...")
    env = UnityPy.load(BUNDLE_PATH)

    for obj in env.objects:
        if obj.type.name != "GameObject":
            continue
        try:
            data = obj.read()
            name = getattr(data, "m_Name", "") or ""
            if any(t in name.lower() for t in INSPECT_TARGETS):
                print()
                inspect_gameobject(data, obj)
        except Exception as e:
            pass


if __name__ == "__main__":
    main()
