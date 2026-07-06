import UnityPy
import os

def list_names(bundle_path):
    print(f"Listing names in {bundle_path}...")
    env = UnityPy.load(bundle_path)
    for obj in env.objects:
        try:
            data = obj.read()
            name = getattr(data, "name", "")
            if name:
                print(f"[{obj.type.name}] {name}")
        except:
            pass

if __name__ == "__main__":
    list_names(r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710")
    list_names(r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\6f765361")
