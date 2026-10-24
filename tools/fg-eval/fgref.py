# Numpy port of PortForge's fg.comp (LUMA, SEARCH, MEDIAN, INTERP) and fg.c's pass order, for offline evaluation.
import numpy as np

LEVELS = 5


def bilinear(img, x, y):
    """Sample img (H,W,C) at continuous texel coords x,y (pixel centres at k+0.5), clamp to edge."""
    H, W = img.shape[:2]
    fx = x - 0.5
    fy = y - 0.5
    x0 = np.floor(fx).astype(np.int64)
    y0 = np.floor(fy).astype(np.int64)
    ax = (fx - x0)[..., None]
    ay = (fy - y0)[..., None]
    x1 = np.clip(x0 + 1, 0, W - 1)
    y1 = np.clip(y0 + 1, 0, H - 1)
    x0 = np.clip(x0, 0, W - 1)
    y0 = np.clip(y0, 0, H - 1)
    a = img[y0, x0]
    b = img[y0, x1]
    c = img[y1, x0]
    d = img[y1, x1]
    return (a * (1 - ax) + b * ax) * (1 - ay) + (c * (1 - ax) + d * ax) * ay


def sample_uv(img, u, v):
    H, W = img.shape[:2]
    return bilinear(img, u * W, v * H)


def fetch(img, x, y):
    H, W = img.shape[:2]
    return img[np.clip(y, 0, H - 1), np.clip(x, 0, W - 1)]


def half(v):
    return (v + 1) // 2 if v > 1 else 1


def luma(c):
    if c.shape[-1] == 1:
        return c[..., 0]
    return c[..., 0] * 0.299 + c[..., 1] * 0.587 + c[..., 2] * 0.114


def pass_luma(src, dw, dh):
    py, px = np.mgrid[0:dh, 0:dw]
    u = (px + 0.5) / dw
    v = (py + 0.5) / dh
    l = luma(sample_uv(src, u, v))
    return l[..., None]


def pyramid(img):
    out = []
    cur = img
    h, w = img.shape[:2]
    for _ in range(LEVELS):
        w, h = half(w), half(h)
        cur = pass_luma(cur, w, h)
        out.append(cur)
    return out


