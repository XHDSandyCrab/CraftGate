# -*- coding: utf-8 -*-
"""纯标准库生成加载 GIF（无第三方依赖）：螺旋滚入 -> 展开成波浪线 -> 浮现文字 -> 卷回滚出。
透明背景，循环。文字用 5x7 点阵绘制（英文 LOADING，保证无字体依赖）。"""
import math, struct, zlib, os

W, H = 160, 44
GREEN = (61, 220, 132)
CY = H // 2

# 5x7 点阵字母（LOADING 用到的）
FONT5x7 = {
 'L': ["10000","10000","10000","10000","10000","10000","11111"],
 'O': ["01110","10001","10001","10001","10001","10001","01110"],
 'A': ["01110","10001","10001","11111","10001","10001","10001"],
 'D': ["11110","10001","10001","10001","10001","10001","11110"],
 'I': ["11111","00100","00100","00100","00100","00100","11111"],
 'N': ["10001","11001","10101","10011","10001","10001","10001"],
 'G': ["01110","10001","10000","10111","10001","10001","01111"],
}

def ease(t):
    t = max(0.0, min(1.0, t)); return t*t*(3-2*t)
def lerp(a,b,t): return a+(b-a)*t

def line_points(coil, cx, phase):
    pts=[]; N=120; turns=3.2
    for i in range(N):
        u=i/(N-1)
        fx=lerp(18,W-18,u)
        fy=CY+5*math.sin(u*math.pi*6+phase)*math.sin(u*math.pi)
        th=u*turns*2*math.pi+phase
        rad=3+u*13
        cxp=cx+rad*math.cos(th)
        cyp=CY+rad*math.sin(th)
        pts.append((lerp(fx,cxp,coil), lerp(fy,cyp,coil)))
    return pts

def draw_text_px(px, text, ox, oy, scale, alpha):
    cw=6*scale; total=len(text)*cw
    x0=ox-total//2
    for idx,ch in enumerate(text):
        rows=FONT5x7.get(ch,["00000"]*7)
        for r,row in enumerate(rows):
            for col,bit in enumerate(row):
                if bit=='1':
                    for sy in range(scale):
                        for sx in range(scale):
                            X=x0+idx*cw+col*scale+sx
                            Y=oy+r*scale+sy
                            if 0<=X<W and 0<=Y<H:
                                blend_pixel(px,X,Y,GREEN,alpha)

def blend_pixel(px,x,y,color,alpha):
    x=int(x); y=int(y)
    i=(y*W+x)*4
    if px[i+3]==0:
        px[i],px[i+1],px[i+2],px[i+3]=color[0],color[1],color[2],alpha
    else:
        a=alpha/255.0
        px[i]=int(px[i]*(1-a)+color[0]*a)
        px[i+1]=int(px[i+1]*(1-a)+color[1]*a)
        px[i+2]=int(px[i+2]*(1-a)+color[2]*a)
        px[i+3]=max(px[i+3],alpha)

def set_line(px,pts,width=2):
    for (x1,y1),(x2,y2) in zip(pts,pts[1:]):
        steps=int(max(abs(x2-x1),abs(y2-y1)))+1
        for s in range(steps+1):
            t=s/steps
            x=int(round(lerp(x1,x2,t))); y=int(round(lerp(y1,y2,t)))
            for dy in range(width):
                for dx in range(width):
                    X=x+dx-width//2; Y=y+dy-width//2
                    if 0<=X<W and 0<=Y<H: blend_pixel(px,X,Y,GREEN,255)

