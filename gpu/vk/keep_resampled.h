// Which textures the GPU drew and the game samples again after finalizing them the eviction pass keeps (vk_texture.cpp).
// tests/test_keep_resampled.py
#pragma once
#include <cstdint>

namespace keep_resampled {

// A texture the GPU drew, that the game finalized and then sampled again (Texture::resampled), with nothing drawn over its
// storage since: a cache the game keeps, such as the 12288 x 12288 shadow map it draws once and samples for many frames. It is
// depth, so its texels cannot be written back into its memory; evicted over the budget, it was made again over storage the GPU
// rendered, not uploaded, and sampled EMPTY until the game drew it again some 40 frames later (Dodongo's Cavern at a 512 MB
// budget, issue 2209's dark ground in Hyrule Field). Kept while the kept total stays within max; kept: the bytes kept so far
// in this pass
inline bool keep(bool overwritten, bool gpu_written, bool resampled, uint64_t bytes, uint64_t& kept, uint64_t max) {
    if (overwritten || !gpu_written || !resampled || !max || bytes > max - kept) return false;
    kept += bytes;
    return true;
}

} // namespace keep_resampled
