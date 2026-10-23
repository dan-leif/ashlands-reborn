import numpy as np, json, struct, sys, colorsys
from PIL import Image, ImageFilter
S=sys.argv[1]; OUT=S+'/morgenskins'
z=np.load(S+'/morgen/morgen.npz'); J=json.load(open(S+'/morgen/morgen.json')); B=J['bones']; par=J['parents']; idx={b:i for i,b in enumerate(B)}
F=json.load(open(S+'/ape/fit.json')); FZ=np.load(S+'/ape/fit.npz'); A=np.load(S+'/ape/ape_mesh.npz')
Mm=[np.linalg.inv(m) for m in z['BP']]
rest=[];fitlp=[];has=[]
for i,b in enumerate(B):
    p=par.get(b)
    if p in idx:
        j=idx[p]; rest.append(Mm[j][:3,:3].T@(Mm[i][:3,3]-Mm[j][:3,3])); fitlp.append(FZ['M'][j][:3,:3].T@(FZ['M'][i][:3,3]-FZ['M'][j][:3,3])); has.append(1)
    else: rest.append(np.zeros(3)); fitlp.append(np.zeros(3)); has.append(0)
V,N,UV,T,W,I=A['V'],A['N'],A['UV'],A['T'],A['W'],A['I']
with open(OUT+'/ape.bin','wb') as f:
    f.write(b'ARMS'); f.write(struct.pack('<iiii',1,len(V),T.size,len(B)))
    for name in B:
        e=name.encode('utf8'); f.write(struct.pack('<i',len(e))); f.write(e)
    f.write(V.astype('<f4').tobytes()); f.write(N.astype('<f4').tobytes()); f.write(UV.astype('<f4').tobytes())
    f.write(T.astype('<i4').tobytes()); f.write(I.astype('<i4').tobytes()); f.write(W.astype('<f4').tobytes())
    f.write(FZ['BP'].astype('<f4').reshape(-1).tobytes())          # row-major 4x4 per bone
    f.write(np.array(fitlp,'<f4').tobytes()); f.write(np.array(rest,'<f4').tobytes()); f.write(np.array(has,'<i4').tobytes())
print('ape.bin', len(V), T.size, len(B))
for n in ['ape_silverback','ape_chestnut','ape_mossfur']:
    Image.open(S+'/ape/'+n+'.png').convert('RGB').resize((512,512), Image.LANCZOS).save(OUT+'/'+n+'.png')
# Svalt living-face textures
base=Image.open(S+'/monstrum/svalt/T_vendigotexturing_Vendigo_BaseMap.png').convert('RGBA'); a=np.asarray(base).astype(np.float32)/255
rgb=a[...,:3]; mx=rgb.max(-1); mn=rgb.min(-1); sat=np.where(mx>0,(mx-mn)/np.maximum(mx,1e-6),0)
bone=(mx>0.42)&(sat<0.5)                     # pale beige bone/skull/antler/tail
fur=rgb[~bone].mean(0)
rng=np.random.default_rng(3); nz=Image.fromarray((np.clip(rng.normal(0.5,0.25,rgb.shape[:2]),0,1)*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.6)); noise=((np.asarray(nz).astype(np.float32)/255-0.5)*0.35)[...,None]
def save(name, rgbx):
    out=np.clip(np.concatenate([rgbx,a[...,3:]],-1),0,1); Image.fromarray((out*255).astype(np.uint8),'RGBA').save(OUT+'/'+name+'.png')
bark=np.array([0.30,0.21,0.13])               # living wood / bark brown for the face, antlers, ribs
liv=rgb.copy(); liv[bone]=np.clip(bark*(0.75+0.5*(rgb[bone].mean(-1,keepdims=True)))+noise[bone],0,1)
save('svalt_original_living', liv)
save('svalt_original_raw', rgb)
def autumn(x):
    out=np.zeros_like(x)
    for i in range(x.shape[0]):
        for j in range(x.shape[1]):
            h,s,v=colorsys.rgb_to_hsv(*x[i,j]); h=(h-0.02)%1; s=min(1,s*1.15); v=min(1,v*1.3); out[i,j]=colorsys.hsv_to_rgb(h,s,v)
    return np.clip(out*np.array([1.0,0.78,0.55]),0,1)
save('svalt_autumn_living', autumn(liv))
save('svalt_autumn_raw', autumn(rgb))
print('bone pixels', int(bone.sum()), 'of', bone.size)
# preview strip
ims=[Image.open(OUT+'/'+n+'.png').convert('RGB').resize((200,200),Image.NEAREST) for n in ['svalt_original_raw','svalt_original_living','svalt_autumn_raw','svalt_autumn_living','ape_silverback','ape_chestnut','ape_mossfur']]
Sx=Image.new('RGB',(1400,200)); [Sx.paste(im,(i*200,0)) for i,im in enumerate(ims)]; Sx.save(S+'/renders/tex_strip.png')
