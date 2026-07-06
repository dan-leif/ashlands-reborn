import UnityPy
import os

def debug_bundle(bundle_path):
    print(f"--- Debugging Bundle: {bundle_path} ---")
    try:
        env = UnityPy.load(bundle_path)
        type_counts = {}
        for obj in env.objects:
            t = obj.type.name
            type_counts[t] = type_counts.get(t, 0) + 1
            
            # For a few objects, let's see what's inside
            if type_counts[t] < 5:
                try:
                    data = obj.read()
                    name = getattr(data, "name", "NoName")
                    print(f"  [{t}] {name} (PathID: {obj.path_id})")
                except:
                    print(f"  [{t}] <Could not read data> (PathID: {obj.path_id})")
        
        print(f"Type Counts: {type_counts}")
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    bundle1 = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710"
    bundle2 = r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\6f765361"
    debug_bundle(bundle1)
    debug_bundle(bundle2)
