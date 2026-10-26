/* Isolated control-flow regression harness. Compiled by run_fg_motion_deferral_tests.py.
 * The production vk_fg_interpolate_mids() body is included verbatim, not reimplemented.
 * Vulkan/filter/AMD/motion/warp dependencies are mocked; no GPU or game is used. */
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <stdarg.h>
#include <stdlib.h>

typedef uint32_t u32;
typedef uint64_t u64;
typedef int VkCommandBuffer;
typedef struct { int img; u32 sw, sh; } Surf;
typedef struct { Surf s; } Pic;
enum { FG_MAX_MIDS = 3 }; /* matches the current default in vk.h */
static struct {
    int cur, have_prev;
    u32 w, h, up_w, up_h;
    int up_ready[2];
    Pic hist[2], out, mids[FG_MAX_MIDS], up[2], up_mids[FG_MAX_MIDS];
    u64 made;
} g_fg;
static struct { int upscaler; } g_settings;
static struct {
    int amd_ok, filter_ok, blit_ok, motion_ok;
    int retire_filter, retire_blit, retire_amd, retire_warp;
    int motion_calls, amd_calls, warp_calls, motion_ready, fail_warp_at;
    VkCommandBuffer active_cb;
    int events[32], event_count, phases[FG_MAX_MIDS];
    int enlarged_warps;
} mock;
static Surf scaled;
enum { FILTER = 1, BLIT, AMD, MOTION, WARP };
static const char* test_name;
static int tests_run;
#undef assert
#define assert(expression) do { if (!(expression)) { \
    fprintf(stderr, "FAIL %s at line %d: %s\n", test_name, __LINE__, #expression); \
    exit(1); \
} } while (0)

static void log_noop(const char* format, ...) { (void)format; }
#define LOG(...) log_noop(__VA_ARGS__)
static void event(int value) {
    assert(mock.event_count < (int)(sizeof mock.events / sizeof mock.events[0]));
    mock.events[mock.event_count++] = value;
}
static Surf* vk_fg_current(void) { return g_fg.have_prev ? &g_fg.hist[g_fg.cur].s : NULL; }
static void vk_render_end(void) {}
static void vk_render_forget_state(void) {}
static VkCommandBuffer vk_cmd(void) { return mock.active_cb; }
static Surf* vk_present_filters(VkCommandBuffer cb, Surf* src, u32 x, u32 y,
                               u32 w, u32 h, u32 dw, u32 dh) {
    assert(cb == mock.active_cb);
    assert(src == &g_fg.hist[g_fg.cur].s);
    assert(!x && !y && w == g_fg.w && h == g_fg.h);
    event(FILTER);
    if (mock.retire_filter) ++mock.active_cb;
    scaled.sw = dw; scaled.sh = dh;
    return mock.filter_ok ? &scaled : NULL;
}
static int blit_to_pic(VkCommandBuffer cb, Surf* src, Pic* dst, u32 dw, u32 dh) {
    assert(cb == mock.active_cb);
    assert(src == &scaled && dst == &g_fg.up[g_fg.cur]);
    event(BLIT);
    /* Model a helper allocation/resize retiring its caller's buffer. This does
     * not test blit_to_pic's internal Vulkan recording/lifetime correctness. */
    if (mock.retire_blit) ++mock.active_cb;
    if (!mock.blit_ok) return 0;
    dst->s = (Surf){42, dw, dh};
    return 1;
}
static Surf* amd_mid(Pic* a, Pic* b, Pic* dst, u32 dw, u32 dh) {
    assert(a != b && dw && dh);
    event(AMD); ++mock.amd_calls;
    if (mock.retire_amd) ++mock.active_cb;
    return mock.amd_ok ? &dst->s : NULL;
}
static int fg_motion(VkCommandBuffer cb) {
    /* Catches a stale command buffer following filtering, blitting, or AMD. */
    assert(cb == mock.active_cb);
    event(MOTION); ++mock.motion_calls;
    mock.motion_ready = mock.motion_ok;
    return mock.motion_ok;
}
static Surf* fg_warp(VkCommandBuffer cb, Pic* a, Pic* b, Pic* dst, u32 dw, u32 dh, int phase) {
    (void)cb; /* The real fg_warp reacquires vk_cmd() after pic() can resize. */
    assert(mock.motion_ready && "legacy warp must be preceded by successful motion search");
    assert(a != b && dw && dh);
    assert(mock.warp_calls < FG_MAX_MIDS);
    mock.phases[mock.warp_calls] = phase;
    ++mock.warp_calls; event(WARP);
    if (mock.retire_warp) ++mock.active_cb;
    if (a == &g_fg.up[g_fg.cur ^ 1]) {
        assert(b == &g_fg.up[g_fg.cur]);
        assert(dst == &g_fg.up_mids[mock.warp_calls - 1]);
        assert(dw == g_fg.up_w && dh == g_fg.up_h);
        ++mock.enlarged_warps;
    } else {
        assert(a == &g_fg.hist[g_fg.cur ^ 1] && b == &g_fg.hist[g_fg.cur]);
        assert(dst == &g_fg.mids[mock.warp_calls - 1]);
        assert(dw == g_fg.w && dh == g_fg.h);
    }
    if (mock.fail_warp_at == mock.warp_calls) return NULL;
    return &dst->s;
}

