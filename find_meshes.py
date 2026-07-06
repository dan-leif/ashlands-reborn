import UnityPy
import os

def find_meshes(file_path):
    try:
        env = UnityPy.load(file_path)
        for obj in env.objects:
            if obj.type.name == "Mesh":
                mesh = obj.read()
                print(f"Found Mesh: {mesh.name}")
    except Exception as e:
        print(f"Error loading {file_path}: {e}")

if __name__ == "__main__":
    dll_path = r"C:\Users\danjo\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\southsil-SouthsilArmor\ItemManagerModTemplate.dll"
    find_meshes(dll_path)
