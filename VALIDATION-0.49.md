# PortForge 0.49 / Engine 1.31

## Problem

With PortForge FSR 3 On and Limit 120, Esc **Display FPS** stayed near **60** on 30 FPS titles (BOTW swap interval 2). Engine 1.30 only inserted one midpoint per guest frame (pure 2×), so 30 → 60 was mechanically correct for 2× FG even while mid+real presents were succeeding. Players expected Display FPS to rise toward the chosen Limit.

## Fix (Engine 1.31)

1. **Limit-aware multi-mid FG** under speed lock: guest pace unchanged (`present_period` still the VI interval). Classic FG remains a **2× floor**. A Limit above that floor raises display toward `min(Limit, 4× game)` via `present_fg_slots` (1…4 presents per shown guest frame).
   - 30 FPS title + FSR 3 + Limit 120 → **4 slots** (3 generated + 1 real) → ~**120** Display FPS (was stuck at 60).
   - 60 FPS title + FSR 3 + Limit 60 → **2 slots** → ~**120** (classic 2× floor unchanged).
   - 60 FPS title + FSR 3 + Limit 120 → **2 slots** → ~**120**.
   - 60 FPS title + FSR 3 + Limit 240 → **4 slots** → ~**240**.
   - Unlimited / Game speed still defaults to classic **2×**.
2. Optical-flow INTERP takes a **phase** (Q16); `vk_fg_interpolate_mids` builds evenly spaced generated frames; the present queue shows the first mid immediately and schedules the rest + real across the guest period.
3. Esc: `Engine 1.31 | Display FPS: N (4x game 30) | Limit: 120` — Limit no longer shows `(~60)`. Display FPS tracks real present rate.
4. Play / Doctor still install the app Engine into games (`EnsureForPlay`).

## Verification

1. `cc -O2 -o /tmp/fg_cadence_check tests/fg_cadence_check.c && /tmp/fg_cadence_check` → PASS.
2. Windows: `build.ps1 -Test` (includes FgCadenceTests) and rebuild Engine 1.31; Doctor/Play on a game; Esc must show **Engine 1.31**.
3. BOTW-class 30 FPS title: FSR 3 On, Limit 120 → Display FPS leaves 60 toward ~120; guest physics unchanged (no floor clipping). Log line `frame generation display submissions: … (≈120/s)`.
4. 60 FPS title: FSR 3 On, Limit 120 → Display FPS ~120; Limit 240 → toward ~240 when the GPU keeps up.
5. FSR 3 Off: Limit 120 → Display FPS ≈ guest pace; Limit below guest still drops pictures only.
