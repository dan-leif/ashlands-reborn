import UnityPy
import os

def debug_asset_bundle(bundle_path):
    print(f"--- Debugging AssetBundle Object: {bundle_path} ---")
    try:
        env = UnityPy.load(bundle_path)
        for obj in env.objects:
            if obj.type.name == "AssetBundle":
                data = obj.read()
                print(f"Attributes: {dir(data)}")
                # Common UnityPy attribute is container (lowercase) or m_Container
                if hasattr(data, "container"):
                    print("Has container")
                    for k, v in data.container.items():
                        print(f"  {k} -> {v}")
                if hasattr(data, "m_Container"):
                    print("Has m_Container")
                    for k, v in data.m_Container.items():
                        print(f"  {k} -> {v}")
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    bundle1 = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710"
    debug_asset_bundle(bundle1)
