import sys
import UnityPy
import os

def list_assets(file_path):
    env = UnityPy.load(file_path)
    for obj in env.objects:
        if obj.type.name in ["Mesh", "GameObject", "MonoBehaviour"]:
            try:
                data = obj.read()
                print(f"Type: {obj.type.name}, Name: {getattr(data, 'name', 'N/A')}, Path: {obj.path_id}")
            except:
                pass

if __name__ == "__main__":
    dll_path = r"C:\Users\danjo\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\southsil-SouthsilArmor\ItemManagerModTemplate.dll"
    list_assets(dll_path)
