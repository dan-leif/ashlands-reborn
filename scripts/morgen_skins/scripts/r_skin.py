import sys, os, bpy, bmesh, json, math, numpy as np, mathutils
sys.path.insert(0, os.path.dirname(__file__)); from rlib import *
glb, fitdir, out, H = sys.argv[-4], sys.argv[-3], sys.argv[-2], float(sys.argv[-1])
F = json.load(open(os.path.join(fitdir, 'fit.json'))); B = F['bones']; P = {b: mathutils.Vector(F['joints_mm'][b]) for b in B}
MOR = json.load(open(os.path.join(os.path.dirname(fitdir), 'morgen', 'morgen.json'))); par = MOR['parents']
VARS = json.load(open(os.path.join(out, 'variants.json')))
reset(); sc = bpy.context.scene
before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=glb)
ob = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
vs = np.array([v.co[:] for v in ob.data.vertices]); zmin = vs[:, 2].min(); s = H / (vs[:, 2].max() - zmin); cx, cy = vs[:, 0].mean(), vs[:, 1].mean()
for v in ob.data.vertices: v.co = ((v.co.x - cx) * s, (v.co.y - cy) * s, (v.co.z - zmin) * s)
ob.data.update(); ob.parent = None
bm = bmesh.new(); bm.from_mesh(ob.data); bmesh.ops.triangulate(bm, faces=bm.faces[:]); bm.to_mesh(ob.data); bm.free()

# armature from fitted joints
ad = bpy.data.armatures.new('rig'); arm = bpy.data.objects.new('rig', ad); sc.collection.objects.link(arm)
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
kids = {b: [c for c in B if par.get(c) == b] for b in B}
PRIM = {'Hip': 'Spine', 'Spine': 'Chest', 'Chest': 'Jaw', 'Hand.l': 'MiddleFinger0.l', 'Hand.r': 'MiddleFinger0.r',
        'UpperArm.l': 'LowerArm.l', 'UpperArm.r': 'LowerArm.r'}
eb = {}
for b in B:
    e = ad.edit_bones.new(b); e.head = P[b]
    c = PRIM.get(b) or (kids[b][0] if kids[b] else None)
    if c and (P[c] - P[b]).length > 0.3: e.tail = P[c]
    else:
        p = par.get(b)
        d = (P[b] - P[p]).normalized() if p in P and (P[b] - P[p]).length > 1e-4 else mathutils.Vector((0, 0, 1))
        e.tail = P[b] + d * 1.5
    eb[b] = e
for b in B:
    p = par.get(b)
    if p in eb: eb[b].parent = eb[p]
def nodef(b): return b in ('Root', 'Jaw', 'SkullAttach.r') or b.startswith('Mouth')
for b in B: eb[b].use_deform = not nodef(b)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
print('WEIGHTED', sum(1 for v in ob.data.vertices if len(v.groups) > 0), '/', len(ob.data.vertices))

# UV unwrap
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = ob; ob.select_set(True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01); bpy.ops.object.mode_set(mode='OBJECT')

