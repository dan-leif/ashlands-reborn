import numpy as np, json, sys
S=sys.argv[1]; spec=json.load(open(sys.argv[2])); out=sys.argv[3]
z=np.load(S+'/morgen/morgen.npz'); J=json.load(open(S+'/morgen/morgen.json'))
B=J['bones']; par=J['parents']; idx={b:i for i,b in enumerate(B)}
M={b:np.linalg.inv(z['BP'][i]).astype(np.float64) for i,b in enumerate(B)}
pm={b:M[b][:3,3]*1000 for b in B}; Rm={b:M[b][:3,:3] for b in B}
kids={b:[c for c in B if par.get(c)==b] for b in B}
PRIMARY={'Hip':'Spine','Spine':'Chest','Hand.l':'MiddleFinger0.l','Hand.r':'MiddleFinger0.r','Chest':None,'Root':None}
def rot_between(a,b):
    a=a/np.linalg.norm(a); b=b/np.linalg.norm(b); v=np.cross(a,b); c=np.dot(a,b)
    if np.linalg.norm(v)<1e-8: return np.eye(3)
    vx=np.array([[0,-v[2],v[1]],[v[2],0,-v[0]],[-v[1],v[0],0]]); return np.eye(3)+vx+vx@vx*(1/(1+c))
pf={}; Rb={}
order=[]; 
def visit(b):
    order.append(b); [visit(c) for c in kids[b]]
visit('Root')
for b in order:
    p=par.get(b) if par.get(b) in idx else None
    if b in spec['joints']: pf[b]=np.array(spec['joints'][b],float)
    else:
        sc=spec.get('scale',{}).get(b, spec.get('default_child_scale',0.8))
        pf[b]=pf[p]+sc*(pm[b]-pm[p])
    # chain rotation for this bone: align Morgen primary-child dir to fitted (computed after child known) -> do 2nd pass
for b in order: Rb[b]=None
def child_of(b):
    c=PRIMARY.get(b, 'x')
    if c=='x': c=kids[b][0] if kids[b] else None
    return c
# second pass needs child fitted positions; recompute with rotations: iterate twice for derived children
for it in range(3):
    for b in order:
        c=child_of(b); p=par.get(b) if par.get(b) in idx else None
        if c and c in spec['joints'] and b in spec['joints']:
            Rb[b]=rot_between(pm[c]-pm[b], pf[c]-pf[b])
        else:
            Rb[b]=Rb[p] if p and Rb.get(p) is not None else np.eye(3)
    for b in order:
        p=par.get(b) if par.get(b) in idx else None
        if b not in spec['joints']:
            sc=spec.get('scale',{}).get(b, spec.get('default_child_scale',0.8))
            pf[b]=pf[p]+sc*(Rb[p]@(pm[b]-pm[p]))
Mf=np.zeros((len(B),4,4))
for b in B:
    Mf[idx[b]]=np.eye(4); Mf[idx[b]][:3,:3]=Rb[b]@Rm[b]; Mf[idx[b]][:3,3]=pf[b]/1000
BPf=np.array([np.linalg.inv(m) for m in Mf])
# local positions for runtime puppet (in fitted parent frame)
lp={}
for b in B:
    p=par.get(b)
    if p in idx: lp[b]=(Mf[idx[p]][:3,:3].T@(Mf[idx[b]][:3,3]-Mf[idx[p]][:3,3])).tolist()
np.savez(out+'/fit.npz', BP=BPf, M=Mf)
json.dump({'bones':B,'joints_mm':{b:pf[b].tolist() for b in B},'localpos':lp}, open(out+'/fit.json','w'), indent=1)
for b in ['Hip','Chest','UpperArm.l','LowerArm.l','Hand.l','MiddleFinger2.l','UpperLeg.l','LowerLeg.l','Foot.l','Toes.l','Jaw']: print(b, np.round(pf[b],1))
