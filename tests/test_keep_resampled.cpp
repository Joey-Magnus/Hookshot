// keep_resampled::keep (gpu/vk/keep_resampled.h): the textures the GPU drew that the game samples again after finalizing them
// stay through an eviction pass over the budget, up to a cap. tests/test_keep_resampled.py
#include "../gpu/vk/keep_resampled.h"

#include <cstdio>
#include <cstdlib>

static int failures = 0;
static void expect(bool ok, const char* what) {
    if (!ok) {
        std::printf("FAIL: %s\n", what);
        failures++;
    }
}

int main() {
    const uint64_t MB = 1ull << 20, max = 1024 * MB;
    using keep_resampled::keep;
    uint64_t kept = 0;
    expect(keep(false, true, true, 288 * MB, kept, max) && kept == 288 * MB, "the 12288 x 12288 shadow map, sampled again: kept");
    expect(keep(false, true, true, 32 * MB, kept, max) && kept == 320 * MB, "the 4096 x 4096 one too: both fit");
    expect(!keep(true, true, true, 32 * MB, kept, max) && kept == 320 * MB, "drawn over since: nothing left to keep");
    expect(!keep(false, false, true, 32 * MB, kept, max), "uploaded from the game's memory: made again from there");
    expect(!keep(false, true, false, 32 * MB, kept, max), "never sampled again after finalizing: goes as before");
    expect(!keep(false, true, true, 800 * MB, kept, max) && kept == 320 * MB, "past the cap: goes, the total unchanged");
    expect(keep(false, true, true, 704 * MB, kept, max) && kept == max, "exactly up to the cap: kept");
    expect(!keep(false, true, true, 1, kept, max), "the cap reached: nothing more");
    uint64_t none = 0;
    expect(!keep(false, true, true, 1, none, 0), "RX_VK_KEEP_RESAMPLED_MB=0: evicted as before");
    if (failures) return EXIT_FAILURE;
    std::printf("keep resampled: all checks passed\n");
    return EXIT_SUCCESS;
}
