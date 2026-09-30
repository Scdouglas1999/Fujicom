// ASCOM Camera hardware class for ScdouglasFujifilm
// Author: Sean Douglas
// Description: Interfaces with the Fujifilm X SDK to control Fujifilm cameras.
// Implements: ASCOM Camera interface version: 3

using ASCOM;
using ASCOM.Astrometry.AstroUtils;
using ASCOM.DeviceInterface;
using ASCOM.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq; // Added for Min()/Max() extension methods
using System.Runtime.InteropServices; // Needed for GCHandle, Marshal
using System.Threading;
using System.Threading.Tasks; // Added for Task.Run
using System.Windows.Forms;
using ASCOM.ScdouglasFujifilm.Camera.Core;

// Add using for the C++/CLI Wrapper namespace (adjust if you used a different namespace)
using Fujifilm.LibRawWrapper; // Assuming this is your C++/CLI wrapper namespace

namespace ASCOM.ScdouglasFujifilm.Camera
{
    /// <summary>
    /// Wraps the Fujifilm X SDK C-style DLL functions using P/Invoke.
    /// </summary>
    internal static class FujifilmSdkWrapper
    {
        // --- SDK Wrapper code remains the same as user provided file ---
        private const string SdkDllName = "XAPI.dll"; // Core Fuji SDK DLL

        #region SDK Structures (Matching XAPI.H)

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct XSDK_ImageInformation
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string strInternalName;
            public int lFormat;
            public int lDataSize;
            public int lImagePixHeight;
            public int lImagePixWidth;
            public int lImageBitDepth;
            public int lPreviewSize;
            public IntPtr hCamera; // XSDK_HANDLE
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct XSDK_DeviceInformation
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strVendor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strManufacturer;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProduct;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strFirmware;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strDeviceType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strSerialNo;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strFramework;
            public byte bDeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string strDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string strYNo;
        }

        #endregion

        #region SDK Constants (Matching XAPI.H & XAPIOpt.h)

        // Results
        public const int XSDK_COMPLETE = 0;
        public const int XSDK_ERROR = -1;

        // Error Codes (Ensure these match XAPI.H hex values)
        public const int XSDK_ERRCODE_NOERR = 0x0000;
        public const int XSDK_ERRCODE_SEQUENCE = 0x1001;
        public const int XSDK_ERRCODE_PARAM = 0x1002;
        public const int XSDK_ERRCODE_INVALID_CAMERA = 0x1003;
        public const int XSDK_ERRCODE_LOADLIB = 0x1004;
        public const int XSDK_ERRCODE_UNSUPPORTED = 0x1005;
        public const int XSDK_ERRCODE_BUSY = 0x1006;
        public const int XSDK_ERRCODE_AF_TIMEOUT = 0x1007;
        public const int XSDK_ERRCODE_SHOOT_ERROR = 0x1008;
        public const int XSDK_ERRCODE_FRAME_FULL = 0x1009;
        public const int XSDK_ERRCODE_STANDBY = 0x1010;
        public const int XSDK_ERRCODE_NODRIVER = 0x1011;
        public const int XSDK_ERRCODE_NO_MODEL_MODULE = 0x1012;
        public const int XSDK_ERRCODE_API_NOTFOUND = 0x1013;
        public const int XSDK_ERRCODE_API_MISMATCH = 0x1014;
        public const int XSDK_ERRCODE_INVALID_USBMODE = 0x1015;
        public const int XSDK_ERRCODE_FORCEMODE_BUSY = 0x1016;
        public const int XSDK_ERRCODE_RUNNING_OTHER_FUNCTION = 0x1017;
        public const int XSDK_ERRCODE_COMMUNICATION = 0x2001;
        public const int XSDK_ERRCODE_TIMEOUT = 0x2002;
        public const int XSDK_ERRCODE_COMBINATION = 0x2003;
        public const int XSDK_ERRCODE_WRITEERROR = 0x2004;
        public const int XSDK_ERRCODE_CARDFULL = 0x2005;
        public const int XSDK_ERRCODE_HARDWARE = 0x3001;
        public const int XSDK_ERRCODE_INTERNAL = 0x9001;
        public const int XSDK_ERRCODE_MEMFULL = 0x9002;
        public const int XSDK_ERRCODE_UNKNOWN = 0x9100;

        // Priority Modes
        public const int XSDK_PRIORITY_CAMERA = 0x0001;
        public const int XSDK_PRIORITY_PC = 0x0002;

        // Interfaces
        public const int XSDK_DSC_IF_USB = 1;

        // --- REMOVED Hardcoded GFX100S Constants ---
        // Exposure Modes (From XAPI.H & GFX100S.h)
        // public const int GFX100S_MODE_M = 0x0001; // Manual Exposure Mode (Defined in GFX100S.h as API_CODE_SetMode)
        // Focus Modes (From XAPI.H & GFX100S.h)
        // public const int GFX100S_FOCUSMODE_MANUAL = 0x0001; // Manual Focus Mode (Defined in GFX100S.h)
        // Image Quality / Format (Examples - Ensure these match GFX100S.h)
        // public const int GFX100S_IMAGEQUALITY_RAW = 1;          // Corresponds to SDK_IMAGEQUALITY_RAW in GFX100S.h
        // public const int GFX100S_IMAGEQUALITY_FINE = 2;         // Corresponds to SDK_IMAGEQUALITY_FINE in GFX100S.h
        // public const int GFX100S_IMAGEQUALITY_NORMAL = 3;       // Corresponds to SDK_IMAGEQUALITY_NORMAL in GFX100S.h
        // public const int GFX100S_IMAGEQUALITY_RAW_FINE = 4;     // Corresponds to SDK_IMAGEQUALITY_RAW_FINE in GFX100S.h
        // public const int GFX100S_IMAGEQUALITY_RAW_NORMAL = 5;   // Corresponds to SDK_IMAGEQUALITY_RAW_NORMAL in GFX100S.h
        // public const int GFX100S_IMAGEQUALITY_SUPERFINE = 6;    // Corresponds to SDK_IMAGEQUALITY_SUPERFINE in GFX100S.h
        // public const int GFX100S_IMAGEQUALITY_RAW_SUPERFINE = 7;// Corresponds to SDK_IMAGEQUALITY_RAW_SUPERFINE in GFX100S.h
        // --- END REMOVED Hardcoded GFX100S Constants ---


        // Release Modes (From XAPI.h & XAPIOpt.h)
        public const int XSDK_RELEASE_SHOOT = 0x0100; // Just shoot (S2?)
        public const int XSDK_RELEASE_S1ON = 0x0200;   // S1 Press Only (Added from XAPI.h)
        public const int XSDK_RELEASE_N_S1OFF = 0x0004; // Option flag
        public const int XSDK_RELEASE_SHOOT_S1OFF = (XSDK_RELEASE_SHOOT | XSDK_RELEASE_N_S1OFF); // 0x0104 = 260
        // --- Removed incorrect SDK_RELEASE_MODE_* constants ---

        // Bulb Release Modes (From XAPI.h)
        public const int XSDK_RELEASE_BULBS2_ON = 0x0500;  // Correct value from XAPI.h
        public const int XSDK_RELEASE_N_BULBS2OFF = 0x0008; // Correct value from XAPI.h
        // *** CORRECTED: Use the combined constant from XAPI.h for stopping bulb ***
        public const int XSDK_RELEASE_N_BULBS1OFF = (XSDK_RELEASE_N_BULBS2OFF | XSDK_RELEASE_N_S1OFF); // 0x000C
        public const int XSDK_RELEASE_CANCEL = 0x000F;


        // Shutter Speed
        public const int XSDK_SHUTTER_BULB = -1;
        // *** ADDED: Constant for Time (T) mode - Use a distinct value if needed, but SDK likely uses regular codes ***
        // We'll use the longest mapped code instead of a specific T-mode code.
        // public const int XSDK_SHUTTER_TIME = -2; // Example, not a real SDK value

        // Dynamic Range (From SDK PDF p.142)
        public const int XSDK_DRANGE_AUTO = 0xFFFF;
        public const int XSDK_DRANGE_100 = 100;
        public const int XSDK_DRANGE_200 = 200;
        public const int XSDK_DRANGE_400 = 400;
        public const int XSDK_DRANGE_800 = 800;

        // RAW Compression (From XAPIOpt.h)
        public const int SDK_RAW_COMPRESSION_OFF = 0x0001;
        public const int SDK_RAW_COMPRESSION_LOSSLESS = 0x0002;
        public const int SDK_RAW_COMPRESSION_LOSSY = 0x0003;
        private const int API_CODE_SetImageQuality = 0x2129;
        private const int API_CODE_GetImageQuality = 0x2130;
        private const int API_CODE_SetRAWCompression = 0x2150;
        private const int API_CODE_GetRAWCompression = 0x2151;
        private const int API_PARAM_SINGLE_VALUE = 1;

        #endregion

        #region Fujifilm SDK P/Invoke Signatures (Matching XAPI.H)

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_Init")]
        public static extern int XSDK_Init(IntPtr hLib);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_Exit")]
        public static extern int XSDK_Exit();

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_Detect")]
        public static extern int XSDK_Detect(int lInterface, IntPtr pInterface, IntPtr pDeviceName, out int plCount);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_OpenEx")]
        public static extern int XSDK_OpenEx([MarshalAs(UnmanagedType.LPStr)] string pDevice, out IntPtr phCamera, out int plCameraMode, IntPtr pOption);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_Close")]
        public static extern int XSDK_Close(IntPtr hCamera);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_GetErrorNumber")]
        public static extern int XSDK_GetErrorNumber(IntPtr hCamera, out int plAPICode, out int plERRCode);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_GetDeviceInfoEx")]
        public static extern int XSDK_GetDeviceInfoEx(IntPtr hCamera, out XSDK_DeviceInformation pDevInfo, out int plNumAPICode, IntPtr plAPICode);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_SetPriorityMode")]
        public static extern int XSDK_SetPriorityMode(IntPtr hCamera, int lPriorityMode);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_GetPriorityMode")]
        public static extern int XSDK_GetPriorityMode(IntPtr hCamera, out int plPriorityMode);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_SetMode")]
        public static extern int XSDK_SetMode(IntPtr hCamera, int lMode);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_GetMode")]
        public static extern int XSDK_GetMode(IntPtr hCamera, out int plMode);

        // *** ADDED XSDK_GetAEMode for diagnostic check ***
        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_GetAEMode")]
        public static extern int XSDK_GetAEMode(IntPtr hCamera, out int plAEMode);
        // *** END ADDED ***

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_CapSensitivity")]
        public static extern int XSDK_CapSensitivity(IntPtr hCamera, ref int plNumSensitivity, IntPtr plSensitivity);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_SetSensitivity")]
        public static extern int XSDK_SetSensitivity(IntPtr hCamera, int lSensitivity);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_GetSensitivity")]
        public static extern int XSDK_GetSensitivity(IntPtr hCamera, out int plSensitivity);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_CapShutterSpeed")]
        public static extern int XSDK_CapShutterSpeed(IntPtr hCamera, out int plNumShutterSpeed, IntPtr plShutterSpeed, out int plBulbCapable);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_SetShutterSpeed")]
        public static extern int XSDK_SetShutterSpeed(IntPtr hCamera, int lShutterSpeed, int lBulb);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_GetShutterSpeed")]
        public static extern int XSDK_GetShutterSpeed(IntPtr hCamera, out int plShutterSpeed, out int plBulb);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_Release")]
        public static extern int XSDK_Release(IntPtr hCamera, int lReleaseMode, IntPtr plShotOpt, out int pStatus);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_ReadImageInfo")]
        public static extern int XSDK_ReadImageInfo(IntPtr hCamera, out XSDK_ImageInformation pImgInfo);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_ReadImage")]
        public static extern int XSDK_ReadImage(IntPtr hCamera, IntPtr pData, uint ulDataSize);

        // *** ADDED XSDK_DeleteImage ***
        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_DeleteImage")]
        public static extern int XSDK_DeleteImage(IntPtr hCamera);
        // *** END ADDED ***

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_GetBufferCapacity")]
        public static extern int XSDK_GetBufferCapacity(IntPtr hCamera, out int plShootFrameNum, out int plTotalFrameNum);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_SetProp")]
        private static extern int XSDK_SetProp(IntPtr hCamera, int apiCode, int apiParameter, int value);

        [DllImport(SdkDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "XSDK_GetProp")]
        private static extern int XSDK_GetProp(IntPtr hCamera, int apiCode, int apiParameter, out int value);

        public static int XSDK_SetImageQuality(IntPtr hCamera, int value) =>
            XSDK_SetProp(hCamera, API_CODE_SetImageQuality, API_PARAM_SINGLE_VALUE, value);

        public static int XSDK_GetImageQuality(IntPtr hCamera, out int value) =>
            XSDK_GetProp(hCamera, API_CODE_GetImageQuality, API_PARAM_SINGLE_VALUE, out value);

        public static int XSDK_SetRAWCompression(IntPtr hCamera, int value) =>
            XSDK_SetProp(hCamera, API_CODE_SetRAWCompression, API_PARAM_SINGLE_VALUE, value);

        public static int XSDK_GetRAWCompression(IntPtr hCamera, out int value) =>
            XSDK_GetProp(hCamera, API_CODE_GetRAWCompression, API_PARAM_SINGLE_VALUE, out value);

        #endregion

