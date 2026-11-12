// auto_settings (gpu/vk/auto_settings.h): the Quality preset "Auto (this PC)"'s rules. tests/test_auto_settings.py
#include "../gpu/vk/auto_settings.h"
#include "../gpu/vk/upscaler_rows.h"
namespace ur = mkwvk::upscaler_rows;

#include <cstdio>
#include <cstdlib>
#include <cstring>

static int failures = 0;
static void expect(bool ok, const char* what) {
    if (!ok) {
        std::printf("FAIL: %s\n", what);
        failures++;
    }
}

int main() {
    using namespace auto_settings;
    // (the copies of the menu's indices agree with upscaler_rows.h)
    expect((int)GAME == (int)ur::GAME && (int)DLSS == (int)ur::DLSS && (int)FSR4 == (int)ur::FSR4, "upscaler indices as upscaler_rows.h's");
    expect(ur::percent(RES_QUALITY) == 67 && ur::percent(RES_BALANCED) == 58 && ur::percent(RES_PERFORMANCE) == 50,
           "render resolution indices: Quality 67, Balanced 58, Performance 50");

    Hardware h;
    h.vendor = NVIDIA, h.rtx = true, h.dlss_built = true, h.vram_mb = 12282, h.ram_mb = 32000, h.screen_w = 2560, h.screen_h = 1440;
    Choice c = choose(h);
    expect(c.tier == 4 && c.upscaler == DLSS && c.resolution == RES_QUALITY && c.output == 1,
           "RTX, 12 GB, 1440p: High, DLSS Quality, output 2560x1440");
    expect(std::strstr(c.why, "DLSS Quality") && std::strstr(c.why, "2560x1440"), c.why);

    h.dlss_built = false;
    c = choose(h);
    expect(c.upscaler == GAME && c.resolution == 0 && std::strstr(c.why, "no DLSS"), "a build without DLSS: the game's own");

    Hardware amd;
    amd.vendor = AMD, amd.rdna4 = true, amd.fsr4_built = true, amd.vram_mb = 16368, amd.screen_w = 3840, amd.screen_h = 2160;
    c = choose(amd);
    expect(c.tier == 5 && c.upscaler == FSR4 && c.resolution == RES_QUALITY && c.output == 2, "RX 9070 XT, 4K: Ultra, FSR 4, 4K");

    Hardware r6600;
    r6600.vendor = AMD, r6600.vram_mb = 8176, r6600.screen_w = 3840, r6600.screen_h = 2160;
    c = choose(r6600);
    expect(c.tier == 3 && c.upscaler == GAME && c.output == 0, "8 GB on a 4K screen: Balanced, the game's output");
    expect(std::strstr(c.why, "needs tier 4") != nullptr, c.why);

    Hardware six;
    six.vendor = NVIDIA, six.rtx = true, six.dlss_built = true, six.vram_mb = 6144, six.screen_w = 1920, six.screen_h = 1080;
    c = choose(six);
    expect(c.tier == 2 && c.upscaler == DLSS && c.resolution == RES_PERFORMANCE && c.output == 0,
           "RTX 6 GB, 1080p: Low, DLSS Performance, the game's output (1080p needs tier 3)");

    Hardware igpu;
    igpu.vendor = INTEL, igpu.integrated = true, igpu.vram_mb = 16000;
    c = choose(igpu);
    expect(c.tier == 1 && c.upscaler == GAME, "an integrated GPU: Minimum whatever memory it reports");

    Hardware deck;
    deck.vendor = AMD, deck.handheld = true, deck.vram_mb = 1024, deck.screen_w = 1280, deck.screen_h = 800;
    c = choose(deck);
    expect(c.tier == 1 && c.output == 0 && std::strstr(c.why, "handheld"), "a Steam Deck: Minimum");

    Hardware gtx;
    gtx.vendor = NVIDIA, gtx.mesh_native = false, gtx.vram_mb = 8192;
    c = choose(gtx);
    expect(c.tier == 1 && std::strstr(c.why, "fallback"), "no mesh shaders: Minimum");

    Hardware lowram;
    lowram.vendor = NVIDIA, lowram.vram_mb = 12282, lowram.ram_mb = 8192;
    c = choose(lowram);
    expect(c.tier == 2 && std::strstr(c.why, "12 GB of RAM"), "8 GB of RAM: held at Low");

    Hardware unknown;
    unknown.vram_mb = 24564;
    c = choose(unknown);
    expect(c.tier == 5 && c.output == 0, "no screen known: the game's output");

    expect(fingerprint(h, 0x10de, 0x2782) != fingerprint(h, 0x10de, 0x2783), "another GPU: another fingerprint");
    Hardware moved = h;
    moved.screen_w = 3840, moved.screen_h = 2160;
    expect(fingerprint(h, 0x10de, 0x2782) != fingerprint(moved, 0x10de, 0x2782), "another screen: another fingerprint");
    moved = h;
    moved.vram_mb += 3;   // (the heap's size wobbles by a few MB between drivers)
    expect(fingerprint(h, 0x10de, 0x2782) == fingerprint(moved, 0x10de, 0x2782), "a few MB more memory: the same fingerprint");

    // (the tuning in play)
    {
        Tuning t;
        for (int i = 0; i < kWindows - 1; i++) expect(tune(t, 17.5f, 900, 57, 60, true, true) == WAIT, "gathering");
        expect(tune(t, 17.5f, 900, 57, 60, true, true) == LIGHTER, "the GPU over 90% of 16.7 ms: one step lighter");
        for (int i = 0; i < kWindows - 1; i++) tune(t, 6.0f, 900, 60, 60, true, true);
        expect(tune(t, 6.0f, 900, 60, 60, true, true) == KEEP && t.done, "after a step down, never back up: done");
    }
    {
        Tuning t;
        for (int i = 0; i < kWindows; i++) tune(t, 20, 100, 30, 60, true, true);   // (a menu: under 150 draws)
        expect(t.n == 0 && !t.done, "menus and loading screens do not count");
        for (int i = 0; i < kWindows - 1; i++) tune(t, 7.0f, 900, 45, 60, true, true);
        expect(tune(t, 7.0f, 900, 45, 60, true, true) == CPU_BOUND && t.done, "45 fps with the GPU at 7 ms: the CPU holds it");
    }
    {
        Tuning t;
        for (int i = 0; i < kWindows - 1; i++) tune(t, 5.0f, 900, 60, 60, true, true);
        expect(tune(t, 5.0f, 900, 60, 60, true, true) == HEAVIER, "the GPU at 30%: one step heavier");
        for (int i = 0; i < kWindows - 1; i++) tune(t, 5.0f, 900, 60, 60, true, false);
        expect(tune(t, 5.0f, 900, 60, 60, true, false) == KEEP, "back at the profile's start: no further");
    }
    {
        Tuning t;
        int lighter = 0;
        for (int r = 0; r < 5; r++)
            for (int i = 0; i < kWindows; i++) lighter += tune(t, 25.0f, 900, 40, 60, true, true) == LIGHTER;
        expect(lighter == kMaxSteps && t.done, "at most two steps");
    }
    {
        Tuning t;
        for (int i = 0; i < kWindows - 1; i++) tune(t, 18.0f, 900, 55, 60, false, true);
        expect(tune(t, 18.0f, 900, 55, 60, false, true) == KEEP, "no lighter step left: keep");
    }

    if (failures) return EXIT_FAILURE;
    std::printf("auto settings: all checks passed\n");
    return EXIT_SUCCESS;
}
