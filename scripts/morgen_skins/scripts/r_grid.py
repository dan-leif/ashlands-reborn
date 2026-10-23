import sys, os, bpy, numpy as np, mathutils, math; sys.path.insert(0, os.path.dirname(__file__)); from rlib import *
glb, out, H = sys.argv[-3], sys.argv[-2], float(sys.argv[-1])
reset(); before=set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=glb)
ob=[o for o in bpy.data.objects if o not in before and o.type=='MESH'][0]
# Blender import gives Z-up, glTF +Z(front) -> Blender -Y. Morgen space: front -Y, z up -> same orientation.
bpy.context.view_layer.objects.active=ob; bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
vs=np.array([v.co[:] for v in ob.data.vertices]); zmin=vs[:,2].min(); h=vs[:,2].max()-zmin; s=H/h
for v in ob.data.vertices: v.co=((v.co.x-vs[:,0].mean())*s,(v.co.y-vs[:,1].mean())*s,(v.co.z-zmin)*s)
ob.data.update(); assign(ob, mat('c', base_override=(0.7,0.68,0.66)))
sc=bpy.context.scene
# grid lines every 5 units
for zz in range(0,int(H)+6,5):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0,0,zz)); c=bpy.context.object; c.scale=(80,80,0.08); assign(c, mat('g%d'%zz, base_override=(0.1,0.1,0.8) if zz%10==0 else (0.9,0.2,0.2)))
    c.display_type='WIRE'
for xx in range(-40,41,5):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(xx,0,H/2)); c=bpy.context.object; c.scale=(0.08,0.08,H+10); assign(c, mat('x%d'%xx, base_override=(0.1,0.6,0.1) if xx%10==0 else (0.8,0.8,0.1)))
cd=bpy.data.cameras.new('c'); cd.type='ORTHO'; cd.ortho_scale=H*1.5; cam=bpy.data.objects.new('cam',cd); sc.collection.objects.link(cam); sc.camera=cam
sun=bpy.data.lights.new('s','SUN'); sun.energy=3; so=bpy.data.objects.new('s',sun); sc.collection.objects.link(so); so.rotation_euler=(0.8,0.2,0.3)
sc.render.resolution_x=sc.render.resolution_y=800
for nm,loc,rot in (('front',(0,-200,H/2),(math.pi/2,0,0)),('side',(200,0,H/2),(math.pi/2,0,math.pi/2))):
    cam.location=loc; cam.rotation_euler=rot; sc.render.filepath=os.path.join(out,'grid_%s.png'%nm); bpy.ops.render.render(write_still=True)
print('SCALE', s)
