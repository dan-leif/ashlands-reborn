import UnityPy
import os

def scan_file(file_path, keywords):
    print(f"Scanning {file_path} for {keywords}...")
    try:
        env = UnityPy.load(file_path)
        for obj in env.objects:
            # Check for Mesh and GameObject (and maybe MonoBehaviour for items)
            if obj.type.name in ["Mesh", "GameObject", "MonoBehaviour"]:
                try:
                    data = obj.read()
                    name = getattr(data, "name", "")
                    if name:
                        for keyword in keywords:
                            if keyword.lower() in name.lower():
                                print(f"Found {obj.type.name}: {name} (PathID: {obj.path_id})")
                except:
                    continue
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    game_data = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data"
    resources_path = os.path.join(game_data, "resources.assets")
    scan_file(resources_path, ["Charred", "Wolf", "Player"])
