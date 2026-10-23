import sys, os; sys.path.insert(0, os.path.dirname(__file__)); from rlib import *
d, out = sys.argv[-2], sys.argv[-1]; P=lambda n: os.path.join(d,n)
V = {
 'svalt_0_original': dict(body=dict(tint=(0.6,0.6,0.6)), rune=(1,0.02,0), rs=6, hair=(0.376,0.137,0.149), eye=(1,0.85,0), es=8),
 'svalt_1_moss_warden': dict(body=dict(hue=0.72, sat=1.3, val=1.15, tint=(0.75,0.85,0.6)), rune=(0.4,1,0.35), rs=8, hair=(0.22,0.38,0.12), eye=(0.6,1,0.5), es=10),
 'svalt_2_birch_spirit': dict(body=dict(sat=0.15, val=2.6, tint=(0.95,0.93,0.88)), rune=(1,0.75,0.2), rs=8, hair=(0.85,0.85,0.8), eye=(1,0.9,0.5), es=10),
 'svalt_3_autumn_stag': dict(body=dict(hue=0.47, sat=1.5, val=1.5, tint=(1.0,0.62,0.35)), rune=(1,0.55,0.1), rs=7, hair=(0.55,0.22,0.06), eye=(1,0.7,0.2), es=10),
 'svalt_4_frost_ghost': dict(body=dict(hue=0.15, sat=0.5, val=2.0, tint=(0.75,0.88,1.0)), rune=(0.35,0.75,1), rs=10, hair=(0.75,0.85,0.95), eye=(0.5,0.85,1), es=12),
}
reset(); objs={}
for f in os.listdir(d):
    if f.endswith('.obj'):
        for o in import_obj(P(f)): objs[f]=objs.get(f,[])+[o]
allobjs=[o for v in objs.values() for o in v]
for name,v in V.items():
    body=mat('body_'+name, base=P('T_vendigotexturing_Vendigo_BaseMap.png'), emit=P('BossSvalt_TW_Vendigo_Emission.png'), emit_col=v['rune'], emit_str=v['rs'], **v['body'])
    hair=mat('hair_'+name, base=P('T_Hair_Vendigo_Alpha.png'), tint=v['hair'], alpha='base', rough=0.9)
    try: hair.surface_render_method='DITHERED'
    except Exception: pass
    eye=mat('eye_'+name, base_override=(0.05,0.05,0.05), emit_col=v['eye'], emit_str=v['es'])
    for f,os_ in objs.items():
        m = hair if 'hair' in f else eye if 'eyes' in f else body
        for o in os_: assign(o,m)
    frame_and_render(allobjs, os.path.join(out, name+'.png'), yaw_deg=32, pitch_deg=10, dist_mul=1.25)
