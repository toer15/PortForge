# Synthetic scenes with known in-between frames: a textured world panning, a textured object moving against it
# (occlusion and disocclusion at its edges), a static HUD with hard edges and a semi-transparent HUD panel.
import numpy as np

rng = np.random.default_rng(7)


def make_texture(n, smooth):
    t = rng.random((n, n, 3))
    # blur by repeated box filter for some low-frequency structure, then add back detail
    k = t.copy()
    for _ in range(smooth):
        k = (k + np.roll(k, 1, 0) + np.roll(k, -1, 0) + np.roll(k, 1, 1) + np.roll(k, -1, 1)) / 5
    detail = rng.random((n, n, 1))
    out = 0.6 * (k - k.min()) / (k.max() - k.min() + 1e-9) + 0.25 * detail + 0.1
    # some hard edges: stripes and blocks like buildings/grass
    # hard edges at irregular blocks, like buildings or rocks: no repeating period for the search to alias on
    bw = 8 + (n // 64)
    blocks = rng.random((n // bw + 1, n // bw + 1)) < 0.18
    yy, xx = np.mgrid[0:n, 0:n]
    out[blocks[yy // bw, xx // bw]] *= 0.45
    return np.clip(out, 0, 1)


BG = make_texture(2048, 6)
FG = make_texture(256, 2)
FG[..., 0] = np.clip(FG[..., 0] * 1.4, 0, 1)


def tex(T, x, y):
    n = T.shape[0]
    x = np.mod(x, n)
    y = np.mod(y, n)
    x0 = np.floor(x).astype(int) % n
    y0 = np.floor(y).astype(int) % n
    ax = (x - np.floor(x))[..., None]
    ay = (y - np.floor(y))[..., None]
    x1 = (x0 + 1) % n
    y1 = (y0 + 1) % n
    return (T[y0, x0] * (1 - ax) + T[y0, x1] * ax) * (1 - ay) + (T[y1, x0] * (1 - ax) + T[y1, x1] * ax) * ay


def render(t, W=960, H=540, bg_v=(6.0, 0.0), fg_v=(-12.0, 4.0), hud=True, scale=1.0):
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    xx += 0.5
    yy += 0.5
    img = tex(BG, (xx + bg_v[0] * t) * scale, (yy + bg_v[1] * t) * scale)
    cx, cy = W * 0.55 + fg_v[0] * t, H * 0.5 + fg_v[1] * t
    r = H * 0.22
    d = np.hypot(xx - cx, yy - cy)
    inside = np.clip(r - d + 0.5, 0, 1)[..., None]  # antialiased edge
    fgc = tex(FG, (xx - cx) + 128, (yy - cy) + 128)
    img = img * (1 - inside) + fgc * inside
    if hud:
        # opaque HUD: hearts row and text-like strokes
        hudm = np.zeros((H, W))
        hudm[16:40, 16:200] = ((xx[16:40, 16:200] // 8) % 3 != 0)
        hudm[H - 50:H - 26, W - 180:W - 20] = ((xx[H - 50:H - 26, W - 180:W - 20] // 3 + yy[H - 50:H - 26, W - 180:W - 20] // 4) % 2)
        img = np.where(hudm[..., None] > 0, np.array([0.95, 0.95, 0.9]), img)
        # semi-transparent panel: the world shows through it
        p = (slice(H - 110, H - 20), slice(20, 300))
        img[p] = img[p] * 0.45 + np.array([0.05, 0.05, 0.1]) * 0.55
    return np.clip(img, 0, 1)


# motion per real frame at 960x540 (half of 1080p): a walk with the camera turning, a fast camera turn, a slow pan
SCENES = {
    'pan+object': dict(bg_v=(18.0, 0.0), fg_v=(-30.0, 10.0)),
    'fast': dict(bg_v=(40.0, -8.0), fg_v=(-60.0, 20.0)),
    'slow': dict(bg_v=(5.0, 2.0), fg_v=(-10.0, 5.0)),
}


def masks(t, W=960, H=540, **kw):
    """moving region: pixels whose true colour changes between the two real frames"""
    a = render(0.0, W, H, **kw)
    b = render(1.0, W, H, **kw)
    g = render(t, W, H, **kw)
    return a, b, g
