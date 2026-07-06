import UnityPy
import os

def debug_m_container(bundle_path):
    print(f"--- Inspecting m_Container in: {bundle_path} ---")
    try:
        env = UnityPy.load(bundle_path)
        for obj in env.objects:
            if obj.type.name == "AssetBundle":
                data = obj.read()
                container = getattr(data, "m_Container", [])
                print(f"Container size: {len(container)}")
                if len(container) > 0:
                    first_item = container[0]
                    print(f"First item type: {type(first_item)}")
                    # Try to see what attributes the item has
                    try:
                        # Some versions of UnityPy have structured items in the list
                        # They might be (str, dict) or a class
                        if isinstance(first_item, tuple):
                            print(f"First item (tuple): {first_item}")
                        else:
                            print(f"First item attributes: {dir(first_item)}")
                            # Often they have 'first' (string path) and 'second' (asset info)
                            if hasattr(first_item, 'first'):
                                print(f"Sample: {first_item.first} -> {getattr(first_item.second, 'asset', 'N/A')}")
                    except Exception as inner_e:
                        print(f"Inner error: {inner_e}")
                
                # Let's just list the first 10 paths if possible
                for i, item in enumerate(container[:20]):
                    try:
                        path_str = item.first if hasattr(item, 'first') else str(item)
                        print(f"  {i}: {path_str}")
                    except:
                        pass
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    bundle1 = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710"
    bundle2 = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\6f765361"
    debug_m_container(bundle1)
    debug_m_container(bundle2)
