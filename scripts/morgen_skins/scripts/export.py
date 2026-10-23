import UnityPy, sys, os, json
bundle, root, out = sys.argv[1], sys.argv[2], sys.argv[3]; os.makedirs(out, exist_ok=True)
env = UnityPy.load(bundle)
def comps(go):
    for c in go.m_Components:
        p = c.component if hasattr(c,'component') else c
        yield p
done=False
for o in env.objects:
    if o.type.name!='GameObject': continue
    go=o.read()
    if go.m_Name!=root: continue
    # walk
    stack=[go]; info={'smr':[], 'ctrl':[], 'bones':0}
    while stack:
        g=stack.pop()
        for p in comps(g):
            tn=p.type.name if hasattr(p,'type') else ''
            try: c=p.read()
            except Exception: continue
            ot=c.object_reader.type.name
            if ot=='Transform':
                for ch in c.m_Children: stack.append(ch.read().m_GameObject.read())
            elif ot=='Animator':
                try:
                    ac=c.m_Controller.read(); info['ctrl'].append([ac.m_Name, sorted({x.read().m_Name for x in ac.m_AnimationClips})])
                except Exception as e: info['ctrl'].append(str(e))
            elif ot in ('SkinnedMeshRenderer','MeshRenderer'):
                try:
                    if ot=='SkinnedMeshRenderer': m=c.m_Mesh.read(); info['bones']=max(info['bones'],len(c.m_Bones))
                    else:
                        m=None
                        for p2 in comps(g):
                            try:
                                c2=p2.read()
                                if c2.object_reader.type.name=='MeshFilter': m=c2.m_Mesh.read()
                            except Exception: pass
                    if m is None: continue
                    fn=f"{g.m_Name}_{m.m_Name}".replace('/','_').replace(' ','_')+'.obj'
                    open(os.path.join(out,fn),'w').write(m.export())
                    mats=[]
                    for mp in c.m_Materials:
                        try:
                            mat=mp.read(); tex={}
                            for k,v in mat.m_SavedProperties.m_TexEnvs:
                                if v.m_Texture and v.m_Texture.path_id:
                                    t=v.m_Texture.read(); tp=os.path.join(out,t.m_Name+'.png')
                                    if not os.path.exists(tp): t.image.save(tp)
                                    tex[k]=t.m_Name
                            cols={k:[round(x,3) for x in (v.r,v.g,v.b,v.a)] for k,v in mat.m_SavedProperties.m_Colors}
                            sh=mat.m_Shader.read().m_Name if mat.m_Shader and mat.m_Shader.path_id else '?'
                            mats.append({'name':mat.m_Name,'shader':sh,'tex':tex,'colors':{k:cols[k] for k in cols if k in ('_Color','_EmissionColor')}})
                        except Exception as e: mats.append(str(e))
                    info['smr'].append({'go':g.m_Name,'obj':fn,'verts':len(m.m_Vertices)//3 if m.m_Vertices else None,'mats':mats})
                except Exception as e: info['smr'].append(str(e))
    
    if not info["smr"]: continue
    json.dump(info, open(os.path.join(out,"info.json"),"w"), indent=1); print(info["ctrl"], info["bones"], [s["obj"] for s in info["smr"]]); done=True; break
print('found' if done else 'NOT FOUND')
