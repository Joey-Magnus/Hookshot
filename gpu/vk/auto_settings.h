// The Quality preset "Auto (this PC)": the settings a PC starts with, from what it reports about itself (vk_menu.cpp,
// menu_auto_for_gpu). tests/test_auto_settings.py
//
// One rule table, no list of GPU models: the facts a card reports reliably (its video memory, integrated or not, mesh shaders,
// NVIDIA RTX or AMD RDNA 4 for the upscalers) and the screen's size. How fast the card is, which a model list would only guess,
// is left to the measured tuning in play. Each choice says why, for the log line a bug report carries.
#pragma once
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace auto_settings {

enum Vendor : int { OTHER = 0, NVIDIA = 1, AMD = 2, INTEL = 3 };

struct Hardware {
    int vendor = OTHER;
    bool integrated = false;
    bool handheld = false;      // (a Steam Deck or another handheld PC: its own small screen and a shared memory budget)
    bool mesh_native = true;    // (false: the mesh programs run as compute, the fallback, slower)
    bool rtx = false;           // (an NVIDIA RTX card: DLSS's tensor cores)
    bool rdna4 = false;         // (an AMD RDNA 4 card, RX 9000: FSR 4)
    bool dlss_built = false;    // (the game was built with NVIDIA's DLSS SDK)
    bool fsr4_built = false;    // (with AMD's FSR 4 runtime)
    uint32_t vram_mb = 0;
    uint32_t ram_mb = 0;        // (0: not known)
    int threads = 0;            // (0: not known)
    int screen_w = 0, screen_h = 0;   // (the desktop's size in pixels; 0: not known)
    int refresh_hz = 0;
};

// What the profile sets. Indices are the menu's own: the Upscaler (upscaler_rows.h, ur::GAME...), the Render resolution
// (ur::kPercent's index), and the Output resolution (vk_menu.cpp's kOutputs; 0, the game's 2048 x 1152)
struct Choice {
    int tier = 0;               // (the preset whose memory budgets and scaling hold: 1 Minimum ... 5 Ultra)
    int upscaler = 0;
    int resolution = 0;         // (0: the tier's own, as the preset sets it)
    int output = 0;
    char why[480] = {};
};

// The output sizes the menu offers, in kOutputs' order (vk_menu.cpp): the profile names one of them
constexpr int kOutputW[] = {2048, 2560, 3840, 1920, 5120, 5120};
constexpr int kOutputH[] = {1152, 1440, 2160, 1080, 1440, 2880};
constexpr int kOutputs = sizeof kOutputW / sizeof kOutputW[0];
// (the least tier each size is chosen at: the memory a larger frame's targets take)
constexpr int kOutputTier[] = {0, 3, 4, 3, 4, 5};

// (upscaler_rows.h's, kept here so the rules compile on their own; the test checks they agree)
enum { GAME = 0, DLSS = 1, FSR4 = 4 };
enum { RES_QUALITY = 3, RES_PERFORMANCE = 4, RES_BALANCED = 5 };

// The memory tier: the presets' own steps (Minimum 4 GB, Low 6, Balanced 8, High 12, Ultra 16+). An integrated GPU, a
// handheld or a card running the mesh fallback starts at Minimum whatever its memory says: the memory it reports is shared
// with the system, or the fallback costs the frame more than the memory saves
inline int tier_of(const Hardware& h) {
    if (h.integrated || h.handheld || !h.mesh_native) return 1;
    const uint32_t mb = h.vram_mb;
    int t = mb <= 4608 ? 1 : mb <= 6656 ? 2 : mb <= 9216 ? 3 : mb <= 13312 ? 4 : 5;
    // (under 12 GB of RAM the game's memory and the copies the driver keeps of its textures leave little: a lower tier's
    // smaller budgets)
    if (h.ram_mb && h.ram_mb < 12288 && t > 2) t = 2;
    return t;
}

inline void add(Choice& c, const char* text) {
    const size_t n = std::strlen(c.why);
    std::snprintf(c.why + n, sizeof c.why - n, "%s%s", n ? "; " : "", text);
}

inline Choice choose(const Hardware& h) {
    Choice c;
    c.tier = tier_of(h);
    char b[160];
    if (h.integrated) add(c, "integrated GPU: Minimum");
    else if (h.handheld) add(c, "handheld: Minimum");
    else if (!h.mesh_native) add(c, "no mesh shaders (the compute fallback): Minimum");
    else {
        std::snprintf(b, sizeof b, "%u MB of video memory: tier %d", (unsigned)h.vram_mb, c.tier);
        add(c, b);
        if (h.ram_mb && h.ram_mb < 12288 && c.tier == 2 && h.vram_mb > 6656) add(c, "under 12 GB of RAM: held at Low");
    }
    // The upscaler: the one the card has hardware for, else the game's own (on every card, and what the presets were tuned
    // on). At Minimum the game's own at its lowest share is the lightest
    if (c.tier >= 2 && h.rtx && h.dlss_built) {
        c.upscaler = DLSS;
        c.resolution = c.tier == 2 ? RES_PERFORMANCE : c.tier == 3 ? RES_BALANCED : RES_QUALITY;
        add(c, c.tier == 2 ? "NVIDIA RTX: DLSS Performance" : c.tier == 3 ? "NVIDIA RTX: DLSS Balanced" : "NVIDIA RTX: DLSS Quality");
    } else if (c.tier >= 2 && h.rdna4 && h.fsr4_built) {
        c.upscaler = FSR4;
        c.resolution = c.tier == 2 ? RES_PERFORMANCE : c.tier == 3 ? RES_BALANCED : RES_QUALITY;
        add(c, c.tier == 2 ? "AMD RDNA 4: FSR 4 Performance" : c.tier == 3 ? "AMD RDNA 4: FSR 4 Balanced" : "AMD RDNA 4: FSR 4 Quality");
    } else {
        add(c, h.rtx && !h.dlss_built ? "the game's own upscaler (this build has no DLSS)"
               : h.rdna4 && !h.fsr4_built ? "the game's own upscaler (this build has no FSR 4)" : "the game's own upscaler");
    }
    // The output: the screen's own size where the menu offers it and the tier has the memory for its frame; else the game's
    for (int i = 1; i < kOutputs; i++)
        if (h.screen_w == kOutputW[i] && h.screen_h == kOutputH[i]) {
            if (c.tier >= kOutputTier[i]) {
                c.output = i;
                std::snprintf(b, sizeof b, "output %dx%d (the screen's)", kOutputW[i], kOutputH[i]);
            } else {
                std::snprintf(b, sizeof b, "output 2048x1152 (the screen's %dx%d needs tier %d)", kOutputW[i], kOutputH[i],
                              kOutputTier[i]);
            }
            add(c, b);
            break;
        }
    if (!c.output && (h.screen_w != kOutputW[0] || h.screen_h != kOutputH[0])) {
        bool named = false;
        for (int i = 1; i < kOutputs; i++) named |= h.screen_w == kOutputW[i] && h.screen_h == kOutputH[i];
        if (!named) add(c, h.screen_w ? "output 2048x1152 (the screen's size is not one the menu offers)" : "output 2048x1152");
    }
    return c;
}

// The fingerprint the options file keeps (auto_for=): the choice is made again when the hardware it was made for changes
inline uint64_t fingerprint(const Hardware& h, uint32_t vendor_id, uint32_t device_id) {
    uint64_t f = 1469598103934665603ull;
    const uint64_t parts[] = {vendor_id, device_id, h.vram_mb >> 8, (uint64_t)h.screen_w, (uint64_t)h.screen_h,
                              (uint64_t)h.dlss_built, (uint64_t)h.fsr4_built, (uint64_t)h.mesh_native};
    for (uint64_t p : parts) f = (f ^ p) * 1099511628211ull;
    return f;
}

// The measured tuning in play: the render resolution moved toward what this PC holds at its frame rate cap. Each window is the
// replay's 120 presents (vk_replay.cpp): the GPU's time a frame, the draws a frame and the frame rate. Only windows of play count
// (150 draws a frame or more, as the control bridge's "play"; menus and loading screens draw fewer). After kWindows of them,
// their middle values decide:
// - the frame rate is short of the cap while the GPU is idle for a quarter of the frame or more: the CPU holds it (the game's
//   threads, the replay), and a smaller picture would not help: done, nothing changed
// - the GPU takes over 90% of the cap's frame time: one step smaller (lighter)
// - under 50%: one step larger, never past the tier's start (the profile's own choice): what Auto chose was for the card's memory
// - else: done
// At most kMaxSteps changes in all; a step down is never followed by a step up (no seesaw). Then done until the hardware changes
constexpr int kWindows = 8;
constexpr int kMaxSteps = 2;
enum Verdict : int { WAIT = 0, KEEP = 1, CPU_BOUND = 2, LIGHTER = 3, HEAVIER = 4 };
struct Tuning {
    float gpu_ms[kWindows] = {}, fps[kWindows] = {};
    int n = 0;          // (play windows gathered since the last change)
    int steps = 0;
    bool went_down = false;
    bool done = false;
};
inline float middle(float* v, int n) {   // (the median; sorts v)
    for (int i = 1; i < n; i++)
        for (int j = i; j > 0 && v[j] < v[j - 1]; j--) { const float t = v[j]; v[j] = v[j - 1]; v[j - 1] = t; }
    return n % 2 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2;
}
// One window: WAIT while gathering, else the verdict. cap_fps: the frame rate cap's (the display's refresh when uncapped);
// can_lighter, can_heavier: whether the render resolution has a step that way (heavier: not past the profile's start)
inline Verdict tune(Tuning& t, float gpu_ms, float draws, float fps, float cap_fps, bool can_lighter, bool can_heavier) {
    if (t.done || draws < 150 || !(gpu_ms > 0) || !(fps > 0) || !(cap_fps > 0)) return WAIT;
    t.gpu_ms[t.n] = gpu_ms, t.fps[t.n] = fps;
    if (++t.n < kWindows) return WAIT;
    const float g = middle(t.gpu_ms, t.n), f = middle(t.fps, t.n), budget = 1000.0f / cap_fps;
    t.n = 0;
    if (f < cap_fps * 0.95f && g < 0.75f * (1000.0f / f)) return t.done = true, CPU_BOUND;
    if (g > 0.9f * budget && can_lighter && t.steps < kMaxSteps) return t.steps++, t.went_down = true, LIGHTER;
    if (g < 0.5f * budget && can_heavier && !t.went_down && t.steps < kMaxSteps) return t.steps++, HEAVIER;
    t.done = true;
    return KEEP;
}

} // namespace auto_settings
