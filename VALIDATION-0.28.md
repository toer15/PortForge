# PortForge 0.28 / Engine 1.11 validation

## Change

A per-game **Pixel smoothing** switch (`pixel_smoothing`, default off). Engine 1.11 runs a small compute filter on the finished picture, after FSR 1 when FSR is active. It blends each pixel with its four neighbours only where local contrast is high, so flat areas keep their color. It is a spatial filter: it softens jagged edges but also text and fine detail, and it cannot restore detail or remove all aliasing. Guest render targets, timing and saves are untouched. Unsupported formats or a failed setup keep the original presentation.

## Evidence

- Real GPU test (`vulkan_headless`, new `check_pixel_smoothing`): off returns the original image; an invalid crop is refused; on lifts the dark side and lowers the light side of a hard edge, leaves flat areas within 2/255, round-trips sRGB, adds no host wait. Passed for UNORM and sRGB sources.
- Isolated Mario copy, FSR 1 on, smoothing off then on: the log shows `Pixel smoothing active` with the 2433x1369 filter dispatched on every FSR frame, and the world, character and UI render correctly. The two screenshots are different moments of the scene and show the guest image, so they are not a pixel-exact comparison.
- App checks cover saving, verifying and restoring the setting, and an older engine being given no unsupported key.

## Not shown

No FPS or 1% low change is claimed. Smoothing adds GPU work.
