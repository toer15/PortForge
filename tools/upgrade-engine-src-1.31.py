#!/usr/bin/env python3
"""Upgrade a PortForge Engine 1.30 ScarletNX tree (engine/src) to Engine 1.31 limit-aware multi-mid FG.

Usage: python tools/upgrade-engine-src-1.31.py [path-to-engine-src]
Default path: engine/src next to this repo's engine/VERSION.

Expects a 1.30-applied tree (e.g. from PortForge-0.47 Windows Source/engine/src, or a prior local build).
Writes in place. Idempotent enough to re-run if VERSION is already 1.31 (re-applies key replacements).
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "engine" / "portforge-engine.patch"


def patch_plus_file(rel: str) -> str:
    """Reconstruct a new-file (+ only) body from the cumulative patch."""
    text = PATCH.read_text(encoding="utf-8", errors="replace")
    m = re.search(
        rf"diff --git a/{re.escape(rel)} b/{re.escape(rel)}\n.*?^(?=diff --git |\Z)",
        text,
        re.M | re.S,
    )
    if not m:
        raise SystemExit(f"patch missing file {rel}")
    body = []
    for line in m.group(0).splitlines():
        if line.startswith("+++") or line.startswith("---") or line.startswith("diff ") or line.startswith("index "):
            continue
        if line.startswith("@@"):
            continue
        if line.startswith("+"):
            body.append(line[1:])
        elif line.startswith("-"):
            continue
        elif line.startswith(" "):
            body.append(line[1:])
    return "\n".join(body) + ("\n" if body else "")


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8", errors="replace")
    if new.strip() and new in text and old not in text:
        print(f"OK already: {label}")
        return
    if old not in text:
        raise SystemExit(f"marker not found for {label} in {path}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"OK: {label}")


def main() -> int:
    src = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else ROOT / "engine" / "src"
    if not (src / "src" / "nx" / "gpu" / "vk" / "renderer.c").is_file():
        raise SystemExit(f"engine src tree not found at {src}")

    # --- version ---
    ver_h = src / "src" / "nx" / "runtime" / "pf_engine_version.h"
    vh = ver_h.read_text(encoding="utf-8")
    vh2 = re.sub(r'PF_ENGINE_VERSION\s+"[\d.]+"', 'PF_ENGINE_VERSION "1.31"', vh)
    ver_h.write_text(vh2, encoding="utf-8")
    eng = src.parent if (src.parent / "VERSION").is_file() else ROOT / "engine"
    if (eng / "VERSION").is_file():
        (eng / "VERSION").write_text("1.31\n", encoding="utf-8")
    print("OK: VERSION 1.31")

    # --- present.c: limit-aware slots ---
    present = src / "src" / "nx" / "runtime" / "present.c"
    old_expect = """/* Expected on-screen rate. Speed lock keeps a swap-interval game at its own pace, so a cap above that pace does not
   raise the guest rate. Without frame generation the display follows the frames present_show_due actually shows
   (the game's pace, or a lower cap). With frame generation each shown guest frame can contribute one generated
   picture, about twice that shown rate (a 60 FPS game → ~120, a 30 FPS game → ~60) — not twice a cap above the
   game. 0 when unlimited or the pace is not known yet. */
int present_expected_display_fps(int fps_limit, int frame_generation) {
    int side = present_game_side_fps(fps_limit);
    if (!frame_generation || side <= 0) return side;
    return side <= 500 ? side * 2 : 1000;
}"""
    new_expect = """/* PortForge FSR 3 FG: classic doubled presents only (1 mid + 1 real => 2x game).
   Multi-mid (slots>2) caused ghosting/lag; Limit no longer raises slot count. */
enum { PRESENT_FG_MAX_SLOTS = 2 }; /* 1 real + 1 generated => classic 2x display */
int present_fg_slots(int fps_limit) {
    int side = present_game_side_fps(fps_limit);
    (void)fps_limit;
    if (side <= 0) return 1;
    return PRESENT_FG_MAX_SLOTS;
}

