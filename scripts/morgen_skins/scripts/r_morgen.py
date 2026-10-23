import sys, os, bpy, json, numpy as np, mathutils; sys.path.insert(0, os.path.dirname(__file__)); from rlib import *
d, out = sys.argv[-2], sys.argv[-1]
z=np.load(os.path.join(d,'morgen.npz')); J=json.load(open(os.path.join(d,'morgen.json')))
reset()
V=z['V']; T=z['T']
# Unity (x,y,z) y-up -> Blender (x, z, y) mirrored-ok for viewing: (x, -z?) use (x, z, y) then flip faces
K=40.0
verts=[(K*float(v[0]),K*float(v[1]),K*float(v[2])) for v in V]
me=bpy.data.meshes.new('morgen'); me.from_pydata(verts,[],[tuple(t) for t in T.tolist()]); me.update()
ob=bpy.data.objects.new('morgen',me); bpy.context.scene.collection.objects.link(ob)
assign(ob, mat('m', base_override=(0.75,0.72,0.68)))
# joints
for i,bp in enumerate(z['BP']):
    w=np.linalg.inv(bp); p=w[:3,3]
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.03, location=(K*p[0],K*p[1],K*p[2])); s=bpy.context.object
    assign(s, mat('j%d'%i, base_override=(1,0.2,0.1), emit_col=(1,0.2,0.1), emit_str=3))
    s.show_in_front=True
for yaw,nm in ((0,'front'),(90,'side'),(35,'q')):
    frame_and_render([ob], os.path.join(out,'morgen_bind_%s.png'%nm), yaw_deg=yaw, pitch_deg=8, dist_mul=1.3, res=(500,450))
