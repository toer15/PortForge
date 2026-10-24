# Frame generation check

A Python port of PortForge FSR 3's frame generation shaders (`engine/src/src/nx/gpu/vk/shaders/fg.comp`: luma pyramid,
motion search, median, propagation, interpolation, in the order `fg.c` runs them) and synthetic scenes whose in-between
frames are known exactly. It needs only Python 3 and numpy, and no GPU.

- `python evaluate.py` scores Engine 1.33 at phases 0.25, 0.5 and 0.75 on three scenes against blending and repeating a
  frame. `python evaluate.py --1.32` scores Engine 1.32. `name=value` arguments try other settings (`prop=1`,
  `coarse_stride=1`, `soft=0.05`).
- `python fielderr.py` measures the motion vectors against the scenes' true motion.
- `python oracle.py` interpolates with the true motion: the ceiling of the interpolation pass on its own.

The numbers are for these scenes, not for a game. A change to `fg.comp` should be made here too, and checked on real
frames with `fg_dump` and `snx-test-vulkan -H --fg-eval`.