def render_frame(coil,cx,text_alpha,phase):
    px=bytearray(W*H*4)
    pts=line_points(coil,cx,phase)
    set_line(px,pts,2)
    if pts:
        hx,hy=pts[-1]
        for dy in range(5):
            for dx in range(5):
                X=int(hx)+dx-2; Y=int(hy)+dy-2
                if 0<=X<W and 0<=Y<H: blend_pixel(px,X,Y,GREEN,120)
    if text_alpha>0:
        draw_text_px(px,"LOADING",W//2,CY-4,2,int(text_alpha))
    return bytes(px)

# ---- 极简 GIF89a 编码器（透明色表 + LZW）----
def lzw_encode(indices, min_code_size):
    clear=1<<min_code_size; end=clear+1
    code_size=min_code_size+1
    dict_={bytes([i]):i for i in range(clear)}
    next_code=end+1
    out=bytearray(); cur=0; cur_bits=0
    def emit(code,size):
        nonlocal cur,cur_bits
        cur|=code<<cur_bits; cur_bits+=size
        while cur_bits>=8:
            out.append(cur&0xff); cur>>=8; cur_bits-=8
    emit(clear,code_size)
    w=b''
    for k in indices:
        c=bytes([k]); wc=w+c
        if wc in dict_: w=wc
        else:
            emit(dict_[w],code_size)
            dict_[wc]=next_code; next_code+=1
            if next_code>(1<<code_size) and code_size<12: code_size+=1
            w=c
    if w: emit(dict_[w],code_size)
    emit(end,code_size)
    if cur_bits>0: out.append(cur&0xff)
    return bytes(out)

def gif_from_frames(rgba_frames, delays, path):
    # 全局色表：透明色0 + 绿色1 + 半透绿(近似)2；gif 只支持1bit透明，用透明索引0
    palette=[(0,0,0),(61,220,132),(40,150,90)] + [(0,0,0)]*253
    hdr=b'GIF89a'+struct.pack('<HHBBB',W,H,0xF0|1,0,0)  # GCT flag, size 2^(1+1)=4
    for c in palette[:4]: hdr+=bytes(c)
    # NETSCAPE 循环扩展
    hdr+=b'\x21\xFF\x0BNETSCAPE2.0\x03\x01\x00\x00\x00'
    out=bytearray(hdr)
    for rgba,delay in zip(rgba_frames,delays):
        # 转索引：alpha<40 ->0 透明；否则绿色(1)，半透用2
        idx=bytearray(W*H)
        for p in range(W*H):
            a=rgba[p*4+3]
            idx[p]=0 if a<40 else (1 if a>=200 else 2)
        gce=b'\x21\xF9\x04\x01'+struct.pack('<H',delay)+b'\x00\x00\x00'
        img=b'\x2C'+struct.pack('<HHHHB',0,0,W,H,0)
        min_cs=2
        data=lzw_encode(idx,min_cs)
        img+=bytes([min_cs])
        # 分块
        for i in range(0,len(data),255):
            chunk=data[i:i+255]; img+=bytes([len(chunk)])+chunk
        img+=b'\x00'
        out+=gce+img
    out+=b'\x3B'
    with open(path,'wb') as f:f.write(out)

frames=[]; delays=[]
def add(coil,cx,ta,ph,d=7): frames.append(render_frame(coil,cx,ta,ph)); delays.append(d)
for f in range(10):
    t=ease(f/9); add(1.0,lerp(-30,40,t),0,f*0.4)
for f in range(14):
    t=ease(f/13); add(lerp(1.0,0.0,t),lerp(40,W/2,t),0,f*0.3)
for f in range(10):
    t=ease(f/9); add(0.0,W/2,255*t,f*0.5)
for f in range(6): add(0.0,W/2,255,f*0.5)
for f in range(8):
    t=ease(f/7); add(lerp(0.0,1.0,t),lerp(W/2,W-40,t),255*(1-t),f*0.4)
for f in range(10):
    t=ease(f/9); add(1.0,lerp(W-40,W+30,t),0,f*0.4)

out=r"D:\project\CraftGate\Assets\loading.gif"
os.makedirs(os.path.dirname(out),exist_ok=True)
gif_from_frames(frames,delays,out)
print("saved",out,"frames",len(frames),"bytes",os.path.getsize(out))
