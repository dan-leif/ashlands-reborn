#!/usr/bin/env python3
"""
Extract and disassemble Valheim Custom/Heightmap shader from asset bundle.
- Decompresses Unity's LZ4 blob
- Extracts DXBC shader blobs
- Disassembles to HLSL assembly (.asm) via DXDecompiler (dotnet)

Decompile to HLSL fails (DXDecompiler NullRef on Unity shaders); assembly
shows texture2darray sampling, vertex color usage, and slice indices.

Requires: pip install UnityPy lz4

Run from project root: python scripts/extract_and_decompile_shader.py
Output: extracted_shaders/*.dxbc, extracted_shaders/*.asm
"""
from pathlib import Path
import struct
import subprocess
import sys

try:
    import UnityPy
except ImportError:
    print("UnityPy not installed. Run: pip install UnityPy")
    sys.exit(1)

try:
    import lz4.block
except ImportError:
    print("lz4 not installed. Run: pip install lz4")
    sys.exit(1)

VALHEIM_BUNDLES = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles")
OUTPUT_DIR = Path(__file__).resolve().parent.parent / "extracted_shaders"
TERRAIN_BUNDLE = "c4210710"
HEIGHTMAP_PATH_ID = -7788665910763320123

# DXBC header: 4 byte magic, 16 byte hash, 2+2 version, 4 byte ContainerSizeInBytes
DXBC_MAGIC = b"DXBC"
DXBC_SIZE_OFFSET = 24


def find_dxbc_blobs(data: bytes) -> list[tuple[int, int]]:
    """Find (start, size) for each DXBC blob in decompressed data."""
    blobs = []
    pos = 0
    while True:
        idx = data.find(DXBC_MAGIC, pos)
        if idx < 0:
            break
        if idx + DXBC_SIZE_OFFSET + 4 > len(data):
            break
        size = struct.unpack_from("<I", data, idx + DXBC_SIZE_OFFSET)[0]
        if size < 32 or size > 10 * 1024 * 1024:
            pos = idx + 1
            continue
        if idx + size > len(data):
            pos = idx + 1
            continue
        blobs.append((idx, size))
        pos = idx + size
    return blobs


def extract_shader_blobs() -> list[Path]:
    """Load Heightmap shader, decompress blob, extract DXBC files. Returns list of .dxbc paths."""
    bundle_path = VALHEIM_BUNDLES / TERRAIN_BUNDLE
    if not bundle_path.exists():
        print(f"Bundle not found: {bundle_path}")
        return []

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    print(f"Loading {bundle_path}...")
    env = UnityPy.load(str(bundle_path))

    shader_obj = None
    for obj in env.objects:
        if obj.path_id == HEIGHTMAP_PATH_ID and obj.type.name == "Shader":
            shader_obj = obj.read()
            break

    if not shader_obj:
        print("Heightmap shader not found in bundle")
        return []

    blob = bytes(shader_obj.compressedBlob)
    cl = shader_obj.compressedLengths
    dl = shader_obj.decompressedLengths

    if not cl or not dl:
        print("Shader has no compressed blob data")
        return []

    # Decompress each chunk
    offset = 0
    all_dxbc = []
    for (comp_len,), (decomp_len,) in zip(cl, dl):
        comp_len = comp_len[0] if isinstance(comp_len, list) else comp_len
        decomp_len = decomp_len[0] if isinstance(decomp_len, list) else decomp_len
        chunk = blob[offset : offset + comp_len]
        offset += comp_len
        try:
            decompressed = lz4.block.decompress(chunk, uncompressed_size=decomp_len)
        except Exception as e:
            print(f"Decompression failed: {e}")
            continue
        for start, size in find_dxbc_blobs(decompressed):
            all_dxbc.append(decompressed[start : start + size])

    if not all_dxbc:
        print("No DXBC blobs found in decompressed shader data")
        return []

    dxbc_paths = []
    for i, dxbc_data in enumerate(all_dxbc):
        out_path = OUTPUT_DIR / f"Heightmap_{i:03d}.dxbc"
        out_path.write_bytes(dxbc_data)
        dxbc_paths.append(out_path)
        print(f"  Saved {out_path.name} ({len(dxbc_data)} bytes)")

    print(f"\nExtracted {len(dxbc_paths)} DXBC blob(s) to {OUTPUT_DIR}")
    return dxbc_paths