/* Expected Display FPS for Esc. FG Off: numeric Limit. FG On: 2x game-side. */
int present_expected_display_fps(int fps_limit, int frame_generation) {
    int side = present_game_side_fps(fps_limit);
    if (!frame_generation) {
        if (fps_limit > 0 && fps_limit != SNX_FPS_GAME) return fps_limit;
        return side;
    }
    if (side <= 0) return 0;
    return side * present_fg_slots(fps_limit);
}"""
    pt = present.read_text(encoding="utf-8", errors="replace")
    # Re-apply 2x clamp even if an older 4-slot present_fg_slots is already present.
    if "PRESENT_FG_MAX_SLOTS = 2" in pt and "if (fps_limit > 0 && fps_limit != SNX_FPS_GAME) return fps_limit;" in pt:
        print("OK already: present_fg_slots classic 2x + Off=Limit")
    elif "int present_fg_slots(" in pt:
        import re as _re
        pt2 = _re.sub(
            r"/\* (?:Limit-aware FG|PortForge FSR 3 FG):.*?^int present_expected_display_fps\(int fps_limit, int frame_generation\) \{.*?\n\}",
            new_expect.rstrip(),
            pt,
            count=1,
            flags=_re.M | _re.S,
        )
        if pt2 == pt:
            # Fallback: force-replace the enum + function bodies if comment form drifted.
            pt2 = pt.replace("PRESENT_FG_MAX_SLOTS = 4", "PRESENT_FG_MAX_SLOTS = 2")
            if "target = side * 2" in pt2:
                old_slots = _re.search(
                    r"int present_fg_slots\(int fps_limit\) \{.*?\n\}",
                    pt2,
                    _re.M | _re.S,
                )
                if old_slots:
                    pt2 = pt2[: old_slots.start()] + """int present_fg_slots(int fps_limit) {
    int side = present_game_side_fps(fps_limit);
    (void)fps_limit;
    if (side <= 0) return 1;
    return PRESENT_FG_MAX_SLOTS;
}""" + pt2[old_slots.end() :]
            present.write_text(pt2, encoding="utf-8")
            print("OK: present_fg_slots reclamped to classic 2x")
        else:
            present.write_text(pt2, encoding="utf-8")
            print("OK: present_fg_slots classic 2x + Off=Limit")
    else:
        replace_once(present, old_expect, new_expect, "present.c slots")

    # --- present.c: FG Off = Limit pace (present_period / show_due / game_side) ---
    pt = present.read_text(encoding="utf-8", errors="replace")
    if "static int present_fg_on(void)" in pt and "lock && game && fg" in pt:
        print("OK already: present_period FG Off=Limit")
    else:
        old_period = """u64 present_period(int fps, u32 interval, int lock) {
    u64 game = interval ? 1000000000u / (u64)VI_REFRESH_HZ * interval : 0;
    u64 cap = fps > 0 ? 1000000000u / (u64)fps : 0;
    if (fps == SNX_FPS_GAME || (lock && game)) return game;
    return cap;
}"""
        new_period = """static int present_fg_on(void) {
    return __atomic_load_n(&g_opt_frame_generation, __ATOMIC_RELAXED) ||
           (__atomic_load_n(&g_settings.frame_generation, __ATOMIC_RELAXED) &&
            __atomic_load_n(&g_settings.upscaler, __ATOMIC_RELAXED));
}

