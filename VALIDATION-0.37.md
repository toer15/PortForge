# PortForge 0.37 / Engine 1.21

## PortForge FSR 3 (from scratch)

Researched AMD FSR 3 / AFMF (GPUOpen): temporal upscale needs game motion vectors + depth; frame generation uses optical flow (8x8 SAD, luma pyramid, median) plus disocclusion and UI handling. Switch titles do not expose MVs to the host, so this is **not** the AMD FidelityFX SDK.

PortForge FSR 3 implements its own optical-flow FG from those ideas:
- Luma pyramid + hierarchical block search + median filter
- Scene-cut zero-motion (do not invent vectors on hard cuts)
- Bidirectional consistency / disocclusion
- HUD/static lock and hole inpainting from the current frame
- FSR 1 on real frames before interpolation (no FSR on generated pictures)

## Menus
- **Settings → Upscaling → PortForge FSR 3** saves `portforge_fsr3=1` + FSR1 + FG for every game
- **Esc / Home → PortForge FSR 3 On/Off** for the session

## Limits
Still no true FSR 2/3 temporal upscaling without guest MVs. Resolution presets and 60→~120 display cadence remain as in 0.36.