def pass_search(la, lb, coarser, radius, flags, P, stride=1):
    """la, lb: (H,W,1) luma. coarser: (h,w,2+) median vectors or None. Returns (H,W,3): vx, vy, cost."""
    H, W = la.shape[:2]
    A = la[..., 0]
    B = lb[..., 0]
    py, px = np.mgrid[0:H, 0:W]

    def sad(hx, hy):
        # hx, hy arrays (H,W) ints
        s = np.zeros((H, W))
        for y in range(-2, 3):
            for x in range(-2, 3):
                qx = px + x * stride
                qy = py + y * stride
                s += np.abs(fetch(A, qx - hx, qy - hy) - fetch(B, qx + hx, qy + hy))
        return s / 25.0

    def cost(hx, hy, predx, predy):
        return sad(hx, hy) + P['pred_w'] * (np.abs(hx - predx) + np.abs(hy - predy)) + P['len_w'] * (np.abs(hx) + np.abs(hy))

    zero = np.zeros((H, W), dtype=np.int64)
    bx, by = zero.copy(), zero.copy()
    predx = np.zeros((H, W))
    predy = np.zeros((H, W))
    bc = cost(bx, by, predx, predy)
    zero_sad = bc.copy()
    cut = zero_sad > 0.55
    if flags & 1:
        ch, cw = coarser.shape[:2]
        for i in range(13):
            if i < 9:
                ox, oy = (i % 3 - 1) * 2, (i // 3 - 1) * 2
            else:
                ox, oy = [(-6, 0), (6, 0), (0, -6), (0, 6)][i - 9]
            cx = np.clip((px + ox) // 2, 0, cw - 1)
            cy = np.clip((py + oy) // 2, 0, ch - 1)
            c = coarser[cy, cx, :2] * 2.0
            hx = np.round(c[..., 0]).astype(np.int64)
            hy = np.round(c[..., 1]).astype(np.int64)
            k = cost(hx, hy, c[..., 0], c[..., 1])
            better = k < bc
            bc = np.where(better, k, bc)
            bx = np.where(better, hx, bx)
            by = np.where(better, hy, by)
            predx = np.where(better, c[..., 0], predx)
            predy = np.where(better, c[..., 1], predy)
    cx0, cy0 = bx.copy(), by.copy()
    for y in range(-radius, radius + 1):
        for x in range(-radius, radius + 1):
            if x == 0 and y == 0:
                continue
            hx = cx0 + x
            hy = cy0 + y
            k = cost(hx, hy, predx, predy)
            better = k < bc
            bc = np.where(better, k, bc)
            bx = np.where(better, hx, bx)
            by = np.where(better, hy, by)
    vx = bx.astype(np.float64)
    vy = by.astype(np.float64)
    if flags & 2:
        xm = cost(bx - 1, by, predx, predy)
        xp = cost(bx + 1, by, predx, predy)
        ym = cost(bx, by - 1, predx, predy)
        yp = cost(bx, by + 1, predx, predy)
        dx = xm - 2 * bc + xp
        dy = ym - 2 * bc + yp
        with np.errstate(divide='ignore', invalid='ignore'):
            vx = np.where(dx > 1e-5, vx + np.clip(0.5 * (xm - xp) / np.where(dx > 1e-5, dx, 1), -0.5, 0.5), vx)
            vy = np.where(dy > 1e-5, vy + np.clip(0.5 * (ym - yp) / np.where(dy > 1e-5, dy, 1), -0.5, 0.5), vy)
    weak = (bc > 0.28) & (zero_sad < bc + 0.02)
    vx = np.where(weak, 0.0, vx)
    vy = np.where(weak, 0.0, vy)
    bc = np.where(weak, zero_sad, bc)
    vx = np.where(cut, 0.0, vx)
    vy = np.where(cut, 0.0, vy)
    bc = np.where(cut, zero_sad, bc)
    return np.stack([vx, vy, bc], -1)


def propagate(la, lb, m, P, stride):
    # each texel re-tests its own vector and those of texels 2 away, rounded, by the same SAD: a lookalike match in a
    # flat patch loses to the motion its surroundings agree on
    H, W = la.shape[:2]
    A = la[..., 0]; B = lb[..., 0]
    py, px = np.mgrid[0:H, 0:W]
    def sad(hx, hy):
        s = np.zeros((H, W))
        for y in range(-2, 3):
            for x in range(-2, 3):
                qx = px + x * stride; qy = py + y * stride
                s += np.abs(fetch(A, qx - hx, qy - hy) - fetch(B, qx + hx, qy + hy))
        return s / 25.0
    best = m.copy(); bc = np.full((H, W), 1e9)
    d = int(P.get('prop_dist', 2))
    for oy in (-d, 0, d):
        for ox in (-d, 0, d):
            c = fetch(m, px + ox, py + oy)
            hx = np.round(c[..., 0]).astype(np.int64); hy = np.round(c[..., 1]).astype(np.int64)
            k = sad(hx, hy) + (0 if ox == 0 and oy == 0 else P.get('prop_w', 0.002))
            # keep the subpixel part of the texel's own vector when it wins
            upd = k < bc
            bc = np.where(upd, k, bc)
            best = np.where(upd[..., None], c, best)
    return np.concatenate([best, bc[..., None]], -1)


def pass_median(v):
    H, W = v.shape[:2]
    py, px = np.mgrid[0:H, 0:W]
    cand = []
    for y in range(-1, 2):
        for x in range(-1, 2):
            cand.append(fetch(v[..., :2], px + x, py + y))
    cand = np.stack(cand, 0)  # 9,H,W,2
    d = np.zeros((9, H, W))
    for i in range(9):
        for j in range(9):
            d[i] += np.linalg.norm(cand[i] - cand[j], axis=-1)
    best = np.argmin(d, 0)
    out = np.take_along_axis(cand, best[None, ..., None].repeat(2, -1), 0)[0]
    return out


def motion(lumA, lumB, P):
    med = [None] * LEVELS
    coarser = None
    for l in range(LEVELS - 1, -1, -1):
        fine = l == 0
        radius = 1 if fine else (2 if coarser is not None else 4)
        flags = (1 if coarser is not None else 0) | (2 if fine else 0)
        stride = int(P.get('coarse_stride', 1)) if l > 0 else int(P.get('fine_stride', 1))
        v = pass_search(lumA[l], lumB[l], coarser, radius, flags, P, stride)
        m = pass_median(v)
        for _ in range(int(P.get('prop', 0))):
            m = pass_median(propagate(lumA[l], lumB[l], m, P, stride)[..., :2])
        med[l] = m
        coarser = m
    return med[0]


def patch_diff(A, B, ua, va, ub, vb, pxu, pxv):
    s = 0
    for o in [(0, 0), (-1.5, 0), (1.5, 0), (0, -1.5), (0, 1.5)]:
        d = np.abs(sample_uv(A, ua + o[0] * pxu, va + o[1] * pxv) - sample_uv(B, ub + o[0] * pxu, vb + o[1] * pxv))
        s = s + d.sum(-1)
    return s * 0.2


def interp(A, B, field, t, P):
    H, W = A.shape[:2]
    ch, cw = field.shape[:2]
    py, px = np.mgrid[0:H, 0:W]
    pxu, pxv = 1.0 / W, 1.0 / H
    u = (px + 0.5) * pxu
    v = (py + 0.5) * pxv
    fu, fv = 1.0 / cw, 1.0 / ch
    sx, sy = W / cw, H / ch
    ta, tb = 2 * t, 2 * (1 - t)
    sampler = P.get('vec_sampler', 'bilinear')

    def vec_at(uu, vv):
        if sampler == 'nearest':
            xi = np.floor(uu * cw).astype(np.int64)
            yi = np.floor(vv * ch).astype(np.int64)
            return fetch(field, xi, yi)
        return sample_uv(field, uu, vv)

    h0 = vec_at(u, v)
    mu = u - (2 * t - 1) * h0[..., 0] * sx * pxu
    mv = v - (2 * t - 1) * h0[..., 1] * sy * pxv
    e = np.full((H, W), 1e9)
    hs, ce = [], []
    step = P.get('cand_step', 2.0)
    for i in range(9):
        ox, oy = (i % 3 - 1) * step, (i // 3 - 1) * step
        h = vec_at(mu + ox * fu, mv + oy * fv)
        hx, hy = h[..., 0] * sx, h[..., 1] * sy
        c = patch_diff(A, B, u - ta * hx * pxu, v - ta * hy * pxv, u + tb * hx * pxu, v + tb * hy * pxv, pxu, pxv)
        c = c + (0 if i == 4 else 0.005)
        hs.append((hx, hy))
        ce.append(c)
        e = np.minimum(e, c)
    soft = np.zeros((H, W, 3))
    wsum = np.zeros((H, W))
    best_a = np.zeros((H, W, 3))
    best_b = np.zeros((H, W, 3))
    bestc = np.full((H, W), 1e9)
    for (hx, hy), c in zip(hs, ce):
        wi = np.exp(-(c - e) / P['soft'])
        sa = sample_uv(A, u - ta * hx * pxu, v - ta * hy * pxv)
        sb = sample_uv(B, u + tb * hx * pxu, v + tb * hy * pxv)
        soft += wi[..., None] * (sa * (1 - t) + sb * t)
        wsum += wi
        upd = c < bestc
        bestc = np.where(upd, c, bestc)
        best_a = np.where(upd[..., None], sa, best_a)
        best_b = np.where(upd[..., None], sb, best_b)
    a0 = A[py, px]
    b0 = B[py, px]
    e0 = patch_diff(A, B, u, v, u, v, pxu, pxv)
    static = (np.linalg.norm(a0 - b0, axis=-1) < 0.018) | ((e0 <= e + 0.002) & (e0 < 0.03))
    warped = soft / wsum[..., None]
    if P.get('bad_mode', 'near') == 'near':
        near = a0 if t < 0.5 else b0
    else:  # one-sided warp: the nearer frame's sample along the best vector
        near = best_a if t < 0.5 else best_b
    lo, hi = P['bad_lo'], P['bad_hi']
    k = np.clip((e - lo) / (hi - lo), 0, 1)
    k = k * k * (3 - 2 * k)
    out = warped * (1 - k[..., None]) + near * k[..., None]
    out = np.where(static[..., None], a0 * (1 - t) + b0 * t, out)
    return np.clip(out, 0, 1)


# Engine 1.32's settings, and Engine 1.33's: coarse levels match over a 9x9 window (every other texel) and each
# level's vectors are propagated twice (fg.comp FG_PROP)
ENGINE_132 = dict(pred_w=0.004, len_w=0.0008, soft=0.1, bad_lo=9.0, bad_hi=10.0)
ENGINE_133 = dict(ENGINE_132, coarse_stride=2, prop=2)
DEFAULT = ENGINE_133


def generate(A, B, phases, P=DEFAULT):
    la = pyramid(A)
    lb = pyramid(B)
    field = motion(la, lb, P)
    # fg.c rounds the phase to q16
    return [interp(A, B, field, int((t * 65536)) / 65536.0, P) for t in phases]