def decompile_with_dxdecompiler(dxbc_paths: list[Path]) -> bool:
    """Build and run DXDecompiler on each .dxbc file."""
    tools_dir = Path(__file__).resolve().parent.parent / "tools"
    dxdec_repo = tools_dir / "DXDecompiler"
    # Check common output paths (Release/Debug, with or without TFM subdir)
    candidates = [
        dxdec_repo / "src" / "DXDecompilerCmd" / "bin" / "Release" / "DXDecompilerCmd.exe",
        dxdec_repo / "src" / "DXDecompilerCmd" / "bin" / "Debug" / "DXDecompilerCmd.exe",
    ]
    for tf in ("net9.0", "net8.0", "net6.0"):
        candidates.append(dxdec_repo / "src" / "DXDecompilerCmd" / "bin" / "Release" / tf / "DXDecompilerCmd.exe")
        candidates.append(dxdec_repo / "src" / "DXDecompilerCmd" / "bin" / "Debug" / tf / "DXDecompilerCmd.exe")

    dxdec_exe = next((c for c in candidates if c.exists()), None)

    if dxdec_exe:
        print(f"\nDisassembling with {dxdec_exe.name}...")
        ok, fail = 0, 0
        for dxbc in dxbc_paths:
            asm_path = dxbc.with_suffix(".asm")
            try:
                subprocess.run(
                    [str(dxdec_exe), "-a", str(dxbc), "-O", str(asm_path)],
                    check=True,
                    capture_output=True,
                    text=True,
                )
                print(f"  {asm_path.name}")
                ok += 1
            except subprocess.CalledProcessError as e:
                print(f"  {dxbc.name}: failed ({e})")
                fail += 1
        if fail:
            print(f"  ({fail} failed)")
        return ok > 0

    # Need to clone and build
    if not (dxdec_repo / "src" / "DXDecompiler.sln").exists():
        print("\nCloning DXDecompiler...")
        tools_dir.mkdir(parents=True, exist_ok=True)
        try:
            subprocess.run(
                ["git", "clone", "--depth", "1", "https://github.com/spacehamster/DXDecompiler.git", str(dxdec_repo)],
                check=True,
                capture_output=True,
            )
        except subprocess.CalledProcessError as e:
            print(f"git clone failed: {e}")
            return False

    csproj = dxdec_repo / "src" / "DXDecompilerCmd" / "DXDecompilerCmd.csproj"
    if not csproj.exists():
        print(f"Project not found: {csproj}")
        return False

    print("\nBuilding DXDecompiler...")
    try:
        subprocess.run(
            ["dotnet", "build", str(csproj), "-c", "Release"],
            check=True,
            capture_output=True,
        )
    except subprocess.CalledProcessError as e:
        print(f"dotnet build failed: {e}")
        return False

    dxdec_exe = next((c for c in candidates if c.exists()), None)
    if not dxdec_exe:
        print("DXDecompilerCmd.exe not found after build")
        return False

    print(f"\nDisassembling with {dxdec_exe.name}...")
    ok, fail = 0, 0
    for dxbc in dxbc_paths:
        asm_path = dxbc.with_suffix(".asm")
        try:
            subprocess.run(
                [str(dxdec_exe), "-a", str(dxbc), "-O", str(asm_path)],
                check=True,
                capture_output=True,
                text=True,
            )
            print(f"  {asm_path.name}")
            ok += 1
        except subprocess.CalledProcessError:
            fail += 1
    if fail:
        print(f"  ({fail} failed)")
    return ok > 0


def main():
    dxbc_paths = extract_shader_blobs()
    if not dxbc_paths:
        sys.exit(1)

    decompile_with_dxdecompiler(dxbc_paths)
    print("\nDone. Assembly (.asm) files in extracted_shaders/")


if __name__ == "__main__":
    main()
