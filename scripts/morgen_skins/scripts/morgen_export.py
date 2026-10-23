import UnityPy, sys, json, numpy as np
from UnityPy.helpers.MeshHelper import MeshHandler
B=r"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\StreamingAssets\SoftRef\Bundles\c4210710"
out=sys.argv[1]
env=UnityPy.load(B)
def root_of(t):
    while t.m_Father and t.m_Father.path_id: t=t.m_Father.read()
    return t
res=[]
for o in env.objects:
    if o.type.name!='SkinnedMeshRenderer': continue
    d=o.read(); g=d.m_GameObject.read()
    t=g.m_Components[0]; t=(t.component if hasattr(t,'component') else t).read()
    if root_of(t).m_GameObject.read().m_Name!='Morgen': continue
    if not d.m_Mesh.path_id: continue
    m=d.m_Mesh.read()
    bones=[b.read().m_GameObject.read().m_Name for b in d.m_Bones]
    print('SMR', g.m_Name, m.m_Name,  'bones', len(bones), 'mats', [x.read().m_Name for x in d.m_Materials])
    h=MeshHandler(m); h.process(); res.append((g.m_Name, m, bones, d, h))
# take the main Morgen mesh (largest)
name,m,bones,d,h=max(res,key=lambda r:r[4].m_VertexCount)
V=np.array(h.m_Vertices,dtype=np.float32).reshape(-1,3)
N=np.array(h.m_Normals,dtype=np.float32).reshape(-1,3) if h.m_Normals else None
UV=np.array(h.m_UV0,dtype=np.float32).reshape(-1,2) if h.m_UV0 else None
tris=[]
for sub in h.get_triangles():
    tris+= [list(x) for x in sub]
W=np.array(h.m_BoneWeights,dtype=np.float32).reshape(-1,4); I=np.array(h.m_BoneIndices,dtype=np.int32).reshape(-1,4)
BP=np.array([[ [r.e00,r.e01,r.e02,r.e03],[r.e10,r.e11,r.e12,r.e13],[r.e20,r.e21,r.e22,r.e23],[r.e30,r.e31,r.e32,r.e33] ] for r in m.m_BindPose],dtype=np.float32)
np.savez(out+'/morgen.npz', V=V, N=N, UV=UV, T=np.array(tris,dtype=np.int32), W=W, I=I, BP=BP)
# bone hierarchy (parent names) for armature
parents={}
for b in d.m_Bones:
    tt=b.read(); nm=tt.m_GameObject.read().m_Name
    p=tt.m_Father.read().m_GameObject.read().m_Name if tt.m_Father and tt.m_Father.path_id else None
    parents[nm]=p
json.dump({'smr':name,'mesh':m.m_Name,'bones':bones,'parents':parents,'mats':[x.read().m_Name for x in d.m_Materials]}, open(out+'/morgen.json','w'), indent=1)
print('V',V.shape,'T',len(tris),'W',W.shape,'BP',BP.shape)
# save texture of first material
mat=d.m_Materials[0].read()
for k,v in mat.m_SavedProperties.m_TexEnvs:
    if v.m_Texture and v.m_Texture.path_id:
        tx=v.m_Texture.read(); tx.image.save(out+f'/{k}_{tx.m_Name}.png'); print('TEX',k,tx.m_Name,tx.m_Width)
