#pragma once

#include "libraw_types.h"

struct NativeBayerArea
{
    int left;
    int top;
    int width;
    int height;
};

// Keep LibRaw's active-area origin so copying pixels does not change Bayer phase.
inline bool TryGetNativeBayerArea(const libraw_image_sizes_t& sizes, NativeBayerArea& area)
{
    area.left = sizes.left_margin;
    area.top = sizes.top_margin;
    // LibRaw has already identified the active area. A second fixed trim loses
    // valid GFX pixels; the managed ASCOM conversion applies the final crop.
    area.width = sizes.width & ~1;
    area.height = sizes.height;
    return area.width > 0 && area.height > 0 &&
        area.left + area.width <= sizes.raw_width &&
        area.top + area.height <= sizes.raw_height;
}
