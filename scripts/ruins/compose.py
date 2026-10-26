"""Side-by-side vanilla|rebuilt composites per location+view from the ruins photo harness."""
import sys, os
from PIL import Image, ImageDraw
src = sys.argv[1]; out = sys.argv[2]; os.makedirs(out, exist_ok=True)
variants = sys.argv[3].split(',') if len(sys.argv) > 3 else ['vanilla', 'rebuilt']
locs = sorted({f.split('_' + variants[0] + '_')[0] for f in os.listdir(src) if '_' + variants[0] + '_' in f})
for loc in locs:
    for view in ['y30', 'y120', 'y210', 'y300', 'top']:
        ims = []
        for v in variants:
            p = os.path.join(src, f'{loc}_{v}_{view}.png')
            if os.path.exists(p): ims.append((v, Image.open(p).convert('RGB').resize((960, 540))))
        if not ims: continue
        c = Image.new('RGB', (960 * len(ims), 540))
        d = ImageDraw.Draw(c)
        for i, (v, im) in enumerate(ims):
            c.paste(im, (960 * i, 0)); d.text((960 * i + 10, 10), f'{loc} {v} {view}', fill=(255, 255, 0))
        c.save(os.path.join(out, f'{loc}_{view}.jpg'), quality=85)
print('ok', len(locs))
