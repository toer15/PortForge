import sys, json, time
import numpy as np
import fgref, scenes

PHASES = [0.25, 0.5, 0.75]


def score(out, truth, moving):
    err = np.abs(out - truth).sum(-1)
    mse = ((out - truth) ** 2).mean()
    psnr = 10 * np.log10(1.0 / max(mse, 1e-12))
    wrong = (err[moving] > 60 / 255).mean() if moving.any() else 0.0
    return psnr, wrong


def run(P, label, scenes_sel=None, verbose=True):
    rows = []
    for name, kw in scenes.SCENES.items():
        if scenes_sel and name not in scenes_sel:
            continue
        A = scenes.render(0.0, **kw)
        B = scenes.render(1.0, **kw)
        outs = fgref.generate(A, B, PHASES, P)
        moving = np.abs(A - B).sum(-1) > 0.02
        for t, o in zip(PHASES, outs):
            truth = scenes.render(t, **kw)
            p, w = score(o, truth, moving)
            # baselines
            near = A if t < 0.5 else B
            pb, wb = score(A * (1 - t) + B * t, truth, moving)
            pn, wn = score(near, truth, moving)
            rows.append((name, t, p, w, pb, wb, pn, wn))
    if verbose:
        print(f'== {label}')
        for r in rows:
            print(f'  {r[0]:11s} t={r[1]:.2f}  fg {r[2]:6.2f} dB wrong {r[3]*100:5.1f}%   blend {r[4]:6.2f}/{r[5]*100:5.1f}%   repeat {r[6]:6.2f}/{r[7]*100:5.1f}%')
    mp = np.mean([r[2] for r in rows])
    mw = np.mean([r[3] for r in rows])
    print(f'  MEAN {label}: {mp:.2f} dB, {mw*100:.1f}% wrong moving pixels')
    return mp, mw


if __name__ == '__main__':
    args = sys.argv[1:]
    P = dict(fgref.ENGINE_132 if '--1.32' in args else fgref.ENGINE_133)
    args = [a for a in args if a != '--1.32']
    for a in args:
        k, v = a.split('=')
        try:
            P[k] = float(v)
        except ValueError:
            P[k] = v
    t0 = time.time()
    run(P, ' '.join(sys.argv[1:]) or 'Engine 1.33')
    print(f'  ({time.time()-t0:.0f}s)')
