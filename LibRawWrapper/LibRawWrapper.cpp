#include "pch.h" // Standard precompiled header for C++/CLI projects

// Ensure standard C headers are included AFTER pch.h if needed
#include <string.h> // For memcpy_s
#include <vcruntime_string.h> // Alternative for memcpy_s if string.h doesn't work alone

// Include marshal helper for pinning managed arrays
#include <msclr/marshal.h>
#include <msclr/marshal_cppstd.h> // If needed for string conversions

// Include LibRaw headers for type definitions (structs, enums)
#include "libraw_types.h"
#include "libraw_const.h"
// #include "libraw.h" // Keep commented unless absolutely necessary

// Include the header for the managed class definition
#include "LibRawWrapper.h"
#include "NativeBayerArea.h"


// Use namespaces at the top level for clarity
using namespace System;
using namespace System::Runtime::InteropServices; // Needed for DllImport, OutAttribute, etc.
using namespace Fujifilm::LibRawWrapper; // Use the namespace defined in the header

// --- Explicit P/Invoke declarations for LibRaw C API ---
// Using a nested internal static class for P/Invokes is a common C# pattern,
// but in C++/CLI, declaring them directly like this is also fine.
// We keep extern "C" as it explicitly declares C linkage for the imported functions.
extern "C" {
    [DllImport("libraw.dll", CallingConvention = CallingConvention::Cdecl)]
    libraw_data_t* libraw_init(unsigned int flags);

    [DllImport("libraw.dll", CallingConvention = CallingConvention::Cdecl)]
    int libraw_open_buffer(libraw_data_t* lr, const void* buffer, size_t size);

    [DllImport("libraw.dll", CallingConvention = CallingConvention::Cdecl)]
    int libraw_unpack(libraw_data_t* lr);

    [DllImport("libraw.dll", CallingConvention = CallingConvention::Cdecl)]
    int libraw_dcraw_process(libraw_data_t* lr);

    [DllImport("libraw.dll", CallingConvention = CallingConvention::Cdecl)]
    libraw_processed_image_t* libraw_dcraw_make_mem_image(libraw_data_t* lr, int* errorCode);

    [DllImport("libraw.dll", CallingConvention = CallingConvention::Cdecl)]
    void libraw_dcraw_clear_mem(libraw_processed_image_t* image);

    [DllImport("libraw.dll", CallingConvention = CallingConvention::Cdecl)]
    void libraw_close(libraw_data_t* lr);

    // Add other needed LibRaw C functions here using the same pattern
    // e.g., [DllImport("libraw.dll", CallingConvention = CallingConvention::Cdecl)]
    //       const char* libraw_strerror(int errorcode);
} // extern "C"