def fur_mat(name, base, dark, back, face):
    m = bpy.data.materials.new(name); m.use_nodes = True; N = m.node_tree.nodes; L = m.node_tree.links
    for n in list(N): N.remove(n)
    o = N.new('ShaderNodeOutputMaterial'); em = N.new('ShaderNodeEmission'); L.new(em.outputs[0], o.inputs['Surface'])
    tc = N.new('ShaderNodeTexCoord'); sep = N.new('ShaderNodeSeparateXYZ'); L.new(tc.outputs['Object'], sep.inputs[0])
    mp = N.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (1.2, 1.2, 0.25); L.new(tc.outputs['Object'], mp.inputs[0])
    nz = N.new('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 1.4; nz.inputs['Detail'].default_value = 12; L.new(mp.outputs[0], nz.inputs['Vector'])
    fur = N.new('ShaderNodeMixRGB'); fur.inputs[1].default_value = (*dark, 1); fur.inputs[2].default_value = (*base, 1); L.new(nz.outputs['Fac'], fur.inputs[0])
    def band(sock, lo, hi, soft):
        r = N.new('ShaderNodeMapRange'); r.inputs['From Min'].default_value = lo; r.inputs['From Max'].default_value = lo + soft; L.new(sock, r.inputs['Value'])
        r2 = N.new('ShaderNodeMapRange'); r2.inputs['From Min'].default_value = hi; r2.inputs['From Max'].default_value = hi - soft; L.new(sock, r2.inputs['Value'])
        mu = N.new('ShaderNodeMath'); mu.operation = 'MULTIPLY'; L.new(r.outputs[0], mu.inputs[0]); L.new(r2.outputs[0], mu.inputs[1]); return mu.outputs[0]
    bk = N.new('ShaderNodeMath'); bk.operation = 'MULTIPLY'
    L.new(band(sep.outputs['Y'], 1.0, 30, 3), bk.inputs[0]); L.new(band(sep.outputs['Z'], 20, 46, 5), bk.inputs[1])
    m1 = N.new('ShaderNodeMixRGB'); L.new(bk.outputs[0], m1.inputs[0]); L.new(fur.outputs[0], m1.inputs[1]); m1.inputs[2].default_value = (*back, 1)
    ax = N.new('ShaderNodeMath'); ax.operation = 'ABSOLUTE'; L.new(sep.outputs['X'], ax.inputs[0])
    fm = N.new('ShaderNodeMath'); fm.operation = 'MULTIPLY'; L.new(band(sep.outputs['Y'], -30, -6.5, 2.5), fm.inputs[0]); L.new(band(sep.outputs['Z'], 40, 58, 3), fm.inputs[1])
    fm2 = N.new('ShaderNodeMath'); fm2.operation = 'MULTIPLY'; L.new(fm.outputs[0], fm2.inputs[0]); L.new(band(ax.outputs[0], -1, 8, 3), fm2.inputs[1])
    m2 = N.new('ShaderNodeMixRGB'); L.new(fm2.outputs[0], m2.inputs[0]); L.new(m1.outputs[0], m2.inputs[1]); m2.inputs[2].default_value = (*face, 1)
    hf = N.new('ShaderNodeMapRange'); hf.inputs['From Min'].default_value = 11; hf.inputs['From Max'].default_value = 5; L.new(sep.outputs['Z'], hf.inputs['Value'])
    hf2 = N.new('ShaderNodeMath'); hf2.operation = 'MULTIPLY'; hf2.inputs[1].default_value = 0.6; L.new(hf.outputs[0], hf2.inputs[0])
    m3 = N.new('ShaderNodeMixRGB'); L.new(hf2.outputs[0], m3.inputs[0]); L.new(m2.outputs[0], m3.inputs[1]); m3.inputs[2].default_value = (*face, 1)
    L.new(m3.outputs[0], em.inputs['Color'])
    img = bpy.data.images.new(name + '_img', 1024, 1024); t = N.new('ShaderNodeTexImage'); t.image = img; N.active = t
    return m, img

sc.render.engine = 'CYCLES'; sc.cycles.samples = 4; sc.cycles.device = 'CPU'
for name, (base, dark, back, face) in VARS.items():
    m, img = fur_mat(name, base, dark, back, face); ob.data.materials.clear(); ob.data.materials.append(m)
    bpy.ops.object.bake(type='EMIT', margin=4)
    img.filepath_raw = os.path.join(out, name + '.png'); img.file_format = 'PNG'; img.save(); print('BAKED', name)

# export (vertices split by UV seams), weights top-4 in Morgen bone order
me = ob.data; me.calc_loop_triangles()
uvl = me.uv_layers.active.data
gidx = {g.index: g.name for g in ob.vertex_groups}; bidx = {b: i for i, b in enumerate(B)}
key = {}; V = []; Nn = []; UV = []; W = []; I = []; T = []
for tri in me.loop_triangles:
    ids = []
    for li in tri.loops:
        vi = me.loops[li].vertex_index; uv = tuple(round(c, 5) for c in uvl[li].uv); k = (vi, uv)
        if k not in key:
            key[k] = len(V); v = me.vertices[vi]
            V.append(tuple(c / 1000 for c in v.co)); Nn.append(tuple(v.normal)); UV.append(uv)
            gs = sorted([(g.weight, bidx[gidx[g.group]]) for g in v.groups if gidx[g.group] in bidx and g.weight > 0.001], reverse=True)[:4]
            tot = sum(w for w, _ in gs) or 1; gs = [(w / tot, i) for w, i in gs] + [(0.0, 0)] * (4 - len(gs))
            if not any(w > 0 for w, _ in gs): gs = [(1.0, bidx['Chest']), (0, 0), (0, 0), (0, 0)]
            W.append([w for w, _ in gs]); I.append([i for _, i in gs])
        ids.append(key[k])
    T.append(ids)
V = np.array(V, np.float32); T = np.array(T, np.int32)
mz = np.load(os.path.join(os.path.dirname(fitdir), 'morgen', 'morgen.npz')); MV = mz['V']; MT = mz['T']
def signed(Vv, Tt):
    a, b, c = Vv[Tt[:, 0]], Vv[Tt[:, 1]], Vv[Tt[:, 2]]; ctr = Vv.mean(0)
    return float(np.sum(np.einsum('ij,ij->i', np.cross(b - a, c - a), (a + b + c) / 3 - ctr)))
sm, sa = signed(MV, MT), signed(V, T); print('WINDING morgen', np.sign(sm), 'ape', np.sign(sa))
if np.sign(sm) != np.sign(sa): T = T[:, ::-1].copy()
np.savez(os.path.join(out, 'ape_mesh.npz'), V=V, N=np.array(Nn, np.float32), UV=np.array(UV, np.float32), T=T,
         W=np.array(W, np.float32), I=np.array(I, np.int32))
print('EXPORT verts', len(V), 'tris', len(T))

# preview renders: bind pose, then arms raised + leg kicked (deformation check)
first = list(VARS)[0]
m = mat('prev', base=os.path.join(out, first + '.png'), rough=0.9); ob.data.materials.clear(); ob.data.materials.append(m)
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: sc.render.engine = eng; break
    except Exception: pass
frame_and_render([ob], os.path.join(out, 'prev_bind.png'), yaw_deg=25, pitch_deg=8, dist_mul=1.4, res=(600, 600))
bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = arm; arm.select_set(True); bpy.ops.object.mode_set(mode='POSE')
M = mathutils.Matrix
for side, sgn in (('l', 1), ('r', -1)):
    pb = arm.pose.bones['UpperArm.' + side]; h = arm.matrix_world @ pb.head
    pb.matrix = (M.Translation(pb.head) @ M.Rotation(math.radians(70) * sgn, 4, 'Y') @ M.Translation(-pb.head)) @ pb.matrix
    bpy.context.view_layer.update()
pl = arm.pose.bones['UpperLeg.l']
pl.matrix = (M.Translation(pl.head) @ M.Rotation(math.radians(-40), 4, 'X') @ M.Translation(-pl.head)) @ pl.matrix
bpy.ops.object.mode_set(mode='OBJECT'); bpy.context.view_layer.update()
frame_and_render([ob], os.path.join(out, 'prev_posed.png'), yaw_deg=25, pitch_deg=8, dist_mul=1.4, res=(600, 600))
