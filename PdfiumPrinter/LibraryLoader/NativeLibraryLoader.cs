#if !IOS && !MACCATALYST && !TVOS && !ANDROID
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
#endif

namespace PdfiumPrinter.LibraryLoader;

public static class NativeLibraryLoader
{
    private static ILibraryLoader defaultLibraryLoader;

    /// <summary>
    /// Sets the library loader used to load the native libraries. Overwrite this only if you want some custom loading.
    /// </summary>
    /// <param name="libraryLoader">The library loader to be used.</param>
    /// <remarks>
    /// It needs to be set before the first <seealso cref="PdfDocument"/> is created, otherwise it won't have any effect.
    /// </remarks>
    public static void SetLibraryLoader(ILibraryLoader libraryLoader)
    {
        defaultLibraryLoader = libraryLoader;
    }

    public static LoadResult LoadNativeLibrary(string path = default, bool bypassLoading = false)
    {

#if IOS || MACCATALYST || TVOS || ANDROID
        // If we're not bypass loading, and the path was set, and loader was set, allow it to go through.
        if (!bypassLoading && defaultLibraryLoader != null)
        {
            return defaultLibraryLoader.OpenLibrary(path);
        }

        return LoadResult.Success;
#else
        // If the user has handled loading the library themselves, we don't need to do anything.
        if (bypassLoading || RuntimeInformation.OSArchitecture.ToString() == "Wasm")
        {
            return LoadResult.Success;
        }

        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException($"Unsupported OS platform, architecture: {RuntimeInformation.OSArchitecture}")
        };

        var (platform, fileName) = Environment.OSVersion.Platform switch
        {
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => ("win", "pdfium.dll"),
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => ("linux", "libpdfium.so"),
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => ("osx", "libpdfium.dylib"),
            _ => throw new PlatformNotSupportedException($"Unsupported OS platform, architecture: {RuntimeInformation.OSArchitecture}")
        };

        var fullPath = Path.GetFullPath(fileName);
        if (File.Exists(fullPath))
        {
            return LoadTarget(platform, fullPath);
        }

        if (string.IsNullOrEmpty(path))
        {
            var assemblySearchPath = new[]
            {
                AppDomain.CurrentDomain.RelativeSearchPath,
                Path.GetDirectoryName(typeof(NativeMethods).Assembly.Location),
                Path.GetDirectoryName(Environment.GetCommandLineArgs()[0])
            }.Where(it => !string.IsNullOrEmpty(it)).FirstOrDefault();

            var isNetFramework = RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework");

            path = isNetFramework switch
            {
                true when string.IsNullOrEmpty(assemblySearchPath) => Path.Combine(architecture, fileName),
                true => Path.Combine(assemblySearchPath, architecture, fileName),
                false when string.IsNullOrEmpty(assemblySearchPath) => Path.Combine("runtimes", $"{platform}-{architecture}", "native", fileName),
                _ => Path.Combine(assemblySearchPath, "runtimes", $"{platform}-{architecture}", "native", fileName)
            };
        }

        if (defaultLibraryLoader != null)
        {
            return defaultLibraryLoader.OpenLibrary(path);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Native Library not found in path {path}. " +
                $"Verify you have included the native Pdfium library in your application, " +
                $"or install the default libraries with the bblanchon.PDFium NuGet.");
        }

        return LoadTarget(platform, path);
#endif
    }

    private static LoadResult LoadTarget(string platform, string fullPath)
    {
        ILibraryLoader libraryLoader = platform switch
        {
            "win" => new WindowsLibraryLoader(),
            "osx" => new MacOsLibraryLoader(),
            "linux" => new LinuxLibraryLoader(),
            _ => throw new PlatformNotSupportedException($"Currently {platform} platform is not supported")
        };

        var result = libraryLoader.OpenLibrary(fullPath);
        return result;
    }
}
