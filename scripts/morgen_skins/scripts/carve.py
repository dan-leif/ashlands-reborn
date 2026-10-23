import sys, os, mmap, struct
src, outdir = sys.argv[1], sys.argv[2]; os.makedirs(outdir, exist_ok=True)
with open(src,'rb') as f: mm = mmap.mmap(f.fileno(),0,access=mmap.ACCESS_READ)
pos=0; n=0
while True:
    i = mm.find(b'UnityFS\x00', pos)
    if i<0: break
    # header: sig\0, u32 ver, str\0, str\0, i64 size
    j=i+8+4; a=mm.find(b'\x00',j); b=mm.find(b'\x00',a+1)
    size=struct.unpack('>q', mm[b+1:b+9])[0]
    if 0<size<len(mm)-i:
        open(os.path.join(outdir,f'b{n}.unity3d'),'wb').write(mm[i:i+size]); print(n, i, size); n+=1; pos=i+size
    else: pos=i+8
