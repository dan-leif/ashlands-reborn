import sys, os, bpy; sys.path.insert(0, os.path.dirname(__file__)); from rlib import *
glb, out = sys.argv[-2], sys.argv[-1]
YAW=float(os.environ.get('YAW','30'))
reset(); before=set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=glb)
objs=[o for o in bpy.data.objects if o not in before and o.type=='MESH' and o.name!='Icosphere']
for o in bpy.data.objects:
    if o.name=='Icosphere': o.hide_render=True
arm=[o for o in bpy.data.objects if o.type=='ARMATURE']
if arm and 'Left Punch' in bpy.data.actions:
    arm[0].animation_data_create(); arm[0].animation_data.action=bpy.data.actions['Left Punch']
    try: arm[0].animation_data.action_slot=bpy.data.actions['Left Punch'].slots[0]
    except Exception as e: print('slot',e)
    bpy.context.scene.frame_set(12)
import bpy as _b; _b.context.view_layer.update()
print('MESHES', [(o.name, len(o.data.vertices), [m.name for m in o.data.materials]) for o in objs])
# pose at frame of an animation if any
acts=[a.name for a in bpy.data.actions]; print('ACTIONS', acts)
def veins(base_mat_name, col, strength, scale, width, darken):
    m=bpy.data.materials[base_mat_name].copy(); m.name=base_mat_name+'_v'; nt=m.node_tree; N=nt.nodes; L=nt.links
    p=[n for n in N if n.type=='BSDF_PRINCIPLED'][0]
    tc=N.new('ShaderNodeTexCoord'); vor=N.new('ShaderNodeTexVoronoi'); vor.feature='DISTANCE_TO_EDGE'; vor.inputs['Scale'].default_value=scale
    noise=N.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=scale*0.6
    mixv=N.new('ShaderNodeVectorMath'); mixv.operation='ADD'; L.new(tc.outputs['Generated'], mixv.inputs[0]); 
    sc=N.new('ShaderNodeVectorMath'); sc.operation='SCALE'; sc.inputs['Scale'].default_value=0.08; L.new(noise.outputs['Color'], sc.inputs[0]); L.new(sc.outputs[0], mixv.inputs[1])
    L.new(mixv.outputs[0], vor.inputs['Vector'])
    ramp=N.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].position=0.0; ramp.color_ramp.elements[0].color=(1,1,1,1); ramp.color_ramp.elements[1].position=width; ramp.color_ramp.elements[1].color=(0,0,0,1)
    L.new(vor.outputs['Distance'], ramp.inputs['Fac'])
    em=N.new('ShaderNodeMixRGB'); em.blend_type='MULTIPLY'; em.inputs[0].default_value=1; em.inputs[2].default_value=(*col,1)
    mask=N.new('ShaderNodeTexNoise'); mask.inputs['Scale'].default_value=2.2; L.new(tc.outputs['Generated'], mask.inputs['Vector']); mr=N.new('ShaderNodeValToRGB'); mr.color_ramp.elements[0].position=0.42; mr.color_ramp.elements[1].position=0.55; L.new(mask.outputs['Fac'], mr.inputs['Fac']); mm=N.new('ShaderNodeMixRGB'); mm.blend_type='MULTIPLY'; mm.inputs[0].default_value=1; L.new(ramp.outputs['Color'], mm.inputs[1]); L.new(mr.outputs['Color'], mm.inputs[2]); L.new(mm.outputs['Color'], em.inputs[1]); L.new(em.outputs['Color'], p.inputs['Emission Color']); p.inputs['Emission Strength'].default_value=strength
    # darken base
    bc=p.inputs['Base Color']
    if bc.links:
        src=bc.links[0].from_socket; dk=N.new('ShaderNodeMixRGB'); dk.blend_type='MULTIPLY'; dk.inputs[0].default_value=1; dk.inputs[2].default_value=(*darken,1)
        L.new(src, dk.inputs[1]); L.new(dk.outputs['Color'], bc)
    else: bc.default_value=(*darken,1)
    return m
orig={o.name:[m for m in o.data.materials] for o in objs}
[frame_and_render(objs, os.path.join(out,'mo_%d.png'%y), yaw_deg=y, pitch_deg=8, dist_mul=1.6, res=(300,240)) for y in (0,90,180,270)]
import sys as _s; _s.exit(0) if 'orient' in _s.argv else None
frame_and_render(objs, os.path.join(out,'mutant_0_original.png'), yaw_deg=YAW, pitch_deg=8, dist_mul=1.6, res=(900,700))
for name,col,st,scale,width,dark in [('mutant_1_lava',(1,0.22,0.02),6,5.0,0.035,(0.45,0.4,0.4)),('mutant_2_flametal',(1,0.6,0.12),5,7.0,0.025,(0.4,0.38,0.42)),('mutant_3_moss_runes',(0.35,0.9,0.3),3,5.0,0.025,(0.6,0.7,0.5))]:
    for o in objs:
        o.data.materials.clear()
        for m in orig[o.name]: o.data.materials.append(veins(m.name,col,st,scale,width,dark))
    frame_and_render(objs, os.path.join(out,name+'.png'), yaw_deg=YAW, pitch_deg=8, dist_mul=1.6, res=(900,700))
