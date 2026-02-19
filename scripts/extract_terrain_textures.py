#!/usr/bin/env python3
"""
Extract Valheim terrain textures (including ash/gray) from SoftRef asset bundles.
Requires: pip install UnityPy

Run from project root: python scripts/extract_terrain_textures.py
Output: extracted_textures/
"""
from pathlib import Path
import sys

try:
    import UnityPy
except ImportError:
    print("UnityPy not installed. Run: pip install UnityPy")
    sys.exit(1)

VALHEIM_BUNDLES = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles")
OUTPUT_DIR = Path(__file__).resolve().parent.parent / "extracted_textures"

# Bundle c4210710 contains terrain_d.png, terraintile_*, terrain_d_array
TERRAIN_BUNDLE = "c4210710"

def extract_terrain_textures():
    bundle_path = VALHEIM_BUNDLES / TERRAIN_BUNDLE
    if not bundle_path.exists():
        print(f"Bundle not found: {bundle_path}")
        return False

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    print(f"Loading {bundle_path}...")
    try:
        env = UnityPy.load(str(bundle_path))
    except Exception as e:
        print(f"UnityPy failed to load bundle: {e}")
        print("Valheim's SoftRef format may be custom. Try AssetRipper or Asset Studio instead.")
        return False

    count = 0
    for obj in env.objects:
        if obj.type.name not in ("Texture2D", "Texture2DArray", "Sprite"):
            continue

        try:
            data = obj.parse_as_object()
        except Exception:
            continue

        name = getattr(data, "m_Name", None) or str(obj.path_id)
        # Filter for terrain-related
        if "terrain" not in name.lower() and "terraintile" not in name.lower():
            continue

        out_path = OUTPUT_DIR / f"{name}.png"
        try:
            if obj.type.name == "Texture2DArray":
                slices = []
                for i, img in enumerate(data.images):
                    slice_path = OUTPUT_DIR / f"{name}_slice_{i}.png"
                    img.save(str(slice_path))
                    print(f"  Saved {slice_path.name}")
                    slices.append((i, slice_path.name))
                    count += 1
                if slices:
                    manifest_path = OUTPUT_DIR / f"{name}_slices.txt"
                    with open(manifest_path, "w") as f:
                        f.write(f"{name}: {len(slices)} slices\n")
                        for idx, filename in slices:
                            f.write(f"  {filename} = array index {idx}\n")
                    print(f"  Wrote {manifest_path.name}")
            elif hasattr(data, "image") and data.image is not None:
                data.image.save(str(out_path))
                print(f"  Saved {out_path.name}")
                count += 1
        except Exception as e:
            print(f"  Skipped {name}: {e}")

    if count == 0:
        print("No terrain textures extracted. The bundle format may be incompatible.")
        return False

    print(f"\nExtracted {count} texture(s) to {OUTPUT_DIR}")
    return True

if __name__ == "__main__":
    extract_terrain_textures()
