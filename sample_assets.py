import UnityPy
import os

def sample_assets(file_path, limit=200):
    print(f"Sampling first {limit} asset names from {file_path}...")
    try:
        env = UnityPy.load(file_path)
        count = 0
        for obj in env.objects:
            if obj.type.name in ["Mesh", "GameObject", "SkinnedMeshRenderer"]:
                try:
                    data = obj.read()
                    name = getattr(data, "m_Name", "") or ""  # UnityPy 1.24+ uses m_Name
                    if name:
                        print(f"[{obj.type.name}] {name}")
                        count += 1
                        if count >= limit:
                            break
                except:
                    continue
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    game_data = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data"
    resources_path = os.path.join(game_data, "resources.assets")
    sample_assets(resources_path)
