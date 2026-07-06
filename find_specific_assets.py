import UnityPy
import os
import glob

def find_assets(game_data_path, targets):
    results = {target: [] for target in targets}
    asset_files = glob.glob(os.path.join(game_data_path, "*.assets"))
    
    print(f"Scanning {len(asset_files)} asset files for {targets}...")
    
    for file_path in asset_files:
        try:
            env = UnityPy.load(file_path)
            for obj in env.objects:
                if obj.type.name in ["GameObject", "Mesh"]:
                    try:
                        data = obj.read()
                        name = getattr(data, "name", "")
                        for target in targets:
                            if target.lower() in name.lower():
                                results[target].append({
                                    "file": os.path.basename(file_path),
                                    "type": obj.type.name,
                                    "name": name,
                                    "path_id": obj.path_id
                                })
                    except:
                        continue
        except Exception as e:
            print(f"Error loading {file_path}: {e}")
            
    for target, found in results.items():
        print(f"\n--- Results for {target} ---")
        if not found:
            print("Not found.")
        for item in found:
            print(f"[{item['type']}] {item['name']} in {item['file']} (PathID: {item['path_id']})")

if __name__ == "__main__":
    game_path = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data"
    targets = ["ArmorWolfChest", "Charred_Melee", "Player"]
    find_assets(game_path, targets)
