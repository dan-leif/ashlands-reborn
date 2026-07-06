import UnityPy
import os

# All key assets confirmed to be in bundle c4210710
BUNDLE_PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710"
OUTPUT_DIR = r"c:\DEV\ashlands-reborn\extracted_assets"

# Exact names confirmed by scan_rigging.py
# Note: Charred_Melee is a GameObject prefab root, not a raw Mesh.
# Its skin mesh may be stored under a different name. We search broadly.
MESH_TARGETS = [
    "PlayerCharacter_01",   # Player mesh (the bind-pose reference)
    "ArmorWolfChest",       # Wolf chest armor mesh
    "Charred",              # Catches Charred_Melee, Charred_Gib, etc.
    "knightchest",          # SouthsilArmor (may not be present here)
]

# path_ids of the specific key assets we pinpointed - used for deduplication
# Only the FIRST match per unique name root is saved (lowest path_id seen first)
SAVED_NAMES = set()

def safe_name(name, path_id):
    """Make a unique filename from asset name + path_id."""
    safe = name.replace(" ", "_").replace("/", "_").replace("\\", "_")
    return f"{safe}_{abs(path_id) % 100000}"

def export_mesh(mesh_data, label, output_dir):
    """Export a Mesh object to OBJ format."""
    try:
        obj_text = mesh_data.export()
        if not obj_text or len(obj_text) < 10:
            print(f"  -> Skipped (empty OBJ): {label}")
            return False
        out_path = os.path.join(output_dir, f"{label}.obj")
        with open(out_path, "w") as f:
            f.write(obj_text)
        size_kb = os.path.getsize(out_path) // 1024
        print(f"  -> Saved: {label}.obj  ({size_kb} KB)")
        return True
    except Exception as e:
        print(f"  -> OBJ export failed for {label}: {e}")
        return False

def extract_assets(bundle_path, target_names, output_dir):
    os.makedirs(output_dir, exist_ok=True)
    print(f"\nLoading bundle: {os.path.basename(bundle_path)}")
    env = UnityPy.load(bundle_path)

    exported = []

    for obj in env.objects:
        try:
            if obj.type.name == "Mesh":
                data = obj.read()
                name = getattr(data, "m_Name", "") or ""
                if not name:
                    continue
                if any(t.lower() in name.lower() for t in target_names):
                    label = safe_name(name, obj.path_id)
                    print(f"Found Mesh: {name} (path_id={obj.path_id})")
                    if export_mesh(data, label, output_dir):
                        exported.append(label)

            elif obj.type.name == "SkinnedMeshRenderer":
                data = obj.read()
                name = getattr(data, "m_Name", "") or ""
                if not name:
                    continue
                if any(t.lower() in name.lower() for t in target_names):
                    print(f"Found SkinnedMeshRenderer: {name}")
                    try:
                        mesh_obj = data.m_Mesh.read()
                        mesh_name = getattr(mesh_obj, "m_Name", "") or name
                        label = safe_name(mesh_name + "_skinned", obj.path_id)
                        if export_mesh(mesh_obj, label, output_dir):
                            exported.append(label)
                    except Exception as e:
                        print(f"  -> Could not read m_Mesh for {name}: {e}")

        except Exception:
            continue

    return exported

if __name__ == "__main__":
    # Clear old output
    import shutil
    if os.path.exists(OUTPUT_DIR):
        shutil.rmtree(OUTPUT_DIR)

    all_exported = extract_assets(BUNDLE_PATH, MESH_TARGETS, OUTPUT_DIR)

    print(f"\n=== Done: {len(all_exported)} mesh(es) exported to {OUTPUT_DIR} ===")
    for name in sorted(set(all_exported)):
        path = os.path.join(OUTPUT_DIR, f"{name}.obj")
        size_kb = os.path.getsize(path) // 1024 if os.path.exists(path) else 0
        print(f"  {name}.obj  ({size_kb} KB)")
