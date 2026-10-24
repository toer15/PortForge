import numpy as np, fgref, scenes, sys
P = dict(fgref.ENGINE_132 if '--1.32' in sys.argv else fgref.ENGINE_133)
from oracle import oracle
sys.argv = [a for a in sys.argv if a != '--1.32']
for a in sys.argv[1:]:
    k, v = a.split('='); P[k] = float(v)
print(sys.argv[1:])
for name, kw in scenes.SCENES.items():
    A = scenes.render(0.0, **kw); B = scenes.render(1.0, **kw)
    la, lb = fgref.pyramid(A), fgref.pyramid(B)
    f = fgref.motion(la, lb, P)
    o = oracle(kw)
    err = np.linalg.norm(f - o, axis=-1)
    # exclude HUD-ish rows roughly
    print(f'{name:11s} field err: median {np.median(err):.2f}  >1 texel {np.mean(err>1)*100:.1f}%  >0.5 {np.mean(err>0.5)*100:.1f}%  true |h| bg {np.hypot(*kw["bg_v"])/4:.2f} fg {np.hypot(*kw["fg_v"])/4:.2f}')
