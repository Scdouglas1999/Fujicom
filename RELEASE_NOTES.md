# Fujicom 3.0.2

Fixes the Release DLL packaging failure reported in #10 and image-size mismatches in SharpCap. Includes #11 and #12, contributed by @roma-derski, with additional runtime-verifier and native GFX regression fixes.

## Fixes

- Prevent the checked-in debug `LibRawWrapper.dll` from replacing the freshly built Release wrapper. Both Release configurations explicitly use the Release Visual C++ runtime.
- Reject packages containing Visual C++ debug runtime dependencies, including numbered and named component DLLs such as `VCRUNTIME140_1D.dll` and `MSVCP140D_ATOMIC_WAIT.dll`.
- Return ASCOM image arrays at the camera's advertised dimensions, using even crop offsets to preserve Bayer color alignment.
- Remove the extra 48-column trim from both processed X-Trans images and native Bayer/GFX active areas. Preserve LibRaw's native-area origin and validate its bounds before allocating the image.
- Add native GFX active-area regression tests, broader managed crop tests, and 21 runtime-verifier checks to CI.

## Updating

Use `Fujicom.Setup.v3.0.2.exe` for Windows x64. ASCOM Platform, .NET Framework 4.7.2 or newer, and the Microsoft Visual C++ x64 Redistributable remain prerequisites; Visual Studio debug runtimes are not required.

Image dimensions can differ from earlier Fujicom versions. Use matching capture settings and driver versions for lights and calibration frames. X-Trans output remains synthetic RGGB for ASCOM compatibility; retain native RAFs for calibration-sensitive workflows.

## Validation

- Linux and Windows core checks, managed compilation, Windows x64 C++/CLI compilation, runtime dependency checks, and installer packaging passed.
- 11 core checks, native Bayer geometry tests, and 21 runtime-verifier checks passed.
- SDK verification passed for 25 XAPI exports, 7 LibRaw exports, and 5 signature/constant contracts.
- Local GFX100S and X-T5 RAF replays using LibRaw 0.21.4 and the production geometry/crop helpers produced 11648 x 8736 and 7728 x 5152 respectively, with every output pixel checked against its expected source position. This replay does not exercise Windows C++/CLI loading or the camera's USB protocol.
- The contributor reported 458 successful X-T4 captures through SharpCap for the original image-dimension fix. No new physical-camera capture session was performed for the final 3.0.2 build.

# Fujicom 3.0.1

Bug-fix release for the 3.0.0 connection failure reported in GitHub issue #8 (`Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')`), seen in NINA with the X-T4 and X-H2S.

## Fixes

- The driver now uses a single ASCOM gain mode, "Gain Value": `Gain` is the ISO number, `GainMin`/`GainMax` give the range the body reports, and `Gains` throws `PropertyNotImplementedException`. 3.0.0 exposed both modes at once, which ICameraV3 forbids. NINA treats a populated `Gains` list as authoritative and indexes it with the value of `Gain`, so `Gains[800]` threw inside NINA during connection. Earlier releases only avoided this because their `XSDK_CapSensitivity` call had the wrong native signature, failed, and left `Gains` empty; correcting that signature in 3.0.0 exposed the latent contract violation.
- Setting `Gain` to a value inside the range but between the camera's fixed ISO steps now selects the nearest supported ISO instead of passing an unsupported value to the SDK.
- Added core tests for the nearest-ISO mapping and a regression guard that the COM-facing driver exposes exactly one gain mode.

## Validation boundary

Unchanged from 3.0.0: the managed driver and core logic are checked automatically, but final validation still requires Windows, the ASCOM Platform, the x64 C++/CLI wrapper, and a physical supported Fujifilm body.

# Fujicom 3.0.0

This release overhauls the ASCOM driver's capture lifecycle using the hardware lessons incorporated into the NINA Fujifilm Native Plugin.

## Highlights

- Corrected the `XSDK_CapSensitivity` native signature and now filters Auto ISO modes and expanded ISO values that exceed ASCOM's signed 16-bit `Gain` range.
- Corrected RAW compression constants and routed optional settings through the exported `XSDK_GetProp`/`XSDK_SetProp` functions.
- Added repeated RAW-ready polling for both timed and bulb exposures.
- Added working `AbortExposure` and `StopExposure` paths, including stale-buffer cleanup.
- Recognizes rotated RAW codes and safely skips non-RAW companion frames.
- Crops the Fuji/LibRaw optical-black columns so returned arrays match the advertised active image area.
- Added the extended timed/T-mode shutter catalog through 60 minutes.
- Added X-Trans-to-synthetic-RGGB compatibility conversion while preserving native GFX Bayer data. Synthetic X-Trans output is intended for ASCOM compatibility, not as a substitute for native RAF files in calibration-sensitive workflows.
- Reference-counts shared ASCOM client connections and closes the SDK session after the final disconnect.
- Matches and validates model configurations using normalized SDK product names.
- Added GFX100 II, GFX100RF, GFX100S II, GFX50S II, and X-S10 configurations.
- Corrected the inherited template version `6.6` to `3.0.0`.
- Added repeatable core tests, cross-platform C# compilation, configuration validation, and native interop verification in GitHub Actions.

## Validation boundary

The managed driver, configuration catalog, shutter/ISO logic, SDK exports, constants, and project structure are automatically checked. Final release validation still requires Windows, ASCOM Platform, the x64 C++/CLI wrapper, and a physical supported Fujifilm body; CI cannot simulate the proprietary camera hardware protocol.
