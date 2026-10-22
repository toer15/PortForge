# PortForge 0.29 / Engine 1.12 validation

## Problem

Engine 1.11's pixel smoothing barely changed the picture. It mixed each pixel with its four direct neighbours by at most about 8 percent, and it ran after FSR, where a jagged step is already several pixels wide and RCAS has just sharpened it.

## Change

- `smooth.comp` is now edge antialiasing in the manner of FXAA's quality path. It detects a local edge and its direction, walks along it to both ends (up to 12 steps, about 25 pixels), and resamples across it by where the pixel sits on the step. A subpixel blend handles one-pixel specks. Flat areas below the contrast threshold are copied unchanged. Thresholds are lower than FXAA's defaults, because converted games often upscale their own image and leave softer steps.
- It runs on the game's image at its own resolution, before FSR. FSR then reads the smoothed image, and RCAS sharpens at 2.0 stops instead of 0.5, so it does not put the steps back.
- Smoothing works on values as the game encoded them (sRGB), as FXAA expects. A single decode happens, either in RCAS or in the smoothing pass when FSR is not used.
- If FSR cannot run (equal size, unsupported format, no scratch memory), smoothing still applies alone. If smoothing cannot run, the old path is used.

## Evidence

- GPU test `vulkan_headless`, `check_pixel_smoothing`: a 1-in-4 staircase edge, UNORM and sRGB, alone and before FSR at 2x. The largest height change of the edge between neighbouring columns fell from 1.00 to 0.45 rows (alone) and from 0.66 (FSR only) to 0.25 (smoothing + FSR). 0.25 is the true slope, so the steps are gone. Areas two rows away from the edge are unchanged within 2/255, off and invalid crops keep the original, and there is no host wait.
- Isolated Mario copy, TV mode, FSR 1 from 1600x900 to 2433x1369 in a maximized window, with the screen captured as presented. Average edge gradient fell 25% on the tower brim, 22% on the roller coaster and 10% on the HUD text with smoothing on. The comparison image is `outbox/PortForge-0.29-pixel-smoothing-comparison.png`.

## Not shown

Detail the game did not render cannot be restored, and blocky distant geometry stays blocky. Text softens a little. No FPS change is claimed.
