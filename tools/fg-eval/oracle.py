import numpy as np, fgref, scenes, evaluate, sys
P = dict(fgref.ENGINE_132 if '--1.32' in sys.argv else fgref.ENGINE_133)
sys.argv = [a for a in sys.argv if a != '--1.32']
for a in sys.argv[1:]:
    k, v = a.split('='); P[k] = float(v) if v.replace('.','').replace('-','').isdigit() else v
def oracle(kw, W=960, H=540):
    cw, ch = (W+1)//2, (H+1)//2
    yy, xx = np.mgrid[0:ch, 0:cw].astype(float)
    X, Y = (xx+0.5)*2, (yy+0.5)*2
    bg = kw['bg_v']; fg = kw['fg_v']
    f = np.zeros((ch, cw, 2))
    f[..., 0] = -bg[0]/2/2; f[..., 1] = -bg[1]/2/2
    cx, cy = W*0.55 + fg[0]*0.5, H*0.5 + fg[1]*0.5
    inside = np.hypot(X-cx, Y-cy) < H*0.22
    f[inside, 0] = fg[0]/2/2; f[inside, 1] = fg[1]/2/2
    return f
def main():
  tot=[]
  for name, kw in scenes.SCENES.items():
      A = scenes.render(0.0, **kw); B = scenes.render(1.0, **kw)
      moving = np.abs(A-B).sum(-1) > 0.02
      field = oracle(kw)
      for t in evaluate.PHASES:
          o = fgref.interp(A, B, field, t, P)
          p, w = evaluate.score(o, scenes.render(t, **kw), moving)
          tot.append(p)
          print(f'{name:11s} t={t:.2f} oracle-field {p:6.2f} dB wrong {w*100:5.1f}%')
  print('mean', np.mean(tot))

if __name__=="__main__": main()
