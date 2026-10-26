"""Copy harness PNGs as 1280x720 JPGs: shrink.py <src dir> <dst dir>"""
import sys, os
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]; os.makedirs(dst, exist_ok=True)
for f in sorted(os.listdir(src)):
    if f.endswith('.png'):
        Image.open(os.path.join(src, f)).convert('RGB').resize((1280, 720)).save(os.path.join(dst, f[:-4] + '.jpg'), quality=85)
print('ok')