#include "fg_interpolate_under_test.inc"

static void reset_case(const char* name) {
    test_name = name;
    memset(&g_fg, 0, sizeof g_fg);
    memset(&mock, 0, sizeof mock);
    g_settings.upscaler = 1;
    g_fg.cur = 1; g_fg.have_prev = 1;
    g_fg.w = 640; g_fg.h = 360;
    g_fg.up_w = 1280; g_fg.up_h = 720;
    g_fg.up_ready[0] = 1;
    g_fg.out.s.img = 1;
    mock.active_cb = 10;
    mock.amd_ok = mock.filter_ok = mock.blit_ok = mock.motion_ok = 1;
    /* Each allocating helper changes the mock handle, exercising caller refreshes. */
    mock.retire_filter = mock.retire_blit = mock.retire_amd = mock.retire_warp = 1;
}
static int run(u32 dw, u32 dh, int nmids, Surf** current, Surf** mids) {
    *current = NULL;
    memset(mids, 0, sizeof(Surf*) * FG_MAX_MIDS);
    return vk_fg_interpolate_mids(mock.active_cb, dw, dh, current, mids, nmids);
}
static void expect_events(const int* events, int count) {
    assert(mock.event_count == count);
    assert(!count || memcmp(mock.events, events, (size_t)count * sizeof(int)) == 0);
    ++tests_run;
    printf("PASS %s\n", test_name);
}
#define EVENTS(...) do { const int expected[] = {__VA_ARGS__}; \
    expect_events(expected, (int)(sizeof expected / sizeof expected[0])); } while (0)
#define NO_EVENTS() expect_events(NULL, 0)

