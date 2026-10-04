#include "../LibRawWrapper/NativeBayerArea.h"
#include <iostream>
#include <stdexcept>

static void Require(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
}

int main()
{
    try
    {
        // Measured from a GFX100S RAF with LibRaw 0.21.4. The ASCOM config is
        // 11648 x 8736; subtracting another 48 columns makes it impossible to crop.
        libraw_image_sizes_t sizes = {};
        sizes.raw_width = 11808;
        sizes.raw_height = 8754;
        sizes.width = 11662;
        sizes.height = 8752;
        sizes.left_margin = 0;
        sizes.top_margin = 2;
        NativeBayerArea area = {};
        Require(TryGetNativeBayerArea(sizes, area), "GFX100S active area rejected");
        Require(area.width >= 11648 && area.height >= 8736,
            "GFX100S frame is smaller than the advertised ASCOM image");
        Require(area.width == 11662 && area.height == 8752,
            "LibRaw active pixels were discarded before ASCOM conversion");
        Require(area.left == 0 && area.top == 2, "GFX100S origin changed");

        // Exact-size frames must retain their last active columns and row.
        sizes.raw_width = 12;
        sizes.raw_height = 10;
        sizes.width = 8;
        sizes.height = 6;
        sizes.left_margin = 2;
        sizes.top_margin = 2;
        Require(TryGetNativeBayerArea(sizes, area), "Valid active rectangle rejected");
        Require(area.width == 8 && area.height == 6 && area.left == 2 && area.top == 2,
            "Active-area dimensions or origin changed");

        sizes.width = 11;
        Require(TryGetNativeBayerArea(sizes, area) && area.width == 10,
            "Odd active width must retain complete Bayer pairs");
        sizes.width = 12;
        Require(!TryGetNativeBayerArea(sizes, area), "Out-of-bounds width accepted");
        sizes.width = 8;
        sizes.height = 9;
        Require(!TryGetNativeBayerArea(sizes, area), "Out-of-bounds height accepted");
        sizes.height = 0;
        Require(!TryGetNativeBayerArea(sizes, area), "Empty image accepted");
        sizes.height = 6;
        sizes.raw_width = 0;
        Require(!TryGetNativeBayerArea(sizes, area), "Missing raw buffer dimensions accepted");
        std::cout << "All native Bayer active-area checks passed.\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
