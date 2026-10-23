import sys, os; sys.path.insert(0, os.path.dirname(__file__)); from rlib import *
d, out = sys.argv[-2], sys.argv[-1]; P=lambda n: os.path.join(d,n)
reset(); objs=import_obj(P('Mammut_Mammut.obj'))
for name,body,ec,es in [
  ('mammoth_0_original', dict(), (0,0.545,1), 4),
  ('mammoth_1_meadow', dict(sat=1.0, val=0.75, tint=(0.62,0.5,0.32)), (0.5,1,0.35), 4),
  ('mammoth_2_mossback', dict(val=0.8, tint=(0.45,0.55,0.3)), (1,0.8,0.3), 4),
  ('mammoth_3_ember', dict(val=0.35, tint=(0.5,0.45,0.42)), (1,0.35,0.05), 8),
]:
    m=mat('m'+name, base=P('arcticmammoth_texture.png'), emit=P('arcticmammoth_emissive_texturetest.png'), emit_col=ec, emit_str=es, **body)
    for o in objs: assign(o,m)
    frame_and_render(objs, os.path.join(out, name+'.png'), yaw_deg=40, pitch_deg=10, dist_mul=1.2)