        #region Helper Methods
        // --- Helper methods remain the same as user provided file ---
        internal delegate int CapFunctionDelegate(IntPtr hCamera, out int count, IntPtr buffer);
        internal delegate int CapFunctionBulbDelegate(IntPtr hCamera, out int count, IntPtr buffer, out int bulbCapable);

        internal static int[] GetIntArrayFromSdk(IntPtr hCamera, CapFunctionDelegate capFunc)
        {
            int count = 0;
            // First call to get the count
            int result = capFunc(hCamera, out count, IntPtr.Zero);
            CheckSdkError(hCamera, result, "GetIntArrayFromSdk (GetCount)");
            if (count <= 0) return new int[0];

            IntPtr buffer = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal(count * sizeof(int));
                // Second call to get the data
                result = capFunc(hCamera, out count, buffer); // Count might change, but buffer size is fixed
                CheckSdkError(hCamera, result, "GetIntArrayFromSdk (GetData)");

                int[] managedArray = new int[count];
                Marshal.Copy(buffer, managedArray, 0, count);
                return managedArray;
            }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        }

        internal static int[] GetIntArrayFromSdkShutterSpeed(IntPtr hCamera, out int bulbCapable)
        {
            int count = 0;
            bulbCapable = 0; // Default to false
                             // First call to get the count and bulb capability
            int result = FujifilmSdkWrapper.XSDK_CapShutterSpeed(hCamera, out count, IntPtr.Zero, out bulbCapable);
            CheckSdkError(hCamera, result, "GetIntArrayFromSdkShutterSpeed (GetCount)");
            if (count <= 0) return new int[0];

            IntPtr buffer = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal(count * sizeof(int));
                // Second call to get the data (bulbCapable is already retrieved)
                result = FujifilmSdkWrapper.XSDK_CapShutterSpeed(hCamera, out count, buffer, out _); // Ignore bulbCapable on second call
                CheckSdkError(hCamera, result, "GetIntArrayFromSdkShutterSpeed (GetData)");

                int[] managedArray = new int[count];
                Marshal.Copy(buffer, managedArray, 0, count);
                return managedArray;
            }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        }

        // *** MODIFIED: Use carefully managed temporary pointer for first call ***
        internal static int[] GetIntArrayFromSdkSensitivity(IntPtr hCamera)
        {
            int count = 0; // Use int for count
            IntPtr dataPtr = IntPtr.Zero; // Pointer for the second call
            int result = -1; // Initialize result

            try
            {
                // --- First call: Get Count (using IntPtr.Zero) ---
                LogMessageStatic("GetIntArrayFromSdkSensitivity", "Calling XSDK_CapSensitivity(GetCount)...");
                result = XSDK_CapSensitivity(hCamera, ref count, IntPtr.Zero);
                LogMessageStatic("GetIntArrayFromSdkSensitivity", $"XSDK_CapSensitivity (GetCount) returned count={count}, result={result}");

                // Check result of the first call
                if (result != XSDK_COMPLETE)
                {
                    CheckSdkError(hCamera, result, "XSDK_CapSensitivity (GetCount)");
                    return new int[0]; // Return empty on error
                }
                if (count <= 0)
                {
                    LogMessageStatic("GetIntArrayFromSdkSensitivity", $"No sensitivities reported (count={count}).");
                    return new int[0];
                }

                // --- Second call: Get Data ---
                // Allocate the correctly sized buffer for the actual data (array of ints, matching 32-bit long)
                dataPtr = Marshal.AllocHGlobal(sizeof(int) * count);
                LogMessageStatic("GetIntArrayFromSdkSensitivity", $"Calling XSDK_CapSensitivity(GetData into ptr {dataPtr})...");
                int countCheck = count;
                result = XSDK_CapSensitivity(hCamera, ref countCheck, dataPtr);
                LogMessageStatic("GetIntArrayFromSdkSensitivity", $"XSDK_CapSensitivity (GetData) returned result={result}, countCheck={countCheck}");

                // Check result of the second call
                if (result != XSDK_COMPLETE)
                {
                    CheckSdkError(hCamera, result, "XSDK_CapSensitivity (GetData)");
                    return new int[0]; // Return empty on error
                }

                // Optional: Check if count changed between calls
                if (countCheck != count)
                {
                    LogMessageStatic("GetIntArrayFromSdkSensitivity", $"Warning: Sensitivity count changed between calls ({count} -> {countCheck}). Using original count.");
                    // Stick with the original count for buffer allocation consistency.
                    if (countCheck <= 0 || count <= 0) return new int[0]; // Return empty if count became zero or was already zero
                    count = Math.Min(count, countCheck); // Use the smaller count to avoid reading past buffer
                }

                // Copy data from unmanaged buffer (array of ints) to managed int array
                int[] array = new int[count];
                Marshal.Copy(dataPtr, array, 0, count); // Marshal.Copy works with int count

                LogMessageStatic("GetIntArrayFromSdkSensitivity", $"Successfully copied {count} sensitivities.");
                return array;
            }
            finally
            {
                // Ensure data pointer is freed if allocated
                if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            }
        }
        #endregion

        #region Error Handling Helper
        // --- Error handling remains the same as user provided file ---
        public static void CheckSdkError(IntPtr hCamera, int sdkResult, string operation)
        {
            if (sdkResult != XSDK_COMPLETE)
            {
                int apiCode = 0, errCode = 0;
                try
                {
                    if (hCamera != IntPtr.Zero)
                    {
                        XSDK_GetErrorNumber(hCamera, out apiCode, out errCode);
                    }
                    else
                    {
                        switch (sdkResult)
                        {
                            case XSDK_ERRCODE_COMMUNICATION:
                            case XSDK_ERRCODE_TIMEOUT:
                            case XSDK_ERRCODE_PARAM:
                                errCode = sdkResult; break;
                            default: errCode = XSDK_ERRCODE_UNKNOWN; break;
                        }
                    }
                }
                catch { errCode = sdkResult; } // Fallback

                // Convert error code to string name using if-else if (C# 7.3 compatible)
                string errCodeName;
                if (errCode == XSDK_ERRCODE_NOERR) errCodeName = "NOERR";
                else if (errCode == XSDK_ERRCODE_SEQUENCE) errCodeName = "SEQUENCE";
                else if (errCode == XSDK_ERRCODE_PARAM) errCodeName = "PARAM";
                else if (errCode == XSDK_ERRCODE_INVALID_CAMERA) errCodeName = "INVALID_CAMERA";
                else if (errCode == XSDK_ERRCODE_LOADLIB) errCodeName = "LOADLIB";
                else if (errCode == XSDK_ERRCODE_UNSUPPORTED) errCodeName = "UNSUPPORTED";
                else if (errCode == XSDK_ERRCODE_BUSY) errCodeName = "BUSY";
                else if (errCode == XSDK_ERRCODE_AF_TIMEOUT) errCodeName = "AF_TIMEOUT";
                else if (errCode == XSDK_ERRCODE_SHOOT_ERROR) errCodeName = "SHOOT_ERROR";
                else if (errCode == XSDK_ERRCODE_FRAME_FULL) errCodeName = "FRAME_FULL";
                else if (errCode == XSDK_ERRCODE_STANDBY) errCodeName = "STANDBY";
                else if (errCode == XSDK_ERRCODE_NODRIVER) errCodeName = "NODRIVER";
                else if (errCode == XSDK_ERRCODE_NO_MODEL_MODULE) errCodeName = "NO_MODEL_MODULE";
                else if (errCode == XSDK_ERRCODE_API_NOTFOUND) errCodeName = "API_NOTFOUND";
                else if (errCode == XSDK_ERRCODE_INVALID_USBMODE) errCodeName = "INVALID_USBMODE";
                else if (errCode == XSDK_ERRCODE_FORCEMODE_BUSY) errCodeName = "FORCEMODE_BUSY";
                else if (errCode == XSDK_ERRCODE_RUNNING_OTHER_FUNCTION) errCodeName = "RUNNING_OTHER_FUNCTION";
                else if (errCode == XSDK_ERRCODE_COMMUNICATION) errCodeName = "COMMUNICATION";
                else if (errCode == XSDK_ERRCODE_TIMEOUT) errCodeName = "TIMEOUT";
                else if (errCode == XSDK_ERRCODE_COMBINATION) errCodeName = "COMBINATION";
                else if (errCode == XSDK_ERRCODE_WRITEERROR) errCodeName = "WRITEERROR";
                else if (errCode == XSDK_ERRCODE_CARDFULL) errCodeName = "CARDFULL";
                else if (errCode == XSDK_ERRCODE_HARDWARE) errCodeName = "HARDWARE";
                else if (errCode == XSDK_ERRCODE_INTERNAL) errCodeName = "INTERNAL";
                else if (errCode == XSDK_ERRCODE_MEMFULL) errCodeName = "MEMFULL";
                else errCodeName = $"UNKNOWN ({errCode:X})"; // Show hex if unknown

                string errorMessage = $"Fujifilm SDK Error during '{operation}'. SDK Result: {sdkResult}, Last API Code: {apiCode:X}, Last Error Code: {errCode} ({errCodeName})";
                LogMessageStatic("CheckSdkError", errorMessage);

                // Throw appropriate ASCOM exception based on the error code
                if (errCode == XSDK_ERRCODE_BUSY) throw new ASCOM.InvalidOperationException($"{errorMessage} (Camera Busy)");
                else if (errCode == XSDK_ERRCODE_COMMUNICATION || errCode == XSDK_ERRCODE_TIMEOUT) throw new ASCOM.NotConnectedException($"{errorMessage} (Communication Error/Timeout)");
                else if (errCode == XSDK_ERRCODE_UNSUPPORTED) throw new ASCOM.MethodNotImplementedException($"{errorMessage} (Unsupported Operation)");
                else if (errCode == XSDK_ERRCODE_PARAM) throw new ASCOM.InvalidValueException($"{errorMessage} (Invalid Parameter)");
                // Add more specific exceptions if needed
                else throw new ASCOM.DriverException(errorMessage); // General driver exception for others
            }
        }


        private static void LogMessageStatic(string identifier, string message)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {identifier}: {message}");
            // Try to log to ASCOM trace logger if CameraHardware is available
            try { CameraHardware.LogMessage(identifier, message); } catch { }
        }

        #endregion
    }

    /// <summary>
    /// ASCOM Camera hardware class for ScdouglasFujifilm.
    /// Static class containing the shared hardware control logic.
    /// </summary>
    [HardwareClass()]
    internal static class CameraHardware
    {
        #region Constants and Fields
        // --- Fields remain the same as user provided file ---
        internal const string traceStateProfileName = "Trace Level";
        internal const string traceStateDefault = "true";
        internal const string cameraNameProfileName = "Camera Name";
        internal const string cameraNameDefault = "";
        private static string DriverProgId = "";
        private static string DriverDescription = "";
        internal static string cameraName = cameraNameDefault;
        private static bool connectedState;
        private static bool sdkInitialized = false;
        private static IntPtr hCamera = IntPtr.Zero;
        private static object hardwareLock = new object();
        private static int connectedClientCount;
        private static bool runOnce = false;
        internal static CameraConfig currentConfig = null; // Keep this null initially
        internal static Util utilities;
        internal static AstroUtils astroUtilities;
        internal static TraceLogger tl;
        private static CameraStates cameraState = CameraStates.cameraIdle;
        private static int cameraXSize = 11648; // Default, will be updated from JSON/Camera
        private static int cameraYSize = 8736;  // Default, will be updated from JSON/Camera
        private static double pixelSizeX = 3.76; // Default, will be updated from JSON/Camera
        private static double pixelSizeY = 3.76; // Default, will be updated from JSON/Camera
        private static int maxAdu = 65535;      // Default, will be updated from JSON/Camera
        private static bool canAbortExposure = false;
        private static bool canStopExposure = false;
        private static bool canPulseGuide = false;
        private static bool hasShutter = true;
        private static string sensorName = "Unknown";
        private static DateTime exposureStartTime;
        private static double lastExposureDuration;
        private static bool imageReady = false;
        private static System.Threading.Timer exposureTimer;
        // *** CORRECTED: Renamed padlock back to exposureLock ***
        private static readonly object exposureLock = new object(); // Lock for exposure state and image data
        private static List<int> supportedSensitivities = new List<int>(); // *** Will be populated by SDK ***
        private static int minSensitivity = 100; // *** Default, updated by SDK ***
        private static int maxSensitivity = 12800; // *** Default, updated by SDK ***
        private static Dictionary<int, double> sdkShutterSpeedToDuration = new Dictionary<int, double>();
        private static Dictionary<double, int> durationToSdkShutterSpeed = new Dictionary<double, int>();
        private static List<int> supportedShutterSpeeds = new List<int>(); // *** Will be populated by SDK ***
        private static double minExposure = 0.0001; // *** Default, updated by SDK ***
        private static double maxTimedExposure = 60.0;
        private static double maxExposure = 3600.0;
        private static bool bulbCapable = true; // *** Default, updated by SDK ***
        private static object lastImageArray = null;
        private static bool activeExposureIsBulb;
        private static int exposureGeneration;
        #endregion

        #region Initialisation and Dispose
        // --- Init and Dispose remain the same as user provided file ---
        static CameraHardware()
        {
            try
            {
                tl = new TraceLogger("", "ScdouglasFujifilm.Hardware");
                LogMessage("CameraHardware", $"Static initialiser created TraceLogger.");
            }
            catch (Exception ex) { Debug.WriteLine($"Static Initialisation Exception creating TraceLogger: {ex}"); }
        }
        internal static void InitialiseHardware()
        {
            lock (hardwareLock)
            {
                if (string.IsNullOrEmpty(DriverProgId))
                {
                    try
                    {
                        DriverProgId = Camera.DriverProgId;
                        DriverDescription = Camera.DriverDescription;
                        ReadProfile();
                        LogMessage("InitialiseHardware", $"ProgID set: {DriverProgId}. Profile read. Trace State: {tl?.Enabled}");
                    }
                    catch (Exception ex) { LogMessage("InitialiseHardware", $"Exception setting ProgID/reading profile: {ex.Message}"); }
                }

                if (!runOnce)
                {
                    LogMessage("InitialiseHardware", $"Starting one-off initialisation.");
                    try
                    {
                        utilities = new Util();
                        astroUtilities = new AstroUtils();
                        connectedState = false;
                        hCamera = IntPtr.Zero;
                        sdkInitialized = false;
                        LogMessage("InitialiseHardware", "One-off initialisation complete.");
                        runOnce = true;
                    }
                    catch (Exception ex) { LogMessage("InitialiseHardware", $"One-off Initialisation Exception: {ex}"); }
                }
                else { LogMessage("InitialiseHardware", "Skipping one-off initialisation (already run)."); }
            }
        }
        public static void Dispose()
        {
            lock (hardwareLock)
            {
                LogMessage("Dispose", $"Disposing CameraHardware resources.");
                if (Connected) { try { Connected = false; } catch (Exception ex) { LogMessage("Dispose", $"Exception during disconnect in Dispose: {ex.Message}"); } }
                if (sdkInitialized) { try { FujifilmSdkWrapper.XSDK_Exit(); sdkInitialized = false; } catch (Exception ex) { LogMessage("Dispose", $"Exception during XSDK_Exit: {ex.Message}"); } }
                utilities?.Dispose(); utilities = null;
                astroUtilities?.Dispose(); astroUtilities = null;
                exposureTimer?.Dispose(); exposureTimer = null;
                if (tl != null) { tl.Enabled = false; tl.Dispose(); tl = null; }
                LogMessage("Dispose", $"CameraHardware disposal complete.");
            }
        }
        #endregion

        #region ASCOM Common Properties and Methods
        // --- Common properties remain the same as user provided file ---
        public static void SetupDialog()
        {
            if (IsConnected) { MessageBox.Show("Already connected, settings cannot be changed.", "ScdouglasFujifilm Setup", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (SetupDialogForm F = new SetupDialogForm(tl)) { if (F.ShowDialog() == DialogResult.OK) { WriteProfile(); } ReadProfile(); }
        }
        public static ArrayList SupportedActions => new ArrayList();
        public static string Action(string actionName, string actionParameters) { LogMessage("Action", $"Action {actionName} not implemented."); throw new ActionNotImplementedException($"Action {actionName} is not implemented by this driver"); }
        public static void CommandBlind(string command, bool raw) { CheckConnected("CommandBlind"); throw new MethodNotImplementedException($"CommandBlind - Command:{command}, Raw: {raw}"); }
        public static bool CommandBool(string command, bool raw) { CheckConnected("CommandBool"); throw new MethodNotImplementedException($"CommandBool - Command:{command}, Raw: {raw}"); }
        public static string CommandString(string command, bool raw) { CheckConnected("CommandString"); throw new MethodNotImplementedException($"CommandString - Command:{command}, Raw: {raw}"); }

        public static bool Connected
        {
            get { lock (hardwareLock) { LogMessage("Connected Get", IsConnected.ToString()); return IsConnected; } }
            set
            {
                lock (hardwareLock)
                {
                    if (value == IsConnected) { LogMessage("Connected Set", $"Already in state: {value}"); return; }

                    if (value) // Connect
                    {
                        LogMessage("Connected Set", "Attempting to connect hardware...");
                        hCamera = IntPtr.Zero;
                        sensorName = "Unknown";
                        currentConfig = null; // Reset config on connect attempt
                        IntPtr apiCodeBufferPtr = IntPtr.Zero; // <<<< Pointer for API code buffer
                        try
                        {
                            LogMessage("Connected Set", "Step 1: Initializing SDK (if needed)...");
                            if (!sdkInitialized)
                            {
                                int initResult = FujifilmSdkWrapper.XSDK_Init(IntPtr.Zero);
                                FujifilmSdkWrapper.CheckSdkError(IntPtr.Zero, initResult, "XSDK_Init");
                                sdkInitialized = true;
                                LogMessage("Connected Set", "SDK Initialized.");
                            }
                            else { LogMessage("Connected Set", "SDK already initialized."); }

                            LogMessage("Connected Set", "Step 2: Detecting cameras...");
                            int cameraCount;
                            int detectResult = FujifilmSdkWrapper.XSDK_Detect(FujifilmSdkWrapper.XSDK_DSC_IF_USB, IntPtr.Zero, IntPtr.Zero, out cameraCount);
                            FujifilmSdkWrapper.CheckSdkError(IntPtr.Zero, detectResult, "XSDK_Detect");
                            LogMessage("Connected Set", $"Detected {cameraCount} camera(s).");
                            if (cameraCount <= 0) throw new ASCOM.NotConnectedException("No Fujifilm cameras detected via USB.");

                            string deviceId = "ENUM:0"; // TODO: Implement camera selection based on 'cameraName' profile setting.
                            LogMessage("Connected Set", $"Step 3: Opening camera session for '{deviceId}'...");
                            int openResult = FujifilmSdkWrapper.XSDK_OpenEx(deviceId, out hCamera, out int cameraMode, IntPtr.Zero);
                            FujifilmSdkWrapper.CheckSdkError(IntPtr.Zero, openResult, $"XSDK_OpenEx ({deviceId})");
                            LogMessage("Connected Set", $"Camera session opened. Handle: {hCamera}, Mode: {cameraMode}");
                            if (hCamera == IntPtr.Zero) throw new ASCOM.DriverException("Failed to open camera session (handle is null).");

                            LogMessage("Connected Set", "Step 4: Setting PC Priority Mode...");
                            int priorityResult = FujifilmSdkWrapper.XSDK_SetPriorityMode(hCamera, FujifilmSdkWrapper.XSDK_PRIORITY_PC);
                            FujifilmSdkWrapper.CheckSdkError(hCamera, priorityResult, "XSDK_SetPriorityMode");
                            LogMessage("Connected Set", "PC Priority Mode set.");

                            // --- Get Device Info ---
                            LogMessage("Connected Set", "Step 5: Getting device info...");
                            string detectedModelName = "Unknown Model";
                            try
                            {
                                FujifilmSdkWrapper.XSDK_DeviceInformation deviceInfo;
                                int numApiCodes = 0;
                                int infoResult;

                                // --- Call 1: Get the number of API codes ---
                                LogMessage("Connected Set", $"Calling XSDK_GetDeviceInfoEx (GetCount - Handle: {hCamera})...");
                                infoResult = FujifilmSdkWrapper.XSDK_GetDeviceInfoEx(hCamera, out deviceInfo, out numApiCodes, IntPtr.Zero);
                                LogMessage("Connected Set", $"XSDK_GetDeviceInfoEx (GetCount) returned {infoResult}, numApiCodes={numApiCodes}");
                                FujifilmSdkWrapper.CheckSdkError(hCamera, infoResult, "XSDK_GetDeviceInfoEx (GetCount)");

                                if (numApiCodes < 0) numApiCodes = 0;

                                // --- Allocate buffer for API codes ---
                                int bufferSize = numApiCodes * sizeof(int);
                                if (bufferSize > 0)
                                {
                                    apiCodeBufferPtr = Marshal.AllocHGlobal(bufferSize);
                                    LogMessage("Connected Set", $"Allocated {bufferSize} bytes for {numApiCodes} API codes at {apiCodeBufferPtr}.");
                                }
                                else
                                {
                                    apiCodeBufferPtr = IntPtr.Zero;
                                    LogMessage("Connected Set", "No API codes reported, buffer not allocated.");
                                }

                                // --- Call 2: Get the info struct AND the API codes list ---
                                LogMessage("Connected Set", $"Calling XSDK_GetDeviceInfoEx (GetData - Handle: {hCamera}, Buffer: {apiCodeBufferPtr})...");
                                infoResult = FujifilmSdkWrapper.XSDK_GetDeviceInfoEx(hCamera, out deviceInfo, out numApiCodes, apiCodeBufferPtr);
                                LogMessage("Connected Set", $"XSDK_GetDeviceInfoEx (GetData) returned {infoResult}");
                                FujifilmSdkWrapper.CheckSdkError(hCamera, infoResult, "XSDK_GetDeviceInfoEx (GetData)");

                                // Optional: Read the API codes
                                if (apiCodeBufferPtr != IntPtr.Zero && numApiCodes > 0)
                                {
                                    int[] apiCodes = new int[numApiCodes];
                                    Marshal.Copy(apiCodeBufferPtr, apiCodes, 0, numApiCodes);
                                    LogMessage("Connected Set", $"Retrieved {numApiCodes} API Codes (Example: {apiCodes[0]})"); // Log first code as example
                                }

                                detectedModelName = deviceInfo.strProduct ?? "Unknown Model";
                                sensorName = detectedModelName; // Update sensorName here
                                LogMessage("Connected Set", $"Retrieved Product Name: {sensorName}");
                                LogMessage("Connected Set", $"Serial: {deviceInfo.strSerialNo}, Firmware: {deviceInfo.strFirmware}");
                            }
                            catch (Exception infoEx)
                            {
                                LogMessage("Connected Set", $"Error getting device info: {infoEx.Message}");
                                sensorName = "Fujifilm Camera (Info Error)";
                                throw;
                            }
                            finally
                            {
                                if (apiCodeBufferPtr != IntPtr.Zero)
                                {
                                    Marshal.FreeHGlobal(apiCodeBufferPtr);
                                    LogMessage("Connected Set", $"Freed API code buffer at {apiCodeBufferPtr}.");
                                    apiCodeBufferPtr = IntPtr.Zero;
                                }
                            }
                            // --- End Get Device Info ---

                            // --- Load Configuration based on detected model ---
                            LogMessage("Connected Set", $"Step 5.5: Loading configuration for model '{detectedModelName}'...");
                            LoadConfiguration(detectedModelName); // Load JSON based on model name
                            if (currentConfig == null || currentConfig.SdkConstants == null)
                            {
                                throw new DriverException($"Failed to load or parse configuration for model '{detectedModelName}'. Cannot proceed.");
                            }
                            LogMessage("Connected Set", $"Configuration loaded for {currentConfig.ModelName}.");
                            // --- End Load Configuration ---

                            // Read the current mode for diagnostics, but do not force it here. Some
                            // bodies report a COMBINATION error when mode is changed remotely even
                            // though the physical dial is already set correctly.
                            LogMessage("Connected Set", "Step 5.6: Reading the current exposure mode (the camera must be set to Manual physically)...");
                            try
                            {
                                int currentMode;
                                int getModeResult = FujifilmSdkWrapper.XSDK_GetMode(hCamera, out currentMode);
                                if (getModeResult == FujifilmSdkWrapper.XSDK_COMPLETE)
                                {
                                    LogMessage("Connected Set", $"Current camera exposure mode: {currentMode} (0x{currentMode:X}).");
                                }
                                else
                                {
                                    LogMessage("Connected Set", $"Warning: XSDK_GetMode failed with result {getModeResult}.");
                                }
                            }
                            catch (Exception modeEx)
                            {
                                LogMessage("Connected Set", $"Warning: Exception while reading exposure mode: {modeEx.Message}");
                            }

                            // *** ADDED: Attempt to set RAW Only + Uncompressed ***
                            LogMessage("Connected Set", "Step 5.7: Attempting to set Image Quality and RAW Compression...");
                            try
                            {
                                // Set Image Quality to RAW Only (using value from JSON)
                                int rawOnlyQualityCode = currentConfig.SdkConstants.ImageQualityRaw;
                                LogMessage("Connected Set", $"Setting Image Quality to RAW Only (Code: {rawOnlyQualityCode})...");
                                int iqResult = FujifilmSdkWrapper.XSDK_SetImageQuality(hCamera, rawOnlyQualityCode);
                                FujifilmSdkWrapper.CheckSdkError(hCamera, iqResult, $"XSDK_SetImageQuality(RAW Only - Code: {rawOnlyQualityCode})");
                                LogMessage("Connected Set", "Image Quality set to RAW Only.");

                                // Set RAW Compression to Uncompressed (SDK value 1).
                                int uncompressedCode = FujifilmSdkWrapper.SDK_RAW_COMPRESSION_OFF;
                                LogMessage("Connected Set", $"Setting RAW Compression to Uncompressed (Code: {uncompressedCode})...");
                                int rcResult = FujifilmSdkWrapper.XSDK_SetRAWCompression(hCamera, uncompressedCode);
                                FujifilmSdkWrapper.CheckSdkError(hCamera, rcResult, $"XSDK_SetRAWCompression(Uncompressed - Code: {uncompressedCode})");
                                LogMessage("Connected Set", "RAW Compression set to Uncompressed.");

                                // Optional: Verify settings
                                int currentIQ, currentRC;
                                if (FujifilmSdkWrapper.XSDK_GetImageQuality(hCamera, out currentIQ) == FujifilmSdkWrapper.XSDK_COMPLETE)
                                    LogMessage("Connected Set", $"Verified Image Quality: {currentIQ}");
                                else LogMessage("Connected Set", "Warning: Could not verify Image Quality.");
                                if (FujifilmSdkWrapper.XSDK_GetRAWCompression(hCamera, out currentRC) == FujifilmSdkWrapper.XSDK_COMPLETE)
                                    LogMessage("Connected Set", $"Verified RAW Compression: {currentRC}");
                                else LogMessage("Connected Set", "Warning: Could not verify RAW Compression.");

                            }
                            catch (Exception settingsEx)
                            {
                                // Log error but don't fail connection - user might need to set manually
                                LogMessage("Connected Set", $"WARNING: Failed to set RAW/Uncompressed settings: {settingsEx.Message}. Please check camera settings manually.");
                            }
                            // *** END ADDED ***

                            // --- Removed attempt to Set Focus Mode via SetProp ---
                            LogMessage("Connected Set", "Step 5.8: Skipping Focus Mode set (requires reliable SDK method or manual camera setting).");
                            // --- End Removed Code ---


                            // --- Set connected state TRUE *before* caching capabilities ---
                            connectedState = true;
                            LogMessage("Connected Set", $"State before CacheCameraCapabilities: connectedState={connectedState}, hCamera={hCamera}");
                            // --- End Change ---

                            LogMessage("Connected Set", "Step 6: Caching camera capabilities...");
                            CacheCameraCapabilities(); // This should now run correctly
                            LogMessage("Connected Set", "Capabilities cached.");

                            // connectedState = true; // MOVED EARLIER
                            LogMessage("Connected Set", "Hardware Connected Successfully.");
                        }
                        catch (Exception ex)
                        {
                            LogMessage("Connected Set", $"HARDWARE CONNECTION FAILED: {ex.Message}\n{ex.StackTrace}");
                            if (hCamera != IntPtr.Zero) { try { FujifilmSdkWrapper.XSDK_Close(hCamera); } catch { } hCamera = IntPtr.Zero; }
                            if (apiCodeBufferPtr != IntPtr.Zero) { try { Marshal.FreeHGlobal(apiCodeBufferPtr); } catch { } }
                            connectedState = false; // Ensure state is false on error
                            sensorName = "Unknown";
                            currentConfig = null; // Clear config on error
                            throw;
                        }
                    }
                    else // Disconnect
                    {
                        LogMessage("Connected Set", "Disconnecting hardware...");
                        lock (exposureLock)
                        {
                            exposureGeneration++;
                            exposureTimer?.Dispose();
                            exposureTimer = null;
                            if (cameraState == CameraStates.cameraExposing || cameraState == CameraStates.cameraWaiting)
                            {
                                try { CancelActiveExposure(); } catch (Exception ex) { LogMessage("Connected Set", "Exposure cancellation failed: " + ex.Message); }
                            }
                            cameraState = CameraStates.cameraIdle;
                            imageReady = false;
                            lastImageArray = null;
                        }
                        if (hCamera != IntPtr.Zero)
                        {
                            try
                            {
                                LogMessage("Connected Set", $"Closing camera handle {hCamera}...");
                                int closeResult = FujifilmSdkWrapper.XSDK_Close(hCamera);
                                LogMessage("Connected Set", $"XSDK_Close returned {closeResult}");
                                LogMessage("Connected Set", "Camera session closed.");
                            }
                            catch (Exception ex) { LogMessage("Connected Set", $"Exception during XSDK_Close: {ex.Message}"); }
                            finally
                            {
                                hCamera = IntPtr.Zero;
                                connectedState = false;
                                sensorName = "Unknown";
                                currentConfig = null; // Clear config on disconnect
                                LogMessage("Connected Set", "Hardware Disconnected.");
                            }
                        }
                        else { LogMessage("Connected Set", "Already disconnected (no handle)."); connectedState = false; sensorName = "Unknown"; currentConfig = null; }
                    }
                }
            }
        }

        internal static void AcquireConnection()
        {
            lock (hardwareLock)
            {
                if (connectedClientCount == 0) Connected = true;
                connectedClientCount++;
                LogMessage("AcquireConnection", $"Connected ASCOM clients: {connectedClientCount}");
            }
        }

        internal static void ReleaseConnection()
        {
            lock (hardwareLock)
            {
                if (connectedClientCount == 0) return;
                connectedClientCount--;
                LogMessage("ReleaseConnection", $"Connected ASCOM clients: {connectedClientCount}");
                if (connectedClientCount == 0) Connected = false;
            }
        }


        public static string Description => DriverDescription;
        public static string DriverInfo => $"Fujifilm ASCOM Driver. Version: {DriverVersion}";
        public static string DriverVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(2);
        public static short InterfaceVersion => 3;
        public static string Name => "Fujifilm Camera (ASCOM)";

        #endregion

        #region ASCOM Camera Specific Properties and Methods

        // ... (Properties like AbortExposure, BayerOffsetX/Y, BinX/Y, CCDTemperature, CameraState etc. remain largely unchanged) ...
        public static void AbortExposure()
        {
            lock (exposureLock)
            {
                LogMessage("AbortExposure", $"Request received. Current state: {cameraState}");
                if (cameraState == CameraStates.cameraExposing || cameraState == CameraStates.cameraWaiting)
                {
                    exposureTimer?.Dispose(); exposureTimer = null;
                    CancelActiveExposure();
                    int abortedGeneration = ++exposureGeneration;
                    cameraState = CameraStates.cameraWaiting;
                    imageReady = false;
                    lastImageArray = null;
                    Task.Run(() => DrainAbortedExposure(abortedGeneration));
                }
                else { LogMessage("AbortExposure", "No exposure in progress to abort."); }
            }
        }

        public static short BayerOffsetX => 0;
        public static short BayerOffsetY => 0;
        public static short BinX { get => 1; set { if (value != 1) throw new InvalidValueException("BinX", value.ToString(), "1"); } }
        public static short BinY { get => 1; set { if (value != 1) throw new InvalidValueException("BinY", value.ToString(), "1"); } }
        public static double CCDTemperature => throw new PropertyNotImplementedException("CCDTemperature", false);

        public static CameraStates CameraState
        {
            get { lock (exposureLock) { LogMessage("CameraState Get", cameraState.ToString()); return cameraState; } }
        }

        public static int CameraXSize => cameraXSize;
        public static int CameraYSize => cameraYSize;
        public static bool CanAbortExposure => canAbortExposure;
        public static bool CanAsymmetricBin => false;
        public static bool CanGetCoolerPower => false;
        public static bool CanPulseGuide => canPulseGuide;
        public static bool CanSetCCDTemperature => false;
        public static bool CanStopExposure => canStopExposure;
        public static bool CoolerOn { get => false; set => throw new PropertyNotImplementedException("CoolerOn", true); }
        public static double CoolerPower => 0.0;
        public static double ElectronsPerADU => throw new PropertyNotImplementedException("ElectronsPerADU", false);
        public static double ExposureMax => maxExposure;
        public static double ExposureMin => minExposure;
        public static double ExposureResolution => minExposure;
        public static bool FastReadout { get => false; set => throw new PropertyNotImplementedException("FastReadout", true); }
        public static double FullWellCapacity => throw new PropertyNotImplementedException("FullWellCapacity", false);

        public static short Gain
        {
            get
            {
                CheckConnected("Gain Get");
                // *** ADDED Lock to prevent interference ***
                lock (exposureLock)
                {
                    // Check if an exposure is running; if so, maybe return cached value or throw?
                    // For now, proceed but be aware this might still interfere if SDK is sensitive
                    if (cameraState == CameraStates.cameraExposing || cameraState == CameraStates.cameraDownload)
                    {
                        LogMessage("Gain Get", $"Warning: Getting Gain while camera state is {cameraState}. SDK call might interfere or fail.");
                    }

                    int sdkSensitivity;
                    LogMessage("Gain Get", $"Calling XSDK_GetSensitivity(hCamera={hCamera})...");
                    int result = FujifilmSdkWrapper.XSDK_GetSensitivity(hCamera, out sdkSensitivity);
                    LogMessage("Gain Get", $"XSDK_GetSensitivity returned {result}, sensitivity={sdkSensitivity}");
                    FujifilmSdkWrapper.CheckSdkError(hCamera, result, "XSDK_GetSensitivity");
                    LogMessage("Gain Get", $"SDK Sensitivity: {sdkSensitivity}");
                    // Return the actual SDK value, clamped to the *discovered* min/max
                    return (short)Math.Max(GainMin, Math.Min(GainMax, sdkSensitivity));
                }
            }
            set
            {
                CheckConnected("Gain Set");
                // Validate against the *discovered* min/max
                if (value < GainMin || value > GainMax) throw new InvalidValueException("Gain", value.ToString(), $"Range {GainMin} to {GainMax}");

                // Gain Value mode: a client may send any integer between GainMin and GainMax, so map
                // the request onto the nearest fixed ISO the body actually reports.
                int iso = FujifilmCapabilities.NearestSensitivity(supportedSensitivities, value);
                if (iso != value)
                {
                    LogMessage("Gain Set", $"Requested ISO {value} is not one of the {supportedSensitivities.Count} fixed ISO values reported by the camera. Using nearest supported ISO {iso}.");
                }

                // *** ADDED Lock to prevent interference ***
                lock (exposureLock)
                {
                    // Check if an exposure is running; if so, maybe prevent setting?
                    if (cameraState == CameraStates.cameraExposing || cameraState == CameraStates.cameraDownload)
                    {
                        LogMessage("Gain Set", $"Error: Cannot set Gain while camera state is {cameraState}.");
                        throw new ASCOM.InvalidOperationException($"Cannot set Gain while camera is {cameraState}.");
                    }

                    LogMessage("Gain Set", $"Calling XSDK_SetSensitivity(hCamera={hCamera}, value={iso})...");
                    int result = FujifilmSdkWrapper.XSDK_SetSensitivity(hCamera, iso);
                    LogMessage("Gain Set", $"XSDK_SetSensitivity returned {result}");
                    FujifilmSdkWrapper.CheckSdkError(hCamera, result, "XSDK_SetSensitivity"); // This might throw if SDK fails
                    LogMessage("Gain Set", $"SDK Sensitivity set to: {iso}");
                }
            }
        }

        // ASCOM "Gain Value" mode: Gain is the ISO number and GainMin/GainMax bound it, using the
        // range discovered from the SDK. The Gains index list is deliberately NOT exposed: ICameraV3
        // allows only one gain mode to be active, and clients such as NINA treat a populated Gains
        // list as authoritative and index it with the value of Gain (Gains[800] -> out of range).
        public static short GainMax => (short)maxSensitivity;
        public static short GainMin => (short)minSensitivity;
        public static bool HasShutter => hasShutter;
        public static double HeatSinkTemperature => throw new PropertyNotImplementedException("HeatSinkTemperature", false);

        public static object ImageArray
        {
            get
            {
                CheckConnected("ImageArray Get");
                lock (exposureLock)
                {
                    if (!imageReady) { LogMessage("ImageArray Get", "Error: Image not ready."); throw new InvalidOperationException("Image not ready. Check ImageReady first."); }
                    imageReady = false;
                    cameraState = CameraStates.cameraDownload;
                }

                // RAW decoding can take seconds for a 100 MP frame. Do it without holding the
                // exposure-state lock so CameraState and logging remain responsive.
                DownloadImageData();

                lock (exposureLock)
                {
                    if (lastImageArray == null)
                    {
                        cameraState = CameraStates.cameraError;
                        throw new DriverException("Failed to retrieve image data after download attempt.");
                    }
                    object imageToReturn = lastImageArray;
                    lastImageArray = null;
                    cameraState = CameraStates.cameraIdle;
                    LogMessage("ImageArray Get", "Returning image array.");
                    return imageToReturn;
                }
            }
        }
        public static object ImageArrayVariant => ImageArray;
        public static bool ImageReady
        {
            get { lock (exposureLock) { LogMessage("ImageReady Get", imageReady.ToString()); return imageReady; } }
        }
        public static bool IsPulseGuiding => false;
        public static double LastExposureDuration => lastExposureDuration;
        public static string LastExposureStartTime => exposureStartTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff");
        public static int MaxADU => maxAdu;
        public static short MaxBinX => 1;
        public static short MaxBinY => 1;
        public static int NumX { get => CameraXSize; set => CheckSubframe("NumX", value, CameraXSize); }
        public static int NumY { get => CameraYSize; set => CheckSubframe("NumY", value, CameraYSize); }
        public static int StartX { get => 0; set => CheckSubframe("StartX", value, 0); }
        public static int StartY { get => 0; set => CheckSubframe("StartY", value, 0); }
        public static short PercentCompleted
        {
            get
            {
                lock (exposureLock)
                {
                    if (cameraState == CameraStates.cameraExposing)
                        return FujifilmCapabilities.PercentComplete(exposureStartTime, lastExposureDuration, false, false);
                    if (cameraState == CameraStates.cameraWaiting) return 98;
                    if (cameraState == CameraStates.cameraDownload) return 99;
                    if (imageReady) return 100;
                    return 0;
                }
            }
        }
        public static double PixelSizeX => pixelSizeX;
        public static double PixelSizeY => pixelSizeY;
        public static void PulseGuide(GuideDirections direction, int duration) => throw new MethodNotImplementedException("PulseGuide");
        public static short ReadoutMode { get => 0; set { if (value != 0) throw new InvalidValueException("ReadoutMode", "0", "Only ReadoutMode 0 is supported."); } }
        public static ArrayList ReadoutModes => new ArrayList { "Normal" };
        // Updated SensorType based on user finding for NINA compatibility
        public static SensorType SensorType => SensorType.RGGB;

        public static string SensorName => sensorName;

        public static double SetCCDTemperature { get => throw new PropertyNotImplementedException("SetCCDTemperature", false); set => throw new PropertyNotImplementedException("SetCCDTemperature", true); }

        // *** MODIFIED StartExposure with Corrected Differentiated Bulb/Time Logic ***
        public static void StartExposure(double duration, bool light)
        {
            CheckConnected("StartExposure");

            // Reset from error state if necessary
            lock (exposureLock)
            {
                if (cameraState == CameraStates.cameraError)
                {
                    LogMessage("StartExposure", $"Camera was in error state. Resetting to Idle.");
                    cameraState = CameraStates.cameraIdle;
                    imageReady = false;
                    lastImageArray = null;
                }
            }

            lock (exposureLock)
            {
                if (cameraState != CameraStates.cameraIdle) throw new InvalidOperationException($"Camera not idle. State: {cameraState}");

                LogMessage("StartExposure", $"Request: Duration={duration}s, Light Frame={light}");

                // Check against cached min/max exposure values
                if (duration < minExposure) { LogMessage("StartExposure", $"Requested duration {duration}s is less than minimum {minExposure}s."); throw new InvalidValueException("StartExposure Duration", duration.ToString(), $"Minimum exposure is {minExposure}"); }

                // Determine if Bulb/Time mode is needed based on max *programmable* exposure
                if (duration > maxExposure)
                    throw new InvalidValueException("StartExposure Duration", duration.ToString(), $"Maximum exposure is {maxExposure} seconds");
                bool useSdkBulbSequence = false; // Flag for PASM bulb sequence OR Physical Dial Bulb sequence

                IntPtr shotOptPtr = IntPtr.Zero;
                bool shotOptAllocated = false;

                try
                {
                    // Log Current Camera State
                    try
                    {
                        int currentMode = -1, currentShut = -1, currentBulb = -1, currentIso = -1;
                        int getResult = FujifilmSdkWrapper.XSDK_GetMode(hCamera, out currentMode);
                        if (getResult == FujifilmSdkWrapper.XSDK_COMPLETE) LogMessage("StartExposure", $"GetMode OK: Mode={currentMode}");
                        else LogMessage("StartExposure", $"GetMode FAILED: Result={getResult}");

                        getResult = FujifilmSdkWrapper.XSDK_GetShutterSpeed(hCamera, out currentShut, out currentBulb);
                        if (getResult == FujifilmSdkWrapper.XSDK_COMPLETE) LogMessage("StartExposure", $"GetShutterSpeed OK: Shutter={currentShut}, Bulb={currentBulb}");
                        else LogMessage("StartExposure", $"GetShutterSpeed FAILED: Result={getResult}");

                        getResult = FujifilmSdkWrapper.XSDK_GetSensitivity(hCamera, out currentIso);
                        if (getResult == FujifilmSdkWrapper.XSDK_COMPLETE) LogMessage("StartExposure", $"GetSensitivity OK: ISO={currentIso}");
                        else LogMessage("StartExposure", $"GetSensitivity FAILED: Result={getResult}");

                        LogMessage("StartExposure", $"State Before Exposure: Mode={currentMode}, Shutter={currentShut}, Bulb={currentBulb}, ISO={currentIso}");
                    }
                    catch (Exception stateEx) { LogMessage("StartExposure", $"Warning: Could not get full camera state before exposure: {stateEx.Message}"); }

                    int sdkShutterSpeed = FujifilmCapabilities.SelectShutterCode(
                        sdkShutterSpeedToDuration, duration, bulbCapable);
                    int isBulbFlag = sdkShutterSpeed == FujifilmSdkWrapper.XSDK_SHUTTER_BULB ? 1 : 0;
                    useSdkBulbSequence = isBulbFlag == 1;
                    double actualDuration = useSdkBulbSequence
                        ? duration
                        : sdkShutterSpeedToDuration[sdkShutterSpeed];

                    if (useSdkBulbSequence && !bulbCapable)
                        throw new InvalidValueException("StartExposure Duration", duration.ToString(), "Bulb mode is unavailable.");

                    LogMessage("StartExposure", $"Setting shutter code {sdkShutterSpeed} for {actualDuration:0.########}s (requested {duration:0.########}s, Bulb={isBulbFlag}).");
                    int setResult = FujifilmSdkWrapper.XSDK_ERROR;
                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        setResult = FujifilmSdkWrapper.XSDK_SetShutterSpeed(hCamera, sdkShutterSpeed, isBulbFlag);
                        if (setResult == FujifilmSdkWrapper.XSDK_COMPLETE) break;
                        LogMessage("StartExposure", $"SetShutterSpeed attempt {attempt}/3 returned {setResult}.");
                        if (attempt < 3) Thread.Sleep(250);
                    }
                    if (setResult != FujifilmSdkWrapper.XSDK_COMPLETE)
                    {
                        LogMessage("StartExposure", "The camera rejected software shutter control. Set a physical shutter dial to T and use Manual exposure mode.");
                        FujifilmSdkWrapper.CheckSdkError(hCamera, setResult, $"XSDK_SetShutterSpeed (Bulb={isBulbFlag})");
                    }

                    if (useSdkBulbSequence) Thread.Sleep(100);


                    // --- Allocate shotOptPtr ---
                    shotOptPtr = Marshal.AllocHGlobal(sizeof(int));
                    Marshal.WriteInt32(shotOptPtr, 0);
                    shotOptAllocated = true; // Mark as allocated
                    LogMessage("StartExposure", $"Allocated plShotOpt at {shotOptPtr} with value 0");

                    // --- Trigger Exposure Start ---
                    int releaseModeStart;
                    int releaseResult;
                    int releaseStatus;

                    if (useSdkBulbSequence) // PASM Bulb Mode OR Physical Dial Bulb Mode
                    {
                        // *** Sequence for SDK Bulb: S1ON -> Delay -> BULBS2_ON ***
                        // 1. Press Halfway (S1ON)
                        releaseModeStart = FujifilmSdkWrapper.XSDK_RELEASE_S1ON; // Use 0x0200
                        LogMessage("StartExposure", $"Triggering S1ON via XSDK_Release (Mode: 0x{releaseModeStart:X}, Options Ptr: {shotOptPtr})...");
                        releaseResult = FujifilmSdkWrapper.XSDK_Release(hCamera, releaseModeStart, shotOptPtr, out releaseStatus);
                        LogMessage("StartExposure", $"XSDK_Release (S1ON) returned {releaseResult}, status={releaseStatus}");
                        try { FujifilmSdkWrapper.CheckSdkError(hCamera, releaseResult, "XSDK_Release (S1ON)"); }
                        catch (Exception s1Ex) { LogMessage("StartExposure", $"Error during S1ON: {s1Ex.Message}. Aborting exposure start."); throw; }
                        LogMessage("StartExposure", $"SDK Release command (S1ON) sent successfully.");

                        // Delay between S1ON and BULBS2_ON
                        LogMessage("StartExposure", "Adding increased delay between S1ON and BULBS2_ON...");
                        System.Threading.Thread.Sleep(500); // Use the 500ms delay

                        // 2. Start Bulb (BULBS2_ON)
                        releaseModeStart = FujifilmSdkWrapper.XSDK_RELEASE_BULBS2_ON; // Use 0x0500
                        LogMessage("StartExposure", $"Triggering BULBS2_ON via XSDK_Release (Mode: 0x{releaseModeStart:X}, Options Ptr: {shotOptPtr})...");
                        releaseResult = FujifilmSdkWrapper.XSDK_Release(hCamera, releaseModeStart, shotOptPtr, out releaseStatus);
                        LogMessage("StartExposure", $"XSDK_Release (BULBS2_ON) returned {releaseResult}, status={releaseStatus}");
                        try { FujifilmSdkWrapper.CheckSdkError(hCamera, releaseResult, "XSDK_Release (BULBS2_ON - Start)"); }
                        catch (Exception bulbStartEx)
                        {
                            LogMessage("StartExposure", $"Error during BULBS2_ON: {bulbStartEx.Message}. Attempting to release S1...");
                            try { FujifilmSdkWrapper.XSDK_Release(hCamera, FujifilmSdkWrapper.XSDK_RELEASE_N_S1OFF, IntPtr.Zero, out _); } catch { /* Ignore */ }
                            throw;
                        }
                        LogMessage("StartExposure", $"SDK Release command (BULBS2_ON - Start) sent successfully.");
                    }
                    else // Standard Timed Exposure (Only for PASM cameras)
                    {
                        // *** Sequence for Timed: SHOOT_S1OFF ***
                        releaseModeStart = FujifilmSdkWrapper.XSDK_RELEASE_SHOOT_S1OFF; // Use 0x0104
                        LogMessage("StartExposure", $"Triggering Timed exposure START via XSDK_Release (Mode: 0x{releaseModeStart:X}, Options Ptr: {shotOptPtr})...");
                        releaseResult = FujifilmSdkWrapper.XSDK_Release(hCamera, releaseModeStart, shotOptPtr, out releaseStatus);
                        LogMessage("StartExposure", $"XSDK_Release (Timed Start) returned {releaseResult}, status={releaseStatus}");
                        FujifilmSdkWrapper.CheckSdkError(hCamera, releaseResult, "XSDK_Release (Timed Start)");
                        LogMessage("StartExposure", $"SDK Release command (Timed Start) sent successfully.");
                    }


                    // --- Update State and Start Timer ---
                    cameraState = CameraStates.cameraExposing;
                    activeExposureIsBulb = useSdkBulbSequence;
                    int generation = ++exposureGeneration;
                    exposureStartTime = DateTime.UtcNow;
                    lastExposureDuration = actualDuration;
                    imageReady = false;
                    lastImageArray = null;

                    int exposureMillis = Math.Max(0, (int)Math.Round(actualDuration * 1000));
                    exposureTimer?.Dispose();

                    // Use different timer callbacks based on sequence used
                    if (useSdkBulbSequence) // Timer needed for SDK-controlled bulb (PASM or Physical Dial)
                    {
                        LogMessage("StartExposure", $"Starting BULB timer for {exposureMillis} ms (Callback: OnBulbExposureTimerElapsed).");
                        exposureTimer = new System.Threading.Timer(OnBulbExposureTimerElapsed, generation, exposureMillis, Timeout.Infinite);
                    }
                    else // Standard Timed Exposure (Only PASM)
                    {
                        LogMessage("StartExposure", $"Starting TIMED timer for {exposureMillis} ms (Callback: OnExposureComplete).");
                        exposureTimer = new System.Threading.Timer(OnExposureComplete, generation, exposureMillis, Timeout.Infinite);
                    }
                    LogMessage("StartExposure", $"Exposure timing initiated.");

                }
                catch (Exception ex)
                {
                    LogMessage("StartExposure", $"StartExposure failed: {ex.Message}\n{ex.StackTrace}"); // Added stack trace
                    cameraState = CameraStates.cameraError;
                    // *** Ensure pointer is freed if exception occurs before timer starts ***
                    if (shotOptAllocated && shotOptPtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(shotOptPtr);
                        LogMessage("StartExposure", $"Freed plShotOpt memory due to exception before timer start.");
                        shotOptAllocated = false; // Prevent double-free in finally
                    }
                    throw;
                }
                finally
                {
                    // *** Free allocated shot options memory ***
                    if (shotOptAllocated && shotOptPtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(shotOptPtr);
                        LogMessage("StartExposure", $"Freed allocated plShotOpt memory at {shotOptPtr}");
                    }
                    // *** END Free ***
                }
            }
        }


        public static void StopExposure()
        {
            lock (exposureLock)
            {
                LogMessage("StopExposure", $"Request received. Current state: {cameraState}");
                if (cameraState == CameraStates.cameraExposing)
                {
                    exposureTimer?.Dispose(); exposureTimer = null;
                    CancelActiveExposure();
                    cameraState = CameraStates.cameraWaiting;
                    imageReady = false;
                    int generation = exposureGeneration;
                    Task.Run(() => PollForImage(generation));
                }
                else { LogMessage("StopExposure", "No exposure in progress to stop."); }
            }
        }

        private static void CancelActiveExposure()
        {
            int mode = activeExposureIsBulb
                ? FujifilmSdkWrapper.XSDK_RELEASE_N_BULBS1OFF
                : FujifilmSdkWrapper.XSDK_RELEASE_CANCEL;
            int status;
            int result = FujifilmSdkWrapper.XSDK_Release(hCamera, mode, IntPtr.Zero, out status);
            LogMessage("CancelActiveExposure", $"Release mode 0x{mode:X} returned {result}, status={status}.");
            activeExposureIsBulb = false;
        }

        private static async Task DrainAbortedExposure(int generation)
        {
            int discarded = 0;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                if (!IsConnected) return;
                try
                {
                    int pending;
                    int total;
                    int result = FujifilmSdkWrapper.XSDK_GetBufferCapacity(hCamera, out pending, out total);
                    while (result == FujifilmSdkWrapper.XSDK_COMPLETE && pending > 0 && discarded < 8)
                    {
                        if (FujifilmSdkWrapper.XSDK_DeleteImage(hCamera) != FujifilmSdkWrapper.XSDK_COMPLETE) break;
                        discarded++;
                        result = FujifilmSdkWrapper.XSDK_GetBufferCapacity(hCamera, out pending, out total);
                    }
                }
                catch (Exception ex)
                {
                    LogMessage("DrainAbortedExposure", ex.Message);
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
            lock (exposureLock)
            {
                if (generation == exposureGeneration && cameraState == CameraStates.cameraWaiting && !imageReady)
                    cameraState = CameraStates.cameraIdle;
            }
            LogMessage("DrainAbortedExposure", $"Discarded {discarded} stale frame(s).");
        }

        #endregion

        #region Private Helper Methods

        private static void CheckConnected(string message) { if (!IsConnected) { throw new NotConnectedException($"{DriverDescription} ({DriverProgId}) is not connected: {message}"); } }
        private static void CheckSubframe(string property, int value, int expected) { if (value != expected) { LogMessage(property, $"Invalid value {value}. Only full frame ({expected}) is supported."); throw new InvalidValueException(property, value.ToString(), expected.ToString()); } }

        internal static void ReadProfile()
        {
            if (tl == null) { Debug.WriteLine("ReadProfile called before TraceLogger initialized!"); return; }
            try
            {
                // Correct Profile usage: Instantiate, set DeviceType, then use
                Profile driverProfile = new Profile();
                driverProfile.DeviceType = "Camera"; // Set DeviceType
                tl.Enabled = Convert.ToBoolean(driverProfile.GetValue(DriverProgId, traceStateProfileName, string.Empty, traceStateDefault));
                cameraName = driverProfile.GetValue(DriverProgId, cameraNameProfileName, string.Empty, cameraNameDefault);
                LogMessage("ReadProfile", $"Trace state: {tl.Enabled}, Camera Name: '{cameraName}'");
            }
            catch (Exception ex) { LogMessage("ReadProfile", $"Error reading profile: {ex.Message}"); tl.Enabled = Convert.ToBoolean(traceStateDefault); cameraName = cameraNameDefault; }
        }

        internal static void WriteProfile()
        {
            try
            {
                // Correct Profile usage: Instantiate, set DeviceType, then use
                Profile driverProfile = new Profile();
                driverProfile.DeviceType = "Camera"; // Set DeviceType
                if (tl != null) driverProfile.WriteValue(DriverProgId, traceStateProfileName, tl.Enabled.ToString());
                // driverProfile.WriteValue(DriverProgId, cameraNameProfileName, cameraName); // Uncomment when camera selection is added
                LogMessage("WriteProfile", $"Trace state saved: {tl?.Enabled}");
            }
            catch (Exception ex) { LogMessage("WriteProfile", $"Error writing profile: {ex.Message}"); }
        }


        internal static void LogMessage(string identifier, string message) { tl?.LogMessageCrLf(identifier, message); }
        internal static void LogMessage(string identifier, string format, params object[] args) { tl?.LogMessageCrLf(identifier, string.Format(format, args)); }
        private static bool IsConnected => connectedState && hCamera != IntPtr.Zero;

        // *** NEW: Load configuration from JSON file ***
        private static void LoadConfiguration(string modelName)
        {
            try
            {
                string driverPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                currentConfig = CameraModelCatalog.Load(driverPath, modelName,
                    message => LogMessage("LoadConfiguration", message));
                cameraXSize = currentConfig.CameraXSize;
                cameraYSize = currentConfig.CameraYSize;
                pixelSizeX = currentConfig.PixelSizeX;
                pixelSizeY = currentConfig.PixelSizeY;
                maxAdu = currentConfig.MaxAdu;
                LogMessage("LoadConfiguration", $"Successfully loaded and parsed config for {currentConfig.ModelName}.");
            }
            catch (Exception ex)
            {
                LogMessage("LoadConfiguration", $"Unable to load a configuration for '{modelName}': {ex.Message}");
                currentConfig = null;
            }
        }


        // *** MODIFIED: CacheCameraCapabilities now calls SDK functions ***
        private static void CacheCameraCapabilities()
        {
            LogMessage("CacheCameraCapabilities", $"Entering CacheCameraCapabilities. State: connectedState={connectedState}, hCamera={hCamera}, IsConnected={IsConnected}");

            if (!IsConnected)
            {
                LogMessage("CacheCameraCapabilities", "Exiting CacheCameraCapabilities early because IsConnected is false.");
                return; // Exit if not connected
            }
            // Ensure config is loaded before proceeding (should have been loaded during connect)
            if (currentConfig == null)
            {
                LogMessage("CacheCameraCapabilities", "CRITICAL: currentConfig is null. Cannot cache capabilities.");
                // Optionally throw an exception or set default fallbacks here
                // For now, using hardcoded fallbacks as before, but logging critical error
                minSensitivity = 100; maxSensitivity = 12800; supportedSensitivities.Clear();
                minExposure = 0.0001; maxTimedExposure = 60.0; maxExposure = 3600.0;
                bulbCapable = true; supportedShutterSpeeds.Clear();
                return;
            }


            LogMessage("CacheCameraCapabilities", "Caching camera capabilities (Querying SDK)...");

            // --- Populate Shutter Map FIRST ---
            PopulateShutterSpeedMaps(); // Uses hardcoded map from SDK PDF for now
            LogMessage("CacheCameraCapabilities", $"After PopulateShutterSpeedMaps, durationToSdkShutterSpeed.Count = {durationToSdkShutterSpeed.Count}");

            // --- Get Sensitivity (Gain) Capabilities ---
            try
            {
                LogMessage("CacheCameraCapabilities", "Querying fixed sensitivity values...");
                int[] sdkSensitivities = FujifilmSdkWrapper.GetIntArrayFromSdkSensitivity(hCamera);

                // Process results as before...
                if (sdkSensitivities != null && sdkSensitivities.Length > 0)
                {
                    supportedSensitivities = FujifilmCapabilities.FixedSensitivities(sdkSensitivities).ToList();
                    if (supportedSensitivities.Count > 0) { minSensitivity = supportedSensitivities.Min(); maxSensitivity = supportedSensitivities.Max(); LogMessage("CacheCameraCapabilities", $"Sensitivity Range: Min={minSensitivity}, Max={maxSensitivity}. Count={supportedSensitivities.Count}"); }
                    else { LogMessage("CacheCameraCapabilities", "Warning: SDK returned sensitivities, but all were filtered out. Using defaults."); /* Fallback */ minSensitivity = currentConfig.DefaultMinSensitivity; maxSensitivity = currentConfig.DefaultMaxSensitivity; supportedSensitivities.Clear(); }
                }
                else { LogMessage("CacheCameraCapabilities", "Warning: Failed to get sensitivity list from SDK or list was empty. Using defaults."); /* Fallback */ minSensitivity = currentConfig.DefaultMinSensitivity; maxSensitivity = currentConfig.DefaultMaxSensitivity; supportedSensitivities.Clear(); }
            }
            catch (Exception ex) { LogMessage("CacheCameraCapabilities", $"Error getting sensitivity capabilities: {ex.Message}. Using defaults."); /* Fallback */ minSensitivity = currentConfig.DefaultMinSensitivity; maxSensitivity = currentConfig.DefaultMaxSensitivity; supportedSensitivities.Clear(); }

            // --- Get Shutter Speed Capabilities ---
            try
            {
                LogMessage("CacheCameraCapabilities", "Querying Shutter Speed capabilities...");
                int bulbCapResult = 0;
                int[] sdkShutterCodes = FujifilmSdkWrapper.GetIntArrayFromSdkShutterSpeed(hCamera, out bulbCapResult);
                bool sdkBulbCapable = (bulbCapResult == 1); // Store SDK result temporarily

                if (sdkShutterCodes != null && sdkShutterCodes.Length > 0)
                {
                    supportedShutterSpeeds = sdkShutterCodes.ToList();
                    LogMessage("CacheCameraCapabilities", $"Retrieved {supportedShutterSpeeds.Count} shutter speed codes. SDK Bulb Capable: {sdkBulbCapable}");

                    sdkShutterSpeedToDuration = new Dictionary<int, double>(
                        FujifilmCapabilities.BuildShutterMap(
                            supportedShutterSpeeds,
                            currentConfig.ShutterSpeedMap,
                            code => LogMessage("CacheCameraCapabilities", $"Ignoring undocumented shutter code {code}.")));
                    durationToSdkShutterSpeed.Clear();
                    foreach (var pair in sdkShutterSpeedToDuration.OrderBy(pair => pair.Value))
                    {
                        if (!durationToSdkShutterSpeed.ContainsKey(pair.Value))
                            durationToSdkShutterSpeed[pair.Value] = pair.Key;
                    }

                    // *** MODIFIED: Fallback logic for bulbCapable ***
                    bulbCapable = FujifilmCapabilities.ResolveBulbCapability(
                        sdkBulbCapable, currentConfig.DefaultBulbCapable);
                    LogMessage("CacheCameraCapabilities", $"Final Bulb Capable setting: {bulbCapable}");
                    // *** END MODIFIED ***

                    // Update min/max exposure based on *actual* supported codes and the map
                    List<double> validDurations = new List<double>();
                    foreach (int code in supportedShutterSpeeds)
                    {
                        if (sdkShutterSpeedToDuration.ContainsKey(code))
                        {
                            validDurations.Add(sdkShutterSpeedToDuration[code]);
                        }
                    }
                    if (validDurations.Count > 0)
                    {
                        minExposure = validDurations.Min();
                        // *** MODIFIED: maxExposure now represents the longest *programmable* time ***
                        maxTimedExposure = validDurations.Max();
                        maxExposure = bulbCapable
                            ? Math.Max(maxTimedExposure, currentConfig.DefaultMaxExposure)
                            : maxTimedExposure;
                        LogMessage("CacheCameraCapabilities", $"Exposure Range: Min={minExposure}s, TimedMax={maxTimedExposure}s, Max={maxExposure}s");
                    }
                    else
                    {
                        LogMessage("CacheCameraCapabilities", "Warning: No supported shutter codes found in the pre-defined map. Using defaults.");
                        minExposure = currentConfig.DefaultMinExposure;
                        maxTimedExposure = 60.0;
                        maxExposure = currentConfig.DefaultMaxExposure;
                    }
                }
                else
                {
                    LogMessage("CacheCameraCapabilities", "Warning: Failed to get shutter speed list from SDK or list was empty. Using defaults.");
                    minExposure = currentConfig.DefaultMinExposure;
                    maxTimedExposure = 60.0;
                    maxExposure = currentConfig.DefaultMaxExposure;
                    bulbCapable = currentConfig.DefaultBulbCapable; // Use JSON default if SDK fails
                    supportedShutterSpeeds.Clear();
                }
            }
            catch (Exception ex)
            {
                LogMessage("CacheCameraCapabilities", $"Error getting shutter speed capabilities: {ex.Message}. Using defaults.");
                minExposure = currentConfig.DefaultMinExposure;
                maxTimedExposure = 60.0;
                maxExposure = currentConfig.DefaultMaxExposure;
                bulbCapable = currentConfig.DefaultBulbCapable; // Use JSON default if SDK fails
                supportedShutterSpeeds.Clear();
            }

            // Update other capabilities if needed (e.g., CanAbortExposure, CanStopExposure)
            // For now, keeping them as they were:
            canAbortExposure = true;
            canStopExposure = true;
            canPulseGuide = false; // Assuming no pulse guiding

            LogMessage("CacheCameraCapabilities", "Capability caching finished.");
        }


        // Removed List<int> parameter as it wasn't used for population
        private static void PopulateShutterSpeedMaps()
        {
            // Ensure maps are clear before populating
            sdkShutterSpeedToDuration.Clear();
            durationToSdkShutterSpeed.Clear();
            LogMessage("PopulateShutterSpeedMaps", "Populating shutter speed maps based on SDK PDF...");
            // --- MAPPINGS BASED ON SDK PDF pp. 91-95 ---
            // This part remains hardcoded based on the SDK documentation, as these mappings
            // are generally consistent across models that support these speeds.
            // If future models change these fundamental mappings, this would also need
            // to potentially move to the JSON config, but that seems less likely.
            AddSdkShutterMapping(5, 1.0 / 180000.0); AddSdkShutterMapping(6, 1.0 / 160000.0); AddSdkShutterMapping(7, 1.0 / 128000.0);
            AddSdkShutterMapping(9, 1.0 / 102400.0); AddSdkShutterMapping(12, 1.0 / 80000.0); AddSdkShutterMapping(15, 1.0 / 64000.0);
            AddSdkShutterMapping(19, 1.0 / 51200.0); AddSdkShutterMapping(24, 1.0 / 40000.0); AddSdkShutterMapping(30, 1.0 / 32000.0);
            AddSdkShutterMapping(38, 1.0 / 25600.0); AddSdkShutterMapping(43, 1.0 / 24000.0); AddSdkShutterMapping(48, 1.0 / 20000.0);
            AddSdkShutterMapping(61, 1.0 / 16000.0); AddSdkShutterMapping(76, 1.0 / 12800.0); AddSdkShutterMapping(86, 1.0 / 12000.0);
            AddSdkShutterMapping(96, 1.0 / 10000.0); AddSdkShutterMapping(122, 1.0 / 8000.0); AddSdkShutterMapping(153, 1.0 / 6400.0);
            AddSdkShutterMapping(172, 1.0 / 6000.0); AddSdkShutterMapping(193, 1.0 / 5000.0); AddSdkShutterMapping(244, 1.0 / 4000.0);
            AddSdkShutterMapping(307, 1.0 / 3200.0); AddSdkShutterMapping(345, 1.0 / 3000.0); AddSdkShutterMapping(387, 1.0 / 2500.0);
            AddSdkShutterMapping(488, 1.0 / 2000.0); AddSdkShutterMapping(615, 1.0 / 1600.0); AddSdkShutterMapping(690, 1.0 / 1500.0);
            AddSdkShutterMapping(775, 1.0 / 1250.0); AddSdkShutterMapping(976, 1.0 / 1000.0); AddSdkShutterMapping(1230, 1.0 / 800.0);
            AddSdkShutterMapping(1381, 1.0 / 750.0); AddSdkShutterMapping(1550, 1.0 / 640.0); AddSdkShutterMapping(1953, 1.0 / 500.0);
            AddSdkShutterMapping(2460, 1.0 / 400.0); AddSdkShutterMapping(2762, 1.0 / 350.0); AddSdkShutterMapping(3100, 1.0 / 320.0);
            AddSdkShutterMapping(3906, 1.0 / 250.0); AddSdkShutterMapping(4921, 1.0 / 200.0); AddSdkShutterMapping(5524, 1.0 / 180.0);
            AddSdkShutterMapping(6200, 1.0 / 160.0); AddSdkShutterMapping(7812, 1.0 / 125.0); AddSdkShutterMapping(9843, 1.0 / 100.0);
            AddSdkShutterMapping(11048, 1.0 / 90.0); AddSdkShutterMapping(12401, 1.0 / 80.0); AddSdkShutterMapping(15625, 1.0 / 60.0);
            AddSdkShutterMapping(19686, 1.0 / 50.0); AddSdkShutterMapping(22097, 1.0 / 45.0); AddSdkShutterMapping(24803, 1.0 / 40.0);
            AddSdkShutterMapping(31250, 1.0 / 30.0); AddSdkShutterMapping(39372, 1.0 / 25.0); AddSdkShutterMapping(49606, 1.0 / 20.0);
            AddSdkShutterMapping(62500, 1.0 / 15.0); AddSdkShutterMapping(78745, 1.0 / 13.0); AddSdkShutterMapping(99212, 1.0 / 10.0);
            AddSdkShutterMapping(125000, 1.0 / 8.0); AddSdkShutterMapping(157490, 1.0 / 6.0); AddSdkShutterMapping(198425, 1.0 / 5.0);
            AddSdkShutterMapping(250000, 1.0 / 4.0); AddSdkShutterMapping(314980, 1.0 / 3.0); AddSdkShutterMapping(396850, 1.0 / 2.5);
            AddSdkShutterMapping(500000, 1.0 / 2.0); AddSdkShutterMapping(629960, 1.0 / 1.6); AddSdkShutterMapping(707106, 1.0 / 1.5);
            AddSdkShutterMapping(793700, 1.0 / 1.3); AddSdkShutterMapping(1000000, 1.0); AddSdkShutterMapping(1259921, 1.3);
            AddSdkShutterMapping(1414213, 1.5); AddSdkShutterMapping(1587401, 1.6); AddSdkShutterMapping(2000000, 2.0);
            AddSdkShutterMapping(2519842, 2.5); AddSdkShutterMapping(3174802, 3.0); AddSdkShutterMapping(4000000, 4.0);
            AddSdkShutterMapping(5039684, 5.0); AddSdkShutterMapping(6349604, 6.0); AddSdkShutterMapping(8000000, 8.0);
            AddSdkShutterMapping(10079368, 10.0); AddSdkShutterMapping(12699208, 13.0); AddSdkShutterMapping(16000000, 15.0);
            AddSdkShutterMapping(20158736, 20.0); AddSdkShutterMapping(25398416, 25.0); AddSdkShutterMapping(32000000, 30.0);
            AddSdkShutterMapping(64000000, 60.0);
            // Add other long exposures if needed and supported by CapShutterSpeed
            LogMessage("PopulateShutterSpeedMaps", $"Populated {sdkShutterSpeedToDuration.Count} shutter speed mappings.");
        }

        private static void AddSdkShutterMapping(int sdkValue, double duration)
        {
            if (sdkValue == 0 || sdkValue == -1) return;
            double tolerance = 1e-9;
            bool durationExists = false;
            foreach (var existingKey in durationToSdkShutterSpeed.Keys) { if (Math.Abs(existingKey - duration) < tolerance) { durationExists = true; break; } }
            if (!sdkShutterSpeedToDuration.ContainsKey(sdkValue))
            {
                sdkShutterSpeedToDuration.Add(sdkValue, duration);
                if (!durationExists) { durationToSdkShutterSpeed.Add(duration, sdkValue); }
                else { LogMessage("AddSdkShutterMapping", $"Warning: Duration {duration} already mapped. SDK value {sdkValue} added to forward map only."); }
            }
            else { LogMessage("AddSdkShutterMapping", $"Warning: SDK value {sdkValue} already mapped. Skipping."); }
        }

        private static int DurationToSdkShutterSpeed(double duration)
        {
            LogMessage("DurationToSdkShutterSpeed", $"Attempting to map duration: {duration}s"); // Added log
            if (durationToSdkShutterSpeed.Count == 0)
            {
                LogMessage("DurationToSdkShutterSpeed", "Error: durationToSdkShutterSpeed map is empty!"); // Added log
                throw new DriverException("Shutter speed capabilities not loaded or empty map.");
            }
            double minDiff = double.MaxValue;
            double closestDuration = -1.0;
            foreach (double supportedDuration in durationToSdkShutterSpeed.Keys)
            {
                double diff = Math.Abs(supportedDuration - duration);
                if (diff < minDiff) { minDiff = diff; closestDuration = supportedDuration; }
            }
            if (closestDuration < 0) { throw new DriverException("Could not find closest duration match."); }
            double tolerance = Math.Max(closestDuration * 0.001, 0.0001);
            if (minDiff <= tolerance)
            {
                int sdkVal = durationToSdkShutterSpeed[closestDuration];
                LogMessage("DurationToSdkShutterSpeed", $"Mapping duration {duration}s to closest supported {closestDuration}s (SDK: {sdkVal})");
                return sdkVal;
            }
            else
            {
                // Check bulb capability using the 'bulbCapable' field populated during CacheCameraCapabilities
                // ** maxExposure here represents the longest *programmable* shutter speed **
                if (bulbCapable && duration > maxTimedExposure)
                {
                    LogMessage("DurationToSdkShutterSpeed", $"Duration {duration}s > max programmable ({maxTimedExposure}s), mapping to BULB/TIME (-1).");
                    return FujifilmSdkWrapper.XSDK_SHUTTER_BULB; // Return -1 to indicate Bulb/Time needed
                }
                LogMessage("DurationToSdkShutterSpeed", $"Duration {duration}s is too far from nearest supported {closestDuration}s (Diff: {minDiff}, Tol: {tolerance}).");
                throw new InvalidValueException($"Requested duration {duration}s is not supported or too far from nearest value {closestDuration}s.");
            }
        }

        // *** RE-ADDED: Helper to get the SDK code for the longest mapped duration ***
        private static int GetMaxMappedSdkShutterCode()
        {
            if (durationToSdkShutterSpeed == null || durationToSdkShutterSpeed.Count == 0)
            {
                LogMessage("GetMaxMappedSdkShutterCode", "Error: durationToSdkShutterSpeed map is empty! Falling back.");
                return 64000000; // Fallback to 60s code if map isn't ready
            }
            double maxMappedDuration = durationToSdkShutterSpeed.Keys.Max();
            return durationToSdkShutterSpeed[maxMappedDuration];
        }

        // *** UPDATED: Timer callback specifically for BULB exposures ***
        private static void OnBulbExposureTimerElapsed(object state)
        {
            int generation = state is int ? (int)state : -1;
            // *** Ensure the entire method is locked ***
            lock (exposureLock)
            {
                if (generation != exposureGeneration || cameraState != CameraStates.cameraExposing)
                {
                    LogMessage("OnBulbExposureTimerElapsed", $"Timer fired but state is {cameraState}. Ignoring.");
                    return;
                }
                LogMessage("OnBulbExposureTimerElapsed", $"BULB timer fired for {lastExposureDuration}s. Attempting to STOP exposure using combined command XSDK_RELEASE_N_BULBS1OFF (0x{FujifilmSdkWrapper.XSDK_RELEASE_N_BULBS1OFF:X}).");

                IntPtr shotOptPtr = IntPtr.Zero;
                bool stopSuccess = false; // Flag to track if stop sequence likely worked

                try
                {
                    if (!IsConnected)
                    {
                        LogMessage("OnBulbExposureTimerElapsed", "Disconnected during bulb exposure wait.");
                        cameraState = CameraStates.cameraError;
                        return;
                    }

                    shotOptPtr = Marshal.AllocHGlobal(sizeof(int));
                    Marshal.WriteInt32(shotOptPtr, 0);

                    // Send the combined BULB STOP command
                    int releaseModeStopBulb = FujifilmSdkWrapper.XSDK_RELEASE_N_BULBS1OFF; // Use 0x000C
                    LogMessage("OnBulbExposureTimerElapsed", $"Triggering BULB STOP via XSDK_Release (Mode: 0x{releaseModeStopBulb:X}, Options Ptr: {shotOptPtr})...");
                    int releaseStatusBulb; // Variable to receive the status output
                    int bulbOffResult = FujifilmSdkWrapper.XSDK_Release(hCamera, releaseModeStopBulb, shotOptPtr, out releaseStatusBulb); // Pass the pointer for pShotOpt
                    LogMessage("OnBulbExposureTimerElapsed", $"XSDK_Release (BULB STOP) returned {bulbOffResult}, status={releaseStatusBulb}");

                    if (bulbOffResult != FujifilmSdkWrapper.XSDK_COMPLETE)
                    {
                        LogMessage("OnBulbExposureTimerElapsed", $"WARNING: XSDK_Release (BULB STOP) failed with result {bulbOffResult}. Getting specific error...");
                        LogSpecificError($"BULB STOP (0x{releaseModeStopBulb:X})"); // Log specific error
                    }
                    else
                    {
                        LogMessage("OnBulbExposureTimerElapsed", $"SDK Release command (BULB STOP) sent successfully.");
                        stopSuccess = true; // Mark stop as successful
                    }

                    // *** REMOVED FIXED DELAY - Replaced with polling task ***
                    //LogMessage("OnBulbExposureTimerElapsed", "Adding longer delay after stop command...");
                    //System.Threading.Thread.Sleep(2500); // Increase delay to 2.5 seconds

                    // Now check for the image data using the helper method
                    if (stopSuccess)
                    {
                        cameraState = CameraStates.cameraWaiting;
                        LogMessage("OnBulbExposureTimerElapsed", "Starting background task to poll for image data...");
                        Task.Run(() => PollForImage(generation));
                        // The camera state will be set to Idle or Error by the polling task
                        // We don't call CheckForImageData directly here anymore.
                    }
                    else
                    {
                        LogMessage("OnBulbExposureTimerElapsed", "Skipping image polling because Bulb stop command failed.");
                        cameraState = CameraStates.cameraError; // Ensure error state if stop failed
                        imageReady = false;
                    }

                }
                catch (Exception ex)
                {
                    LogMessage("OnBulbExposureTimerElapsed", $"Error stopping bulb exposure or starting poll task: {ex.Message}\n{ex.StackTrace}");
                    cameraState = CameraStates.cameraError;
                    imageReady = false;
                }
                finally
                {
                    // Free the allocated memory for the pointers used in the Release call
                    if (shotOptPtr != IntPtr.Zero) Marshal.FreeHGlobal(shotOptPtr);

                    // *** IMPORTANT: Do NOT set cameraState here. The polling task will handle it. ***
                    // // Ensure state moves away from exposing if not already error/idle
                    // // CheckForImageData will set to Idle if successful, or Error if exception occurred there.
                    // if (cameraState == CameraStates.cameraExposing)
                    // {
                    //     LogMessage("OnBulbExposureTimerElapsed", "State still 'Exposing' after checks/errors, setting to 'Idle'.");
                    //     cameraState = CameraStates.cameraIdle; // Move to idle if checks didn't change state
                    //     imageReady = false; // Ensure imageReady is false if we force idle here
                    // }
                }
            } // End Lock
        }

        // *** NEW: Background task to poll for image after bulb stop ***
        private static async Task PollForImage(int generation)
        {
            const int pollIntervalMs = 500; // Check every 500ms
            const int timeoutSeconds = 15; // Give up after 15 seconds
            Stopwatch stopwatch = Stopwatch.StartNew();
            bool foundImage = false;

            LogMessage("PollForImage", $"Polling started. Timeout: {timeoutSeconds}s, Interval: {pollIntervalMs}ms");

            while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
            {
                lock (exposureLock)
                {
                    if (generation != exposureGeneration)
                    {
                        LogMessage("PollForImage", "A newer exposure superseded this poll.");
                        return;
                    }
                }
                // Check if connected before calling CheckForImageData
                lock (hardwareLock) // Use hardwareLock for checking IsConnected
                {
                    if (!IsConnected)
                    {
                        LogMessage("PollForImage", "Disconnected during polling. Aborting poll.");
                        lock (exposureLock)
                        {
                            if (generation == exposureGeneration)
                            {
                                cameraState = CameraStates.cameraError;
                                imageReady = false;
                            }
                        }
                        return; // Exit the task
                    }
                }

                // Call CheckForImageData - this will acquire its own lock
                CheckForImageData(generation);

                // Check if the image became ready (use lock for thread safety)
                lock (exposureLock)
                {
                    if (imageReady)
                    {
                        foundImage = true;
                        LogMessage("PollForImage", $"Image found after {stopwatch.Elapsed.TotalSeconds:F1}s. Polling successful.");
                        // CheckForImageData already set state to Idle
                        break; // Exit the loop
                    }
                }

                // Wait before next poll
                await Task.Delay(pollIntervalMs);
            }

            stopwatch.Stop();

            // Handle timeout
            if (!foundImage)
            {
                lock (exposureLock)
                {
                    if (generation != exposureGeneration) return;
                    LogMessage("PollForImage", $"Polling timed out after {timeoutSeconds}s. Image not found.");
                    cameraState = CameraStates.cameraError; // Set error state on timeout
                    imageReady = false;
                }
            }
        }


        // Helper to log specific errors after a failed Release call
        private static void LogSpecificError(string commandName)
        {
            try
            {
                int apiCode = 0, errCode = 0;
                FujifilmSdkWrapper.XSDK_GetErrorNumber(hCamera, out apiCode, out errCode);
                LogMessage("OnBulbExposureTimerElapsed", $"Specific Error after {commandName}: API Code={apiCode:X}, Error Code={errCode}");
            }
            catch (Exception getErrEx)
            {
                LogMessage("OnBulbExposureTimerElapsed", $"Exception getting error details after {commandName}: {getErrEx.Message}");
            }
        }


        // *** MODIFIED: Original callback now only for TIMED exposures ***
        private static void OnExposureComplete(object state)
        {
            int generation = state is int ? (int)state : -1;
            lock (exposureLock)
            {
                if (generation != exposureGeneration || cameraState != CameraStates.cameraExposing)
                {
                    LogMessage("OnExposureComplete", $"Timer fired but state is {cameraState}. Ignoring.");
                    return;
                }
                cameraState = CameraStates.cameraWaiting;
                LogMessage("OnExposureComplete", $"Timed exposure completed after {lastExposureDuration}s. Polling for RAW data.");
            }
            Task.Run(() => PollForImage(generation));
        }

        // *** NEW: Helper method to check for image data ***
        private static void CheckForImageData(int generation)
        {
            // This logic was previously in OnExposureComplete
            lock (exposureLock) // Ensure lock is held
            {
                if (generation != exposureGeneration)
                {
                    LogMessage("CheckForImageData", "A newer exposure superseded this image check.");
                    return;
                }
                LogMessage("CheckForImageData", $"Checking image buffer.");
                try
                {
                    if (!IsConnected)
                    {
                        LogMessage("CheckForImageData", "Disconnected while checking for image.");
                        cameraState = CameraStates.cameraError;
                        imageReady = false;
                        return;
                    }
                    FujifilmSdkWrapper.XSDK_ImageInformation imgInfo;
                    int result = FujifilmSdkWrapper.XSDK_ReadImageInfo(hCamera, out imgInfo);
                    if (result == FujifilmSdkWrapper.XSDK_COMPLETE && imgInfo.lDataSize > 0)
                    {
                        if (!FujifilmCapabilities.IsRawImageFormat(imgInfo.lFormat))
                        {
                            LogMessage("CheckForImageData", $"Discarding non-RAW frame. Size={imgInfo.lDataSize}, Format=0x{imgInfo.lFormat:X}.");
                            FujifilmSdkWrapper.XSDK_DeleteImage(hCamera);
                            imageReady = false;
                            cameraState = CameraStates.cameraWaiting;
                        }
                        else
                        {
                            LogMessage("CheckForImageData", $"RAW image detected. Size: {imgInfo.lDataSize}, Format: 0x{imgInfo.lFormat:X}");
                            imageReady = true;
                            activeExposureIsBulb = false;
                            cameraState = CameraStates.cameraIdle;
                        }
                    }
                    else
                    {
                        // Log if no image found, but don't necessarily treat as error yet
                        LogMessage("CheckForImageData", $"No image data found via ReadImageInfo (Result: {result}). Client needs to poll ImageReady or retry download.");
                        cameraState = CameraStates.cameraWaiting;
                        imageReady = false;
                        // Optionally check for specific non-zero results from ReadImageInfo if they indicate errors vs just 'not ready'
                    }
                }
                catch (Exception ex)
                {
                    LogMessage("CheckForImageData", $"Error checking for image: {ex.Message}\n{ex.StackTrace}");
                    cameraState = CameraStates.cameraError;
                    imageReady = false;
                }
            }
        }


        /// <summary>
        /// Downloads RAW data using Fuji SDK and processes it using the C++/CLI LibRawWrapper.
        /// Stores the result in lastImageArray as int[,].
        /// </summary>
        private static void DownloadImageData()
        {
            LogMessage("DownloadImageData", "Starting RAW Bayer image download (Using C++/CLI Wrapper)...");
            cameraState = CameraStates.cameraDownload;
            lastImageArray = null;
            byte[] downloadBuffer = null;

            // Ensure config is loaded before proceeding
            if (currentConfig == null || currentConfig.SdkConstants == null)
            {
                LogMessage("DownloadImageData", "Error: Camera configuration not loaded. Cannot determine RAW formats.");
                cameraState = CameraStates.cameraError;
                throw new DriverException("Camera configuration not loaded. Cannot download image.");
            }

            try
            {
                CheckConnected("DownloadImageData"); // Checks hCamera

                // --- Get Image Info from Fuji SDK ---
                FujifilmSdkWrapper.XSDK_ImageInformation imgInfo;
                LogMessage("DownloadImageData", "Calling XSDK_ReadImageInfo...");
                int result = FujifilmSdkWrapper.XSDK_ReadImageInfo(hCamera, out imgInfo);
                FujifilmSdkWrapper.CheckSdkError(hCamera, result, "XSDK_ReadImageInfo (DownloadImageData)");
                if (imgInfo.lDataSize <= 0) throw new DriverException($"XSDK_ReadImageInfo reported zero data size ({imgInfo.lDataSize}).");
                LogMessage("DownloadImageData", $"Expecting image: {imgInfo.lImagePixWidth}x{imgInfo.lImagePixHeight}, Size: {imgInfo.lDataSize}, Format: {imgInfo.lFormat:X}");

                // --- Check Format using loaded config ---
                // *** MODIFIED: Check against ALL configured RAW codes ***
                bool isRawFormat = FujifilmCapabilities.IsRawImageFormat(imgInfo.lFormat);
                // Add more checks here if other formats can also represent RAW for different models

                if (!isRawFormat)
                {
                    LogMessage("DownloadImageData", $"Format {imgInfo.lFormat:X} is not a configured RAW format. Deleting from buffer.");
                    try
                    {
                        int deleteResult = FujifilmSdkWrapper.XSDK_DeleteImage(hCamera);
                        if (deleteResult != FujifilmSdkWrapper.XSDK_COMPLETE)
                        {
                            LogMessage("DownloadImageData", $"Warning: Failed to delete non-RAW image (Format {imgInfo.lFormat:X}) from buffer. SDK Error: {deleteResult}");
                            // Optionally log detailed error using CheckSdkError without throwing
                        }
                        else
                        {
                            LogMessage("DownloadImageData", $"Successfully deleted non-RAW image (Format {imgInfo.lFormat:X}) from buffer.");
                        }
                    }
                    catch (Exception deleteEx)
                    {
                        LogMessage("DownloadImageData", $"Exception while deleting non-RAW image: {deleteEx.Message}");
                    }
                    // Set lastImageArray to null and return. The get_ImageArray property will handle the retry.
                    lastImageArray = null;
                    cameraState = CameraStates.cameraIdle; // Go back to idle to allow retry
                    return; // Exit the method, don't attempt download/process
                    // --- END MODIFIED: Delete non-RAW and return ---
                }
                // --- End Format Check ---

                // --- Download using Fuji SDK ---
                downloadBuffer = new byte[imgInfo.lDataSize];
                GCHandle pinnedBuffer = GCHandle.Alloc(downloadBuffer, GCHandleType.Pinned);
                IntPtr bufferPtr = IntPtr.Zero;
                try
                {
                    bufferPtr = pinnedBuffer.AddrOfPinnedObject();
                    LogMessage("DownloadImageData", $"Calling XSDK_ReadImage for {imgInfo.lDataSize} bytes...");
                    Stopwatch sw = Stopwatch.StartNew();
                    result = FujifilmSdkWrapper.XSDK_ReadImage(hCamera, bufferPtr, (uint)imgInfo.lDataSize);
                    sw.Stop();
                    LogMessage("DownloadImageData", $"XSDK_ReadImage completed in {sw.ElapsedMilliseconds} ms. Result: {result}");
                    FujifilmSdkWrapper.CheckSdkError(hCamera, result, "XSDK_ReadImage");
                }
                finally
                {
                    if (pinnedBuffer.IsAllocated) pinnedBuffer.Free();
                }

                // --- Process with LibRaw using C++/CLI Wrapper ---
                LogMessage("DownloadImageData", $"Attempting to process {downloadBuffer.Length} bytes with LibRawWrapper...");
                Stopwatch procSw = Stopwatch.StartNew();

                ushort[,] bayerDataUShort = null; // Wrapper returns ushort[,]
                int width = 0;
                int height = 0;

                // Call the static method from the C++/CLI wrapper
                // Ensure the namespace Fujifilm.LibRawWrapper matches the C++/CLI project
                int wrapperResult = RawProcessor.ProcessRawBuffer(downloadBuffer, out bayerDataUShort, out width, out height);

                if (wrapperResult != 0) // LibRaw error codes are non-zero (LIBRAW_SUCCESS is 0)
                {
                    // TODO: Optionally get error string from libraw_strerror if needed (might require adding a P/Invoke for it back or in the wrapper)
                    LogMessage("DownloadImageData", $"LibRawWrapper.ProcessRawBuffer failed with LibRaw error code: {wrapperResult}");
                    throw new DriverException($"LibRaw processing failed via wrapper. LibRaw Error Code: {wrapperResult}");
                }

                if (bayerDataUShort == null || width <= 0 || height <= 0)
                {
                    LogMessage("DownloadImageData", "LibRawWrapper.ProcessRawBuffer returned success but output data/dimensions are invalid.");
                    throw new DriverException("LibRaw wrapper returned invalid data or dimensions despite success code.");
                }

                LogMessage("DownloadImageData", $"LibRawWrapper processed successfully. Dimensions: {width}x{height}");
                procSw.Stop();
                LogMessage("DownloadImageData", $"LibRawWrapper processing completed in {procSw.ElapsedMilliseconds} ms.");

                // --- Convert ushort[,] to int[,] for ASCOM ImageArray ---
                // ASCOM ImageArray standard is int[x,y] or int[,,]
                // C# arrays from GetLength are [rank0=rows=height, rank1=cols=width]
                if (width != bayerDataUShort.GetLength(1) || height != bayerDataUShort.GetLength(0))
                {
                    LogMessage("DownloadImageData", $"Dimension mismatch between reported ({width}x{height}) and array ({bayerDataUShort.GetLength(1)}x{bayerDataUShort.GetLength(0)}).");
                    // Adjust width/height or throw error? Let's trust the array dimensions.
                    height = bayerDataUShort.GetLength(0);
                    width = bayerDataUShort.GetLength(1);
                }

                int cropLeft, cropTop;
                int[,] bayerArrayInt = ActiveFrameCropper.ToAscomArray(
                    bayerDataUShort, cameraXSize, cameraYSize, out cropLeft, out cropTop);
                if (width != cameraXSize || height != cameraYSize)
                    LogMessage("DownloadImageData", $"Cropped LibRaw frame {width}x{height} to advertised {cameraXSize}x{cameraYSize} at ({cropLeft},{cropTop}).");

                lastImageArray = bayerArrayInt; // Store the final int[,] array

            }
            catch (DllNotFoundException dllEx) // Catch errors finding the wrapper DLL or its dependencies (like libraw.dll)
            {
                LogMessage("DownloadImageData", $"DLL Not Found: {dllEx.Message}. Ensure LibRawWrapper.dll and libraw.dll are correctly deployed.");
                lastImageArray = null;
                cameraState = CameraStates.cameraError;
                throw new DriverException($"DLL Not Found: {dllEx.Message}. Ensure LibRawWrapper.dll and libraw.dll are correctly deployed.", dllEx);
            }
            catch (Exception ex)
            {
                LogMessage("DownloadImageData", $"Image download/processing failed: {ex.Message}\n{ex.StackTrace}");
                lastImageArray = null;
                cameraState = CameraStates.cameraError;
                throw; // Rethrow original exception
            }
            finally
            {
                // No LibRaw recycle/close needed here - handled by wrapper
                if (cameraState != CameraStates.cameraError) { cameraState = CameraStates.cameraIdle; }
                downloadBuffer = null; // Allow GC
            }
        }


        #endregion
    }
}
