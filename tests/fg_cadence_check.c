/* Headless check of Engine 1.31 present_fg_slots / expected display FPS math. */
#include <stdio.h>

enum { SNX_FPS_GAME = -1 };
enum { PRESENT_FG_MAX_SLOTS = 4 };

static int g_game_fps = 60;
static int g_lock = 1;

static int present_game_side_fps(int fps_limit) {
    int game_fps = g_game_fps;
    if (fps_limit == SNX_FPS_GAME) return game_fps;
    if (fps_limit <= 0) return g_lock && game_fps > 0 ? game_fps : 0;
    if (g_lock && game_fps > 0 && fps_limit > game_fps) return game_fps;
    return fps_limit;
}

static int present_fg_slots(int fps_limit) {
    int side = present_game_side_fps(fps_limit), target, slots;
    if (side <= 0) return 1;
    target = side * 2; /* classic FG floor */
    if (fps_limit > 0 && fps_limit != SNX_FPS_GAME && fps_limit > target) target = fps_limit;
    if (target > side * PRESENT_FG_MAX_SLOTS) target = side * PRESENT_FG_MAX_SLOTS;
    slots = target / side;
    if (slots < 1) slots = 1;
    if (slots > PRESENT_FG_MAX_SLOTS) slots = PRESENT_FG_MAX_SLOTS;
    return slots;
}

static int present_expected_display_fps(int fps_limit, int frame_generation) {
    int side = present_game_side_fps(fps_limit);
    if (!frame_generation || side <= 0) return side;
    return side * present_fg_slots(fps_limit);
}

static int fails;
#define CHECK(c, ...) do { if (!(c)) { fprintf(stderr, "FAIL: "); fprintf(stderr, __VA_ARGS__); fprintf(stderr, "\n"); fails++; } } while (0)

int main(void) {
    fails = 0;
    g_game_fps = 60;
    CHECK(present_expected_display_fps(30, 0) == 30, "60-game limit 30 no FG");
    CHECK(present_expected_display_fps(120, 0) == 60, "60-game limit 120 no FG");
    CHECK(present_expected_display_fps(30, 1) == 60, "60-game limit 30 FG = 2x side");
    CHECK(present_expected_display_fps(60, 1) == 120, "60-game limit 60 FG classic 2x");
    CHECK(present_expected_display_fps(120, 1) == 120, "60-game limit 120 FG");
    CHECK(present_expected_display_fps(240, 1) == 240, "60-game limit 240 FG multi-mid");
    CHECK(present_fg_slots(0) == 2, "unlimited defaults 2x");

    g_game_fps = 30;
    CHECK(present_expected_display_fps(120, 0) == 30, "30-game limit 120 no FG");
    CHECK(present_expected_display_fps(60, 1) == 60, "30-game limit 60 FG = 2x");
    CHECK(present_fg_slots(120) == 4, "30-game limit 120 = 4 slots");
    CHECK(present_expected_display_fps(120, 1) == 120, "30-game limit 120 FG = 120 (not stuck at 60)");
    CHECK(present_expected_display_fps(30, 1) == 60, "30-game limit 30 FG still classic 2x");

    if (fails) { fprintf(stderr, "%d checks failed\n", fails); return 1; }
    puts("PASS fg_cadence_check");
    return 0;
}