u64 present_period(int fps, u32 interval, int lock) {
    u64 game = interval ? 1000000000u / (u64)VI_REFRESH_HZ * interval : 0;
    u64 cap = fps > 0 ? 1000000000u / (u64)fps : 0;
    int fg = present_fg_on();
    if (fps == SNX_FPS_GAME) return game;
    if (lock && game && fg) return game;
    if (!fps) return game ? game : 0;
    return cap;
}"""
        if old_period in pt:
            pt = pt.replace(old_period, new_period, 1)
            present.write_text(pt, encoding="utf-8")
            print("OK: present_period FG Off=Limit")
        else:
            print("WARN: present_period marker not found (may already be upgraded)")
        pt = present.read_text(encoding="utf-8", errors="replace")
        # show_due: only drop frames when FG On
        old_due = """    if (!__atomic_load_n(&g_opt_speed_lock, __ATOMIC_RELAXED) || fps <= 0 || !game ||
        (cap = 1000000000u / (u64)fps) <= game) {"""
        new_due = """    int fg = present_fg_on();
    if (!__atomic_load_n(&g_opt_speed_lock, __ATOMIC_RELAXED) || !fg || fps <= 0 || !game ||
        (cap = 1000000000u / (u64)fps) <= game) {"""
        if old_due in pt and "|| !fg ||" not in pt:
            pt = pt.replace(old_due, new_due, 1)
            present.write_text(pt, encoding="utf-8")
            print("OK: present_show_due FG-aware")
        pt = present.read_text(encoding="utf-8", errors="replace")
        old_side = """    if (lock && game_fps > 0 && fps_limit > game_fps) return game_fps;"""
        new_side = """    int fg = present_fg_on();
    if (fg && lock && game_fps > 0 && fps_limit > game_fps) return game_fps;"""
        if old_side in pt and "fg && lock && game_fps" not in pt:
            # insert fg decl if missing in function
            if "int present_game_side_fps" in pt:
                pt = pt.replace(old_side, new_side, 1)
                present.write_text(pt, encoding="utf-8")
                print("OK: present_game_side_fps FG-aware")

    # --- present_limit_changed: release Esc Display hold so Limit/FSR updates live ---
    pt = present.read_text(encoding="utf-8", errors="replace")
    if "overlay_release_display_fps_hold" in pt:
        print("OK already: present_limit_changed releases Esc Display hold")
    elif "void present_limit_changed(void) { g_show_next = 0; }" in pt:
        pt = pt.replace(
            "void present_limit_changed(void) { g_show_next = 0; }",
            """void present_limit_changed(void) {
    g_show_next = 0;
    overlay_release_display_fps_hold();
}""",
            1,
        )
        if '#include "nx/runtime/platform/overlay.h"' not in pt:
            pt = pt.replace(
                '#include "nx/runtime/platform/log.h"\n',
                '#include "nx/runtime/platform/log.h"\n#include "nx/runtime/platform/overlay.h"\n',
                1,
            )
        present.write_text(pt, encoding="utf-8")
        print("OK: present_limit_changed releases Esc Display hold")
    oh = src / "src" / "nx" / "runtime" / "platform" / "overlay.h"
    oc = src / "src" / "nx" / "runtime" / "platform" / "overlay.c"
    if oh.is_file() and "overlay_release_display_fps_hold" not in oh.read_text(encoding="utf-8", errors="replace"):
        oh.write_text(
            oh.read_text(encoding="utf-8", errors="replace").replace(
                "void overlay_note_display_fps(int fps);",
                "void overlay_note_display_fps(int fps);\nvoid overlay_release_display_fps_hold(void);",
                1,
            ),
            encoding="utf-8",
        )
        print("OK: overlay.h overlay_release_display_fps_hold")
    if oc.is_file() and "overlay_release_display_fps_hold" not in oc.read_text(encoding="utf-8", errors="replace"):
        oc_txt = oc.read_text(encoding="utf-8", errors="replace")
        oc.write_text(
            oc_txt.replace(
                "int overlay_display_fps(void) { return __atomic_load_n(&g_display_fps, __ATOMIC_RELAXED); }",
                """void overlay_release_display_fps_hold(void) {
    __atomic_store_n(&g_display_fps_hold, 0, __ATOMIC_RELAXED);
}
int overlay_display_fps(void) { return __atomic_load_n(&g_display_fps, __ATOMIC_RELAXED); }""",
                1,
            ),
            encoding="utf-8",
        )
        print("OK: overlay.c overlay_release_display_fps_hold")

    # --- options.c: reset pace when FSR toggled ---
    options = src / "src" / "nx" / "runtime" / "options.c"
    if options.is_file():
        ot = options.read_text(encoding="utf-8", errors="replace")
        if "options_fsr3_live" in ot and "present_limit_changed();" in ot.split("options_fsr3_live")[1][:400]:
            print("OK already: options_fsr3_live resets pace")
        elif "void options_fsr3_live(int on)" in ot:
            needle = """    session_put("frame_generation", on ? "1" : "0");
    if (on) session_put("upscaler", "1");
    pm_unlock(&g_poll_lock);
}"""
            repl = """    session_put("frame_generation", on ? "1" : "0");
    if (on) session_put("upscaler", "1");
    present_limit_changed();
    pm_unlock(&g_poll_lock);
}"""
            if needle in ot:
                options.write_text(ot.replace(needle, repl, 1), encoding="utf-8")
                print("OK: options_fsr3_live present_limit_changed")
            else:
                print("WARN: options_fsr3_live body marker not found")

    # --- main.h declare present_fg_slots ---
    main_h = src / "src" / "nx" / "runtime" / "main.h"
    mh = main_h.read_text(encoding="utf-8", errors="replace")
    if "present_fg_slots" not in mh:
        needle = "int present_expected_display_fps(int fps_limit, int frame_generation);"
        if needle not in mh:
            raise SystemExit("main.h missing present_expected_display_fps decl")
        mh = mh.replace(
            needle,
            "int present_fg_slots(int fps_limit);\n" + needle,
            1,
        )
        main_h.write_text(mh, encoding="utf-8")
        print("OK: main.h present_fg_slots")
    else:
        print("OK already: main.h present_fg_slots")

    # --- overlay Esc: Nx game, no ~ on Limit ---
    overlay = src / "src" / "nx" / "runtime" / "platform" / "overlay.c"
    ot = overlay.read_text(encoding="utf-8", errors="replace")
    old_esc = """        fps_limit_text(fps, limit_word, sizeof limit_word);
        expect = present_expected_display_fps(fps, fg);
        if (expect > 0 && ((fps > 0 && expect != fps) || (fps <= 0 && expect > 0) || fps == SNX_FPS_GAME))
            snprintf(limit_part, sizeof limit_part, "%s (~%d)", limit_word, expect);
        else
            snprintf(limit_part, sizeof limit_part, "%s", limit_word);
        if (live >= 0)
            snprintf(fps_line, sizeof fps_line, "Engine %s | Display FPS: %d | Limit: %s", PF_ENGINE_VERSION, live,
                     limit_part);
        else
            snprintf(fps_line, sizeof fps_line, "Engine %s | Display FPS: -- | Limit: %s", PF_ENGINE_VERSION,
                     limit_part);"""
    new_esc = """        fps_limit_text(fps, limit_word, sizeof limit_word);
        expect = present_expected_display_fps(fps, fg);
        side = present_game_side_fps(fps);
        slots = fg && side > 0 ? present_fg_slots(fps) : 1;
        (void)expect;
        if (live >= 0 && fg && side > 0 && slots > 1)
            snprintf(fps_line, sizeof fps_line, "Engine %s | Display FPS: %d (%dx game %d) | Limit: %s",
                     PF_ENGINE_VERSION, live, slots, side, limit_word);
        else if (live >= 0)
            snprintf(fps_line, sizeof fps_line, "Engine %s | Display FPS: %d | Limit: %s", PF_ENGINE_VERSION, live,
                     limit_word);
        else if (fg && side > 0 && slots > 1)
            snprintf(fps_line, sizeof fps_line, "Engine %s | Display FPS: -- (%dx game %d) | Limit: %s",
                     PF_ENGINE_VERSION, slots, side, limit_word);
        else
            snprintf(fps_line, sizeof fps_line, "Engine %s | Display FPS: -- | Limit: %s", PF_ENGINE_VERSION,
                     limit_word);"""
    # FPS limit row: show present_fg_slots as %dx (not hardcoded 2x) when FG is on.
    old_fps_row = 'snprintf(value, vsize, "< %s (2x game %d -> ~%d) >", word, side, expect);'
    new_fps_row = (
        "int slots = present_fg_slots(fps);\n"
        '                    snprintf(value, vsize, "< %s (%dx game %d -> ~%d) >", word, slots, side, expect);'
    )
    if old_fps_row in ot:
        ot = ot.replace(old_fps_row, new_fps_row, 1)
        overlay.write_text(ot, encoding="utf-8")
        print("OK: overlay FPS limit %dx slots")
        ot = overlay.read_text(encoding="utf-8", errors="replace")
    elif 'snprintf(value, vsize, "< %s (%dx game %d -> ~%d) >", word, slots, side, expect);' in ot:
        print("OK already: overlay FPS limit %dx slots")
    else:
        print("WARN: overlay FPS limit 2x/%dx row not found (check manually)")

    if "(%dx game %d)" in ot and "(~%d)" not in ot.split("PAGE_MAIN")[1][:800]:
        print("OK already: overlay Esc Nx game")
    else:
        # widen locals
        ot2 = ot
        if "int r, n = items(), live, fps, fg, expect;" in ot2 and "side, slots" not in ot2:
            ot2 = ot2.replace(
                "int r, n = items(), live, fps, fg, expect;",
                "int r, n = items(), live, fps, fg, expect, side, slots;",
                1,
            )
        if "char limit_word[32], limit_part[48];" in ot2:
            ot2 = ot2.replace("char limit_word[32], limit_part[48];", "char limit_word[32];", 1)
        if old_esc not in ot2:
            raise SystemExit("overlay Esc block marker not found (already upgraded or unexpected tree)")
        ot2 = ot2.replace(old_esc, new_esc, 1)
        overlay.write_text(ot2, encoding="utf-8")
        print("OK: overlay Esc")

    # --- vk.h FG_MAX_MIDS + interpolate_mids decl ---
    vk_h = src / "src" / "nx" / "gpu" / "vk" / "vk.h"
    vh = vk_h.read_text(encoding="utf-8", errors="replace")
    if "FG_MAX_MIDS" not in vh:
        # insert near interpolate_present decl
        needle = "Surf* vk_fg_interpolate_present(VkCommandBuffer cb, u32 dw, u32 dh, Surf** out_cur);"
        if needle not in vh:
            raise SystemExit("vk.h missing vk_fg_interpolate_present")
        vh = vh.replace(
            needle,
            "#ifndef FG_MAX_MIDS\n"
            "#define FG_MAX_MIDS 3 /* generated frames per guest; 1 real + 3 mids => 4x display */\n"
            "#endif\n"
            "int vk_fg_interpolate_mids(VkCommandBuffer cb, u32 dw, u32 dh, Surf** out_cur, Surf** out_mids, int nmids);\n"
            + needle,
            1,
        )
        vk_h.write_text(vh, encoding="utf-8")
        print("OK: vk.h FG_MAX_MIDS")
    else:
        print("OK already: vk.h FG_MAX_MIDS")

    # --- fg.c: replace whole file with 1.31 from patch (complete new-file body) ---
    fg_path = src / "src" / "nx" / "gpu" / "vk" / "fg.c"
    fg_new = patch_plus_file("src/nx/gpu/vk/fg.c")
    if "vk_fg_interpolate_mids" not in fg_new:
        raise SystemExit("patched fg.c missing vk_fg_interpolate_mids")
    fg_path.write_text(fg_new, encoding="utf-8")
    print("OK: fg.c from patch (multi-mid)")

    # --- renderer.c: replace from end-of-show note_display through present_frame with 1.31 block ---
    rend = src / "src" / "nx" / "gpu" / "vk" / "renderer.c"
    rt = rend.read_text(encoding="utf-8", errors="replace")
    # Extract new block from patch hunk (from "if (displayed)" through present_frame closing before comment)
    plines = PATCH.read_text(encoding="utf-8").splitlines()
    hs = next(i for i, l in enumerate(plines) if l.startswith("@@ -544") and "show" in l)
    block = []
    for l in plines[hs + 1 :]:
        if l.startswith("@@"):
            break
        if l.startswith("+"):
            block.append(l[1:])
        elif l.startswith("-"):
            continue
        elif l.startswith(" "):
            block.append(l[1:])
    # block starts with sc_lost context; take from "if (displayed)" through final "}\n\n/* the game queued"
    full = "\n".join(block)
    m = re.search(
        r"(    if \(displayed\) note_display\(\);.*?\n\}\n)\n/\* the game queued a buffer for display:",
        full,
        re.S,
    )
    if not m:
        raise SystemExit("could not extract 1.31 show-end+present_frame from patch")
    new_mid = m.group(1)
    # Replace in 1.30/current tree from same start through present_frame's closing brace before the comment
    m_old = re.search(
        r"    if \(displayed\) note_display\(\);.*?/\* the game queued a buffer for display:",
        rt,
        re.S,
    )
    if not m_old:
        raise SystemExit("renderer.c: could not find show-end..present_frame region")
    if "vk_fg_interpolate_mids" in m_old.group(0) and "present_fg_slots" in m_old.group(0):
        print("OK already: renderer multi-mid present_frame")
    else:
        rt2 = rt[: m_old.start()] + new_mid + "\n/* the game queued a buffer for display:" + rt[m_old.end() :]
        rend.write_text(rt2, encoding="utf-8")
        print("OK: renderer.c multi-mid present_frame")

    # --- runtime_present.c expectations (30@120 FG => 120, not 60) ---
    rtp = src / "tests" / "runtime_present.c"
    if rtp.is_file():
        rt = rtp.read_text(encoding="utf-8", errors="replace")
        old_60 = """    CHECK(present_expected_display_fps(30, 1) == 60, "30 with FG is not about 60 on screen");
    CHECK(present_expected_display_fps(60, 1) == 120, "limit 60 with FG on a 60 FPS game is ~120 on screen");
    CHECK(present_expected_display_fps(120, 1) == 120, "limit 120 with FG stays ~2x the game, not 240");
    CHECK(present_expected_display_fps(144, 1) == 120, "144 with FG on a 60 FPS game stayed at 60");
    CHECK(present_expected_display_fps(240, 1) == 120, "limit 240 with FG stays ~2x the game, not 480");
    CHECK(present_expected_display_fps(0, 1) == 120, "unlimited with FG stayed at 60");"""
        new_60 = """    CHECK(present_fg_slots(30) == 2, "limit 30 on 60 FPS game still classic 2x");
    CHECK(present_fg_slots(60) == 2, "limit 60 on 60 FPS game is classic 2x");
    CHECK(present_fg_slots(120) == 2, "limit 120 on 60 FPS game stays 2x (not 4x)");
    CHECK(present_fg_slots(144) == 2, "144 with FG stays classic 2x");
    CHECK(present_fg_slots(240) == 2, "limit 240 on 60 FPS game clamped to 2x (no multi-mid)");
    CHECK(present_fg_slots(0) == 2, "unlimited defaults to classic 2x");
    CHECK(present_expected_display_fps(30, 1) == 60, "limit 30 with FG is classic 2x of shown side");
    CHECK(present_expected_display_fps(60, 1) == 120, "limit 60 with FG on a 60 FPS game is ~120 on screen");
    CHECK(present_expected_display_fps(120, 1) == 120, "limit 120 with FG on 60 FPS game is ~120");
    CHECK(present_expected_display_fps(240, 1) == 120, "limit 240 with FG clamped to 2x (~120)");
    CHECK(present_expected_display_fps(0, 1) == 120, "unlimited with FG defaults to ~2x");"""
        old_30 = """    CHECK(present_expected_display_fps(120, 1) == 60, "a 30 FPS game with FG shows about 60, not 240");"""
        new_30 = """    CHECK(present_fg_slots(120) == 2, "limit 120 on 30 FPS game stays 2 slots (classic 2x)");
    CHECK(present_expected_display_fps(120, 1) == 60, "a 30 FPS game with FG + Limit 120 shows ~60 (2x), not 120");
    CHECK(present_expected_display_fps(60, 1) == 60, "a 30 FPS game with FG + Limit 60 shows ~60 (classic 2x)");"""
        if "stays 2 slots (classic 2x)" in rt or "PRESENT_FG_MAX_SLOTS = 2" in present.read_text(encoding="utf-8", errors="replace"):
            print("OK already: runtime_present classic 2x checks")
        elif old_60 in rt and old_30 in rt:
            rt = rt.replace(old_60, new_60, 1).replace(old_30, new_30, 1)
            rtp.write_text(rt, encoding="utf-8")
            print("OK: runtime_present.c classic 2x checks")
        else:
            print("WARN: runtime_present.c markers not found (skip)")

    # --- smoke markers ---
    checks = [
        (present, "present_fg_slots"),
        (overlay, "(%dx game %d)"),
        (fg_path, "vk_fg_interpolate_mids"),
        (rend, "present_fg_slots"),
        (ver_h, 'PF_ENGINE_VERSION "1.31"'),
    ]
    for path, needle in checks:
        if needle not in path.read_text(encoding="utf-8", errors="replace"):
            raise SystemExit(f"post-check failed: {needle} in {path}")
    print("UPGRADE_OK", src)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
