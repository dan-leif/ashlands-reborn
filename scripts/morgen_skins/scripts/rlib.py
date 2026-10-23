import bpy, math, os, mathutils
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc=bpy.context.scene
    for eng in ('BLENDER_EEVEE_NEXT','BLENDER_EEVEE'):
        try: sc.render.engine=eng; break
        except Exception: pass
    sc.render.resolution_x, sc.render.resolution_y = 900, 700
    sc.render.film_transparent=False
    w=bpy.data.worlds.new('w'); sc.world=w; w.use_nodes=True
    bg=w.node_tree.nodes['Background']; bg.inputs[0].default_value=(0.62,0.70,0.72,1); bg.inputs[1].default_value=0.9
    try: sc.view_settings.view_transform='AgX'
    except Exception: pass
    return sc
def load_img(p):
    return bpy.data.images.load(p, check_existing=True) if p and os.path.exists(p) else None
def mat(name, base=None, tint=(1,1,1), hue=0.5, sat=1.0, val=1.0, emit=None, emit_col=(0,0,0), emit_str=0.0, alpha=None, rough=0.75, normal=None, base_override=None, overlay=None):
    m=bpy.data.materials.new(name); m.use_nodes=True; nt=m.node_tree; N=nt.nodes; L=nt.links
    p=N['Principled BSDF']; p.inputs['Roughness'].default_value=rough
    col=None
    if base_override is not None:
        p.inputs['Base Color'].default_value=(*base_override,1)
    elif base:
        t=N.new('ShaderNodeTexImage'); t.image=load_img(base); hs=N.new('ShaderNodeHueSaturation')
        hs.inputs['Hue'].default_value=hue; hs.inputs['Saturation'].default_value=sat; hs.inputs['Value'].default_value=val
        L.new(t.outputs['Color'], hs.inputs['Color'])
        mx=N.new('ShaderNodeMixRGB'); mx.blend_type='MULTIPLY'; mx.inputs[0].default_value=1.0; mx.inputs[2].default_value=(*tint,1)
        L.new(hs.outputs['Color'], mx.inputs[1]); col=mx.outputs['Color']
        if overlay: col=overlay(N,L,col)
        L.new(col, p.inputs['Base Color'])
        if alpha=='base':
            L.new(t.outputs['Alpha'], p.inputs['Alpha'])
    if emit:
        e=N.new('ShaderNodeTexImage'); e.image=load_img(emit)
        mx2=N.new('ShaderNodeMixRGB'); mx2.blend_type='MULTIPLY'; mx2.inputs[0].default_value=1; mx2.inputs[2].default_value=(*emit_col,1)
        bw=N.new('ShaderNodeRGBToBW'); L.new(e.outputs['Color'], bw.inputs[0]); mr=N.new('ShaderNodeMath'); mr.operation='MULTIPLY'; mr.inputs[1].default_value=3.0; L.new(bw.outputs[0], mr.inputs[0]); L.new(mr.outputs[0], mx2.inputs[1]); L.new(mx2.outputs['Color'], p.inputs['Emission Color']); p.inputs['Emission Strength'].default_value=emit_str
    elif emit_str>0:
        p.inputs['Emission Color'].default_value=(*emit_col,1); p.inputs['Emission Strength'].default_value=emit_str
    return m
def assign(obj, m):
    obj.data.materials.clear(); obj.data.materials.append(m)
def frame_and_render(objs, out, yaw_deg=35, pitch_deg=12, dist_mul=1.5, res=None, ground=True):
    sc=bpy.context.scene
    if res: sc.render.resolution_x, sc.render.resolution_y = res
    mn=mathutils.Vector((1e9,)*3); mx=mathutils.Vector((-1e9,)*3)
    for o in objs:
        for c in o.bound_box:
            w=o.matrix_world@mathutils.Vector(c); mn=mathutils.Vector(map(min,mn,w)); mx=mathutils.Vector(map(max,mx,w))
    ctr=(mn+mx)/2; size=(mx-mn).length
    if ground and 'AR_ground' not in bpy.data.objects:
        bpy.ops.mesh.primitive_plane_add(size=size*8, location=(ctr.x,ctr.y,mn.z)); g=bpy.context.object; g.name='AR_ground'
        gm=mat('ground', base_override=(0.33,0.42,0.25)); assign(g,gm)
    cam=bpy.data.objects.get('AR_cam')
    if not cam:
        cd=bpy.data.cameras.new('c'); cd.lens=50; cam=bpy.data.objects.new('AR_cam',cd); sc.collection.objects.link(cam); sc.camera=cam
        sun=bpy.data.lights.new('sun','SUN'); sun.energy=3.2; s=bpy.data.objects.new('sun',sun); sc.collection.objects.link(s); s.rotation_euler=(math.radians(50),0,math.radians(30))
        fill=bpy.data.lights.new('fill','SUN'); fill.energy=0.8; f=bpy.data.objects.new('fill',fill); sc.collection.objects.link(f); f.rotation_euler=(math.radians(60),0,math.radians(200))
    y=math.radians(yaw_deg); p=math.radians(pitch_deg); d=size*dist_mul
    cam.location=ctr+mathutils.Vector((math.sin(y)*math.cos(p)*d, -math.cos(y)*math.cos(p)*d, math.sin(p)*d))
    cam.rotation_euler=(ctr-cam.location).to_track_quat('-Z','Y').to_euler()
    sc.render.filepath=out; bpy.ops.render.render(write_still=True)
def import_obj(p):
    before=set(bpy.data.objects); bpy.ops.wm.obj_import(filepath=p); return [o for o in bpy.data.objects if o not in before]
