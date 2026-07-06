import UnityPy
import os
import glob

def find_rigging_assets(bundles_dir, target_names):
    print(f"Scanning bundles for rigging assets: {target_names}")
    bundles = glob.glob(os.path.join(bundles_dir, "*"))
    
    # Prioritize larger bundles, as they usually contain meshes/textures
    bundles = [b for b in bundles if os.path.isfile(b)]
    bundles.sort(key=lambda x: os.path.getsize(x), reverse=True)
    
    results = []
    # Only scan the top 50 largest bundles to avoid traffic issues
    for bundle_path in bundles[:50]:
        try:
            env = UnityPy.load(bundle_path)
            for obj in env.objects:
                if obj.type.name in ["Mesh", "SkinnedMeshRenderer", "Avatar", "GameObject"]:
                    try:
                        data = obj.read()
                        # UnityPy 1.24+ uses m_Name, not name
                        name = getattr(data, "m_Name", "") or ""
                        if any(target.lower() in name.lower() for target in target_names):
                            results.append({
                                "bundle": os.path.basename(bundle_path),
                                "type": obj.type.name,
                                "name": name,
                                "path_id": obj.path_id
                            })
                            print(f"Match: [{obj.type.name}] {name} in {os.path.basename(bundle_path)}")
                    except:
                        continue
        except:
            pass
            
    return results

if __name__ == "__main__":
    bundles_dir = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles"
    # Updated targets based on actual asset names confirmed in bundle c4210710
    targets = ["Charred_Melee", "ArmorWolfChest", "PlayerCharacter_01"]
    found = find_rigging_assets(bundles_dir, targets)
    
    print("\n--- Summary ---")
    for item in found:
        print(f"[{item['type']}] {item['name']} in {item['bundle']} (PathID: {item['path_id']})")
