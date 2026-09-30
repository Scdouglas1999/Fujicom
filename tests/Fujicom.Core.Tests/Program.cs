using System.Text.Json;
using ASCOM.ScdouglasFujifilm.Camera.Core;

internal static class Program
{
    private static int _passed;

    private static int Main()
    {
        try
        {
            Run("fixed ISO filtering", TestFixedIsoFiltering);
            Run("nearest supported ISO", TestNearestSensitivity);
            Run("single ASCOM gain mode", TestSingleAscomGainMode);
            Run("rotated RAW recognition", TestRawFormats);
            Run("bulb capability fallback", TestBulbFallback);
            Run("long and T-mode shutter map", TestLongShutterMap);
            Run("shutter selection", TestShutterSelection);
            Run("camera model matching", TestCameraModelMatching);
            Run("all camera configurations", TestAllCameraConfigurations);
            Run("active frame crop", TestActiveFrameCrop);
            Run("project XML", TestProjectXml);
            Console.WriteLine($"All {_passed} Fujicom core checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TestFixedIsoFiltering()
    {
        var actual = FujifilmCapabilities.FixedSensitivities(new[] { -10, 160, 320, -1, 160, 640, 51200 });
        Equal("160,320,640", string.Join(",", actual));
    }

    private static void TestNearestSensitivity()
    {
        var supported = new[] { 160, 200, 250, 320, 400, 500, 640, 800, 1000, 1250, 1600 };
        Equal(800, FujifilmCapabilities.NearestSensitivity(supported, 800));
        Equal(800, FujifilmCapabilities.NearestSensitivity(supported, 850));
        Equal(1000, FujifilmCapabilities.NearestSensitivity(supported, 950));
        Equal(640, FujifilmCapabilities.NearestSensitivity(supported, 720)); // equidistant: prefer the lower ISO
        Equal(160, FujifilmCapabilities.NearestSensitivity(supported, 1));
        Equal(1600, FujifilmCapabilities.NearestSensitivity(supported, 25600));
        Equal(1234, FujifilmCapabilities.NearestSensitivity(Array.Empty<int>(), 1234));
        Equal(1234, FujifilmCapabilities.NearestSensitivity(null, 1234));
    }

    private static void TestSingleAscomGainMode()
    {
        // Regression guard for issue #8: the COM-facing driver must expose exactly one ASCOM gain mode.
        // Gain Value mode => GainMin/GainMax are implemented and Gains throws PropertyNotImplementedException.
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Fuji", "CameraDriver", "CameraDriver.cs"));
        True(Member(source, "public ArrayList Gains").Contains("throw new PropertyNotImplementedException(\"Gains\", false)"),
            "Gains must throw PropertyNotImplementedException while Gain is an ISO value.");
        False(Member(source, "public short GainMin").Contains("PropertyNotImplementedException"));
        False(Member(source, "public short GainMax").Contains("PropertyNotImplementedException"));
    }

    private static string Member(string source, string declaration)
    {
        int start = source.IndexOf(declaration, StringComparison.Ordinal);
        True(start >= 0, "Missing member: " + declaration);
        int next = source.IndexOf("\n        public ", start + declaration.Length, StringComparison.Ordinal);
        return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
    }

    private static void TestRawFormats()
    {
        True(FujifilmCapabilities.IsRawImageFormat(0x0001));
        True(FujifilmCapabilities.IsRawImageFormat(0x0601));
        True(FujifilmCapabilities.IsRawImageFormat(0x0301));
        True(FujifilmCapabilities.IsRawImageFormat(0x0801));
        False(FujifilmCapabilities.IsRawImageFormat(0x0007));
    }

    private static void TestBulbFallback()
    {
        True(FujifilmCapabilities.ResolveBulbCapability(false, true));
        True(FujifilmCapabilities.ResolveBulbCapability(true, false));
        False(FujifilmCapabilities.ResolveBulbCapability(false, false));
    }

    private static void TestLongShutterMap()
    {
        var codes = new[] { 15625, 64000000, 64000030, 64000180, 123456789 };
        var unknown = new List<int>();
        var map = FujifilmCapabilities.BuildShutterMap(codes, null, unknown.Add);
        Near(1.0 / 60.0, map[15625]);
        Near(60.0, map[64000000]);
        Near(120.0, map[64000030]);
        Near(3600.0, map[64000180]);
        Equal(123456789, unknown.Single());
    }

    private static void TestShutterSelection()
    {
        var map = new Dictionary<int, double> { [1] = 1.0, [2] = 2.0, [3] = 4.0 };
        Equal(2, FujifilmCapabilities.SelectShutterCode(map, 2.1, false));
        Equal(FujifilmCapabilities.BulbCode, FujifilmCapabilities.SelectShutterCode(map, 5.0, true));
        Throws<InvalidOperationException>(() => FujifilmCapabilities.SelectShutterCode(map, 5.0, false));
    }

    private static void TestCameraModelMatching()
    {
        string temp = Path.Combine(Path.GetTempPath(), "fujicom-model-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "CameraConfigs"));
        try
        {
            WriteConfig(temp, "GFX100S", 100);
            WriteConfig(temp, "GFX100SII", 80);
            var match = CameraModelCatalog.Load(temp, "FUJIFILM GFX 100S II", null);
            Equal("GFX100SII", match.ModelName);
            Equal(80, match.DefaultMinSensitivity);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static void TestAllCameraConfigurations()
    {
        string repo = FindRepositoryRoot();
        string directory = Path.Combine(repo, "Fuji", "CameraConfigs");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        string[] files = Directory.GetFiles(directory, "*.json");
        True(files.Length >= 18);
        foreach (string file in files)
        {
            var config = JsonSerializer.Deserialize<CameraConfig>(File.ReadAllText(file), options);
            True(CameraModelCatalog.IsValid(config), "Invalid configuration: " + file);
            var selected = CameraModelCatalog.Load(Path.Combine(repo, "Fuji"), "FUJIFILM " + config.ModelName, null);
            Equal(config.ModelName, selected.ModelName);
        }
    }

    private static void TestProjectXml()
    {
        string project = Path.Combine(FindRepositoryRoot(), "Fuji", "Fuji.csproj");
        System.Xml.Linq.XDocument.Load(project);
    }

    private static void TestActiveFrameCrop()
    {
        Equal(72, ActiveFrameCropper.EvenCenteredOffset(6384, 6240));
        Equal(10, ActiveFrameCropper.EvenCenteredOffset(4182, 4160));

        var source = new ushort[8, 10];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 10; x++)
                source[y, x] = (ushort)(y * 100 + x);

        var cropped = ActiveFrameCropper.ToAscomArray(source, 4, 4, out int left, out int top);
        Equal(2, left);
        Equal(2, top);
        Equal(4, cropped.GetLength(0));
        Equal(4, cropped.GetLength(1));
        Equal(202, cropped[0, 0]);
        Equal(505, cropped[3, 3]);
        Throws<InvalidOperationException>(() => ActiveFrameCropper.ToAscomArray(source, 11, 4, out _, out _));
    }

    private static void WriteConfig(string root, string model, int minIso)
    {
        var config = new CameraConfig
        {
            ModelName = model,
            CameraXSize = 100,
            CameraYSize = 100,
            PixelSizeX = 3.0,
            PixelSizeY = 3.0,
            MaxAdu = 65535,
            DefaultMinSensitivity = minIso,
            DefaultMaxSensitivity = 12800,
            DefaultMinExposure = 0.001,
            DefaultMaxExposure = 3600,
            DefaultBulbCapable = true,
            SdkConstants = new SdkConstantConfig()
        };
        string path = Path.Combine(root, "CameraConfigs", model + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(config));
    }

    private static string FindRepositoryRoot()
    {
        string current = AppContext.BaseDirectory;
        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "Fuji.sln"))) return current;
            current = Directory.GetParent(current)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not find the Fujicom repository root.");
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void True(bool value, string message = "Expected true.")
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value) => True(!value, "Expected false.");

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static void Near(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.0000001)
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
}
