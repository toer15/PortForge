/* Headless check: FG On => 2x game; FG Off => Display equals Limit (pace uses Limit). */
#include <stdio.h>

enum { SNX_FPS_GAME = -1 };
enum { PRESENT_FG_MAX_SLOTS = 2 };
enum { VI_REFRESH_HZ = 60 };

static int g_game_fps = 60;
static int g_lock = 1;
static int g_fg = 0;

static int present_game_side_fps(int fps_limit) {
    int game_fps = g_game_fps;
    if (fps_limit == SNX_FPS_GAME) return game_fps;
    if (fps_limit <= 0) return g_lock && game_fps > 0 ? game_fps : 0;
    if (g_fg && g_lock && game_fps > 0 && fps_limit > game_fps) return game_fps;
    return fps_limit;
}

static int present_fg_slots(int fps_limit) {
    int side = present_game_side_fps(fps_limit);
    (void)fps_limit;
    if (side <= 0) return 1;
    return PRESENT_FG_MAX_SLOTS;
}

static int present_expected_display_fps(int fps_limit, int frame_generation) {
    int side = present_game_side_fps(fps_limit);
    if (!frame_generation) {
        if (fps_limit > 0 && fps_limit != SNX_FPS_GAME) return fps_limit;
        return side;
    }
    if (side <= 0) return 0;
    return side * present_fg_slots(fps_limit);
}

/* Mirror present_period: FG On+lock => game; FG Off => Limit cap. */
static unsigned long long present_period_ns(int fps, int interval, int lock, int fg) {
    unsigned long long game = interval ? 1000000000ull / VI_REFRESH_HZ * (unsigned)interval : 0;
    unsigned long long cap = fps > 0 ? 1000000000ull / (unsigned)fps : 0;
    if (fps == SNX_FPS_GAME) return game;
    if (lock && game && fg) return game;
    if (!fps) return game;
    return cap;
}

static int fails;
#define CHECK(c, ...) do { if (!(c)) { fprintf(stderr, "FAIL: "); fprintf(stderr, __VA_ARGS__); fprintf(stderr, "\n"); fails++; } } while (0)

int main(void) {
    fails = 0;

    /* 30 FPS guest (swap interval 2), FG Off: Limit locks pace */
    g_game_fps = 30;
    g_fg = 0;
    CHECK(present_expected_display_fps(60, 0) == 60, "Off Limit 60 expect 60");
    CHECK(present_expected_display_fps(120, 0) == 120, "Off Limit 120 expect 120");
    CHECK(present_period_ns(60, 2, 1, 0) == 1000000000ull / 60, "Off Limit 60 period is 60Hz");
    CHECK(present_period_ns(120, 2, 1, 0) == 1000000000ull / 120, "Off Limit 120 period is 120Hz");
    CHECK(present_period_ns(0, 2, 1, 0) == 1000000000ull / 60 * 2, "Off unlimited keeps native 30Hz period");

    /* FG On: guest stays native; display 2x */
    g_fg = 1;
    CHECK(present_game_side_fps(120) == 30, "On+Limit 120 game_side stays 30");
    CHECK(present_expected_display_fps(120, 1) == 60, "On+Limit 120 display 2x=60");
    CHECK(present_fg_slots(120) == 2, "On slots=2");
    CHECK(present_period_ns(120, 2, 1, 1) == 1000000000ull / 60 * 2, "On+Limit 120 period stays native 30Hz");

    g_game_fps = 60;
    g_fg = 1;
    CHECK(present_expected_display_fps(120, 1) == 120, "60-game On display 2x=120");
    CHECK(present_period_ns(240, 1, 1, 1) == 1000000000ull / 60, "60-game On period stays 60Hz");
    g_fg = 0;
    CHECK(present_expected_display_fps(144, 0) == 144, "60-game Off Limit 144");
    CHECK(present_period_ns(144, 1, 1, 0) == 1000000000ull / 144, "60-game Off Limit 144 period");

    if (fails) { fprintf(stderr, "%d checks failed\n", fails); return 1; }
    puts("PASS fg_cadence_check");
    return 0;
}
