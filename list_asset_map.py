import UnityPy
import os

def list_bundle_map(bundle_path):
    print(f"--- AssetBundle Map: {bundle_path} ---")
    try:
        env = UnityPy.load(bundle_path)
        for obj in env.objects:
            if obj.type.name == "AssetBundle":
                data = obj.read()
                # container is a dict mapping name -> asset info
                for name, asset_info in data.container.items():
                    print(f"  {name} -> {asset_info.asset.path_id}")
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    bundle1 = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710"
    bundle2 = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\6f765361"
    list_bundle_map(bundle1)
    list_bundle_map(bundle2)
