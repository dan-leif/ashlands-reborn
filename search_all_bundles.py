import UnityPy
import os
import glob

def search_all_bundles(bundles_dir, target_substrings):
    print(f"Searching all bundles in {bundles_dir} for {target_substrings}...")
    bundles = glob.glob(os.path.join(bundles_dir, "*"))
    
    results = []
    
    for bundle_path in bundles:
        if os.path.isdir(bundle_path): continue
        try:
            env = UnityPy.load(bundle_path)
            for obj in env.objects:
                if obj.type.name == "AssetBundle":
                    data = obj.read()
                    container = getattr(data, "m_Container", [])
                    for path_str, asset_info in container:
                        for target in target_substrings:
                            if target.lower() in path_str.lower():
                                results.append({
                                    "bundle": os.path.basename(bundle_path),
                                    "path": path_str,
                                    "path_id": asset_info.asset.m_PathID
                                })
                                print(f"Found match: {path_str} (Bundle: {os.path.basename(bundle_path)})")
        except:
            pass
            
    return results

if __name__ == "__main__":
    bundles_dir = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles"
    targets = ["ArmorWolfChest", "Charred_Melee", "Player"]
    results = search_all_bundles(bundles_dir, targets)
    
    print("\n--- Final Summary ---")
    for r in results:
        print(f"Bundle: {r['bundle']}, Path: {r['path']}, PathID: {r['path_id']}")
