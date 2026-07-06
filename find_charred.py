import UnityPy
import os

def find_charred(file_path):
    print(f"Scanning {file_path}...")
    try:
        env = UnityPy.load(file_path)
        for obj in env.objects:
            if obj.type.name in ["GameObject", "Mesh"]:
                data = obj.read()
                if "Charred" in data.name:
                    print(f"Found: {obj.type.name} - {data.name}")
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    game_assets = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\resources.assets"
    find_charred(game_assets)