int main(void) {
    Surf* current;
    Surf* mids[FG_MAX_MIDS];
    int n;

    reset_case("enlarged AMD success avoids legacy motion (even when motion would fail)");
    mock.motion_ok = 0;
    n = run(1280, 720, 1, &current, mids);
    assert(n == 1 && current == &g_fg.up[1].s && mids[0] == &g_fg.up_mids[0].s);
    assert(!mock.motion_calls && !mock.warp_calls && g_fg.made == 1);
    EVENTS(FILTER, BLIT, AMD);

    reset_case("enlarged AMD unavailable/disabled falls back after attempt");
    mock.amd_ok = 0; mock.retire_amd = 0; /* early disabled return */
    n = run(1280, 720, 1, &current, mids);
    assert(n == 1 && mock.motion_calls == 1 && mock.enlarged_warps == 1);
    assert(current == &g_fg.up[1].s && mids[0] == &g_fg.up_mids[0].s);
    assert(mock.phases[0] == 32768);
    EVENTS(FILTER, BLIT, AMD, MOTION, WARP);

    reset_case("enlarged AMD failure after resize refreshes command buffer before search");
    mock.amd_ok = 0;
    n = run(1280, 720, 1, &current, mids);
    assert(n == 1 && mock.motion_calls == 1 && mock.enlarged_warps == 1);
    EVENTS(FILTER, BLIT, AMD, MOTION, WARP);

    reset_case("multiple mids skip AMD and search once before enlarged warps");
    n = run(1280, 720, 3, &current, mids);
    assert(n == 3 && !mock.amd_calls && mock.motion_calls == 1 && mock.enlarged_warps == 3);
    assert(mock.phases[0] == 16384 && mock.phases[1] == 32768 && mock.phases[2] == 49152);
    assert(mids[2] == &g_fg.up_mids[2].s && g_fg.made == 1);
    EVENTS(FILTER, BLIT, MOTION, WARP, WARP, WARP);

    reset_case("filter failure still searches before native fallback");
    mock.filter_ok = 0;
    n = run(1280, 720, 1, &current, mids);
    assert(n == 1 && mock.motion_calls == 1 && !mock.amd_calls && !mock.enlarged_warps);
    assert(current == &g_fg.hist[1].s && mids[0] == &g_fg.mids[0].s);
    EVENTS(FILTER, MOTION, WARP);

    reset_case("blit failure after resize still searches with fresh command buffer");
    mock.blit_ok = 0;
    n = run(1280, 720, 1, &current, mids);
    assert(n == 1 && mock.motion_calls == 1 && current == &g_fg.hist[1].s);
    EVENTS(FILTER, BLIT, MOTION, WARP);

    reset_case("filter failure with multiple mids searches once");
    mock.filter_ok = 0;
    n = run(1280, 720, 3, &current, mids);
    assert(n == 3 && mock.motion_calls == 1 && !mock.enlarged_warps);
    EVENTS(FILTER, MOTION, WARP, WARP, WARP);

    reset_case("motion failure after AMD failure stops enlarged warps");
    mock.amd_ok = mock.motion_ok = 0;
    n = run(1280, 720, 1, &current, mids);
    assert(!n && !mock.warp_calls && current == &g_fg.hist[1].s && !g_fg.made);
    EVENTS(FILTER, BLIT, AMD, MOTION);

    reset_case("motion failure on fsr_skip stops native warps");
    mock.filter_ok = mock.motion_ok = 0;
    n = run(1280, 720, 1, &current, mids);
    assert(!n && !mock.warp_calls && current == &g_fg.hist[1].s);
    EVENTS(FILTER, MOTION);

    reset_case("previous filtered image unavailable needs no search yet");
    g_fg.up_ready[0] = 0;
    n = run(1280, 720, 1, &current, mids);
    assert(!n && !mock.motion_calls && !mock.amd_calls && current == &g_fg.up[1].s);
    EVENTS(FILTER, BLIT);

    reset_case("output resize invalidates previous filtered image without search");
    n = run(1920, 1080, 1, &current, mids);
    assert(!n && !mock.motion_calls && !g_fg.up_ready[0] && g_fg.up_ready[1]);
    assert(g_fg.up_w == 1920 && g_fg.up_h == 1080 && current == &g_fg.up[1].s);
    EVENTS(FILTER, BLIT);

    reset_case("missing history returns without any pipeline work");
    g_fg.have_prev = 0;
    n = run(1280, 720, 1, &current, mids);
    assert(!n && !current);
    NO_EVENTS();

    reset_case("partial enlarged warp failure retains prior successful mids");
    mock.fail_warp_at = 2;
    n = run(1280, 720, 3, &current, mids);
    assert(n == 1 && mock.motion_calls == 1 && current == &g_fg.up[1].s);
    assert(mids[0] == &g_fg.up_mids[0].s && !mids[1]);
    EVENTS(FILTER, BLIT, MOTION, WARP, WARP);

    reset_case("partial fsr_skip warp failure retains prior successful mids");
    mock.filter_ok = 0; mock.fail_warp_at = 2;
    n = run(1280, 720, 3, &current, mids);
    assert(n == 1 && mock.motion_calls == 1 && current == &g_fg.hist[1].s);
    EVENTS(FILTER, MOTION, WARP, WARP);

    reset_case("midpoint count clamps to one and retains AMD-first behavior");
    n = run(1280, 720, 0, &current, mids);
    assert(n == 1 && !mock.motion_calls);
    EVENTS(FILTER, BLIT, AMD);

    reset_case("midpoint count clamps to max with one legacy search");
    n = run(1280, 720, FG_MAX_MIDS + 5, &current, mids);
    assert(n == FG_MAX_MIDS && mock.motion_calls == 1 && !mock.amd_calls);
    EVENTS(FILTER, BLIT, MOTION, WARP, WARP, WARP);

    reset_case("opposite history slot retains AMD-first behavior");
    g_fg.cur = 0; g_fg.up_ready[0] = 0; g_fg.up_ready[1] = 1;
    n = run(1280, 720, 1, &current, mids);
    assert(n == 1 && current == &g_fg.up[0].s && !mock.motion_calls);
    EVENTS(FILTER, BLIT, AMD);

    reset_case("native AMD success retains no-motion behavior");
    n = run(640, 360, 1, &current, mids);
    assert(n == 1 && !mock.motion_calls && current == &g_fg.hist[1].s);
    assert(mids[0] == &g_fg.mids[0].s);
    EVENTS(AMD);

    reset_case("native AMD failure retains motion-before-warp behavior");
    mock.amd_ok = 0;
    n = run(640, 360, 1, &current, mids);
    assert(n == 1 && mock.motion_calls == 1 && !mock.enlarged_warps);
    EVENTS(AMD, MOTION, WARP);

    reset_case("native multiple mids retain one motion search");
    n = run(640, 360, 3, &current, mids);
    assert(n == 3 && mock.motion_calls == 1 && !mock.amd_calls);
    EVENTS(MOTION, WARP, WARP, WARP);

    reset_case("native motion failure retains zero-output behavior");
    mock.amd_ok = mock.motion_ok = 0;
    n = run(640, 360, 1, &current, mids);
    assert(!n && !mock.warp_calls && current == &g_fg.hist[1].s);
    EVENTS(AMD, MOTION);

    reset_case("disabled upscaler retains native path despite larger output");
    g_settings.upscaler = 0;
    n = run(1280, 720, 1, &current, mids);
    assert(n == 1 && !mock.motion_calls && current == &g_fg.hist[1].s);
    EVENTS(AMD);

    reset_case("native absent output image retains early return");
    g_fg.out.s.img = 0;
    n = run(640, 360, 1, &current, mids);
    assert(!n && current == &g_fg.hist[1].s);
    NO_EVENTS();

    printf("All %d mocked production-function cases passed.\n", tests_run);
    return 0;
}