/// <summary>
/// Processes a raw image buffer using LibRaw and extracts the Bayer data.
/// </summary>
int RawProcessor::ProcessRawBuffer(
    // Explicitly qualify .NET types
    array<System::Byte>^ rawBuffer,
    [Out] array<System::UInt16, 2>^% bayerData,
    [Out] int% width,
    [Out] int% height)
{
    // Initialize output parameters
    bayerData = nullptr;
    width = 0;
    height = 0;

    // Check input buffer
    if (rawBuffer == nullptr || rawBuffer->Length == 0)
    {
        // Use a specific LibRaw error if appropriate, otherwise a general one
        return LIBRAW_UNSPECIFIED_ERROR; // Or perhaps LIBRAW_IO_ERROR
    }

    // LibRaw data structure pointer (native)
    libraw_data_t* lr = nullptr;
    // Use defined LibRaw success code
    int ret = LIBRAW_SUCCESS;

    // Pin the managed byte array to get a native pointer
    // Using pin_ptr handles pinning and unpinning automatically within its scope.
    pin_ptr<System::Byte> pinnedRawBuffer = &rawBuffer[0];
    // Cast to the type expected by libraw_open_buffer
    const void* nativeBufferPtr = static_cast<const void*>(pinnedRawBuffer);
    size_t bufferSize = (size_t)rawBuffer->Length;

    try
    {
        // 1. Initialize LibRaw (using direct P/Invoke call)
        lr = libraw_init(0); // Flags = 0
        if (!lr)
        {
            // Consider a more specific error if possible, e.g., LIBRAW_UNSUFFICIENT_MEMORY
            return LIBRAW_UNSPECIFIED_ERROR;
        }

        // 2. Open the buffer (using direct P/Invoke call)
        ret = libraw_open_buffer(lr, nativeBufferPtr, bufferSize);
        if (ret != LIBRAW_SUCCESS)
        {
            libraw_close(lr); // Clean up on error
            lr = nullptr; // Prevent double close in finally
            return ret; // Return the specific LibRaw error
        }

        // 3. Unpack the raw data (using direct P/Invoke call)
        ret = libraw_unpack(lr);
        if (ret != LIBRAW_SUCCESS)
        {
            libraw_close(lr); // Clean up on error
            lr = nullptr;
            return ret; // Return the specific LibRaw error
        }

        // X-Trans cannot be represented through ASCOM's 2x2 Bayer sensor types. Match the NINA
        // plugin's compatibility path: let LibRaw demosaic the 6x6 CFA, then sample the resulting
        // RGB pixels into a synthetic RGGB mosaic. GFX Bayer bodies continue down the native path.
        if (lr->idata.filters == 9)
        {
            lr->params.output_bps = 16;
            lr->params.use_camera_wb = 1;
            lr->params.no_auto_bright = 1;
            lr->params.output_color = 1; // sRGB
            lr->params.user_flip = 0;

            ret = libraw_dcraw_process(lr);
            if (ret != LIBRAW_SUCCESS) return ret;

            libraw_processed_image_t* processed = libraw_dcraw_make_mem_image(lr, &ret);
            if (!processed || ret != LIBRAW_SUCCESS)
            {
                if (processed) libraw_dcraw_clear_mem(processed);
                return ret == LIBRAW_SUCCESS ? LIBRAW_DATA_ERROR : ret;
            }

            try
            {
                if (processed->type != LIBRAW_IMAGE_BITMAP || processed->colors < 3 || processed->bits != 16)
                    return LIBRAW_DATA_ERROR;

                // The processed bitmap already uses LibRaw's active image area.
                // Its width can exceed the camera's advertised size by a small border.
                int sourceWidth = processed->width;
                width = sourceWidth;
                if ((width & 1) != 0) --width;
                height = processed->height;
                if (width <= 0 || height <= 0) return LIBRAW_DATA_ERROR;

                bayerData = gcnew array<System::UInt16, 2>(height, width);
                const unsigned short* rgb = reinterpret_cast<const unsigned short*>(processed->data);
                int colors = processed->colors;
                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        int channel;
                        if ((y & 1) == 0 && (x & 1) == 0) channel = 0;      // R
                        else if ((y & 1) == 1 && (x & 1) == 1) channel = 2; // B
                        else channel = 1;                                   // G
                        bayerData[y, x] = rgb[((size_t)y * sourceWidth + x) * colors + channel];
                    }
                }
            }
            finally
            {
                libraw_dcraw_clear_mem(processed);
            }
            return LIBRAW_SUCCESS;
        }

        // 4. Get dimensions (native Bayer path)
        // Check if rawdata is valid before accessing sizes
        if (!lr->rawdata.sizes.raw_width || !lr->rawdata.sizes.raw_height) {
            ret = LIBRAW_DATA_ERROR;
            libraw_close(lr);
            lr = nullptr;
            return ret;
        }
        int sourceWidth = lr->sizes.raw_width;
        NativeBayerArea area;
        if (!TryGetNativeBayerArea(lr->sizes, area))
        {
            ret = LIBRAW_DATA_ERROR;
            libraw_close(lr);
            lr = nullptr;
            return ret;
        }
        int left = area.left;
        int top = area.top;
        width = area.width;
        height = area.height;

        // 5. Get pointer to raw Bayer data (Direct struct access is fine)
        // Check if raw_image is valid
        ushort* raw_image_ptr = lr->rawdata.raw_image;
        if (!raw_image_ptr)
        {
            ret = LIBRAW_DATA_ERROR;
            libraw_close(lr);
            lr = nullptr;
            return ret;
        }

        // 6. Create the managed output array
        // Ensure height and width are positive before allocating
        if (height <= 0 || width <= 0)
        {
            ret = LIBRAW_DATA_ERROR; // Or a more specific error
            libraw_close(lr);
            lr = nullptr;
            return ret;
        }
        bayerData = gcnew array<System::UInt16, 2>(height, width);

        // 7. Copy the active sensor area from the native buffer to the managed array.
        pin_ptr<System::UInt16> pinnedBayerData = &bayerData[0, 0];
        size_t totalPixels = (size_t)width * height;

        // Check for potential overflow before calculating bytesToCopy
        if (totalPixels > SIZE_MAX / sizeof(ushort)) {
            ret = LIBRAW_UNSUFFICIENT_MEMORY; // Or TOO_BIG
            libraw_close(lr);
            lr = nullptr;
            return ret;
        }
        size_t rowBytes = (size_t)width * sizeof(ushort);
        for (int y = 0; y < height; ++y)
        {
            ushort* destination = pinnedBayerData + (size_t)y * width;
            const ushort* source = raw_image_ptr + (size_t)(y + top) * sourceWidth + left;
            errno_t memcpy_ret = memcpy_s(destination, rowBytes, source, rowBytes);
            if (memcpy_ret != 0)
            {
                System::Diagnostics::Debug::WriteLine(L"memcpy_s failed with error code: " + memcpy_ret);
                ret = LIBRAW_UNSPECIFIED_ERROR;
                bayerData = nullptr;
                libraw_close(lr);
                lr = nullptr;
                return ret;
            }
        }

        // If we reach here, all LibRaw operations were successful
        ret = LIBRAW_SUCCESS;

    }
    catch (System::Exception^ ex) // Catch managed exceptions (e.g., OutOfMemoryException during array allocation)
    {
        // Log the managed exception details
        System::Diagnostics::Debug::WriteLine(L"Managed exception in RawProcessor::ProcessRawBuffer: " + ex->Message);
        ret = LIBRAW_UNSPECIFIED_ERROR; // Map managed exceptions to a general error
        bayerData = nullptr;
        width = 0;
        height = 0;
        // Ensure cleanup even if an exception occurred before lr was closed
        if (lr) {
            libraw_close(lr);
            lr = nullptr;
        }
    }
    // We don't need a finally block specifically for libraw_close if we close it
    // immediately after errors within the try block and before returning successfully.
    // However, keeping it doesn't hurt if there are complex paths.
    finally
    {
        if (lr) // Ensure close is called if lr is still valid (e.g., exception after unpack but before close)
        {
            libraw_close(lr);
        }
    }

    return ret; // Return the final LibRaw status code
}
