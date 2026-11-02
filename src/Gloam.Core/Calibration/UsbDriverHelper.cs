using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Gloam.Core.Calibration
{
    /// <summary>
    /// Outcome of an attempt to start the elevated ArgyllCMS USB driver installer.
    /// </summary>
    /// <remarks>
    /// The distinction matters because "the installer did not run" has three very different
    /// causes, and each needs its own message: no installer exists, an installer exists but
    /// failed the integrity check, or elevation was declined.
    /// </remarks>
    public enum DriverInstallerLaunchOutcome
    {
        /// <summary>The elevated installer process was started (and, when awaited, exited 0).</summary>
        Started,

        /// <summary>The installer ran but exited non-zero - typically the user cancelled it.</summary>
        CancelledOrFailed,

        /// <summary>The process could not be started at all, e.g. the UAC prompt was declined.</summary>
        NotLaunched,

        /// <summary>No installer file was found in any candidate location.</summary>
        InstallerNotFound,

        /// <summary>An installer file exists but is not a pinned build, so elevation was refused.</summary>
        IntegrityCheckFailed,
    }

    /// <summary>
    /// Helper class for managing ArgyllCMS USB driver installation.
    /// </summary>
    public static class UsbDriverHelper
    {
        /// <summary>
        /// SHA-256 of <c>ArgyllCMS_install_USB.exe</c> as shipped inside
        /// <c>Argyll_V3.5.0_win64_exe.zip</c> - the same archive pinned by
        /// <see cref="ArgyllDownloader.ArgyllArchiveSha256"/>.
        /// </summary>
        /// <remarks>
        /// SECURITY: every candidate location this class searches (the app's own
        /// <c>argyll_cache</c>, <c>%LOCALAPPDATA%\Argyll</c>, DisplayCAL's AppData download
        /// directory) is writable by the current user, so "the file exists" proves nothing about
        /// its provenance. This process is started with <c>Verb="runas"</c>, so launching a
        /// planted file would hand a standard user full local administrator code execution.
        /// The hash is the only thing tying the elevated binary to the archive we pinned, hence
        /// the file must match one of these before <c>Process.Start</c> is reached.
        /// Add a hash here (with the version it came from) when bumping ArgyllCMS.
        /// </remarks>
        private static readonly string[] TrustedInstallerSha256 =
        {
            "D13D0A94748BD2559565C4DDB5BAE103EB60A0DA1E3E3F58482780900D7289AF", // ArgyllCMS 3.5.0
        };

        /// <summary>
        /// Gets the path to the ArgyllCMS USB driver installer if available.
        /// </summary>
        /// <remarks>
        /// The result is a location, not a trust decision: it can point at a file planted in a
        /// user-writable directory. Anything that starts it must go through
        /// <see cref="LaunchDriverInstaller"/> / <see cref="LaunchDriverInstallerAsync"/>, which
        /// verify the bytes against <see cref="TrustedInstallerSha256"/> before elevating.
        /// </remarks>
        public static string? GetDriverInstallerPath()
        {
            string? discoveredBin = ArgyllPathFinder.FindArgyllBinPath();
            if (!string.IsNullOrEmpty(discoveredBin))
            {
                string? root = Directory.GetParent(discoveredBin)?.FullName;
                if (root != null)
                {
                    string discovered = Path.Combine(root, "usb", "ArgyllCMS_install_USB.exe");
                    if (File.Exists(discovered)) return discovered;
                }
            }

            // Check our downloaded ArgyllCMS first
            string localArgyllDir = Path.Combine(AppPaths.DataDir, "Argyll");

            string installerPath = Path.Combine(localArgyllDir, "usb", "ArgyllCMS_install_USB.exe");
            if (File.Exists(installerPath))
                return installerPath;

            // Check standard installation paths
            var searchPaths = new[]
            {
                @"C:\Program Files\ArgyllCMS",
                @"C:\Program Files (x86)\ArgyllCMS",
                @"C:\Program Files\Argyll_V3.5.0",
                @"C:\Program Files\Argyll_V3.3.0",
                @"C:\ArgyllCMS"
            };

            foreach (var basePath in searchPaths)
            {
                installerPath = Path.Combine(basePath, "usb", "ArgyllCMS_install_USB.exe");
                if (File.Exists(installerPath))
                    return installerPath;
            }

            return null;
        }

        /// <summary>
        /// Gets the path to the ArgyllCMS USB driver uninstaller if available.
        /// </summary>
        public static string? GetDriverUninstallerPath()
        {
            string? installerPath = GetDriverInstallerPath();
            if (installerPath == null)
                return null;

            string uninstallerPath = Path.Combine(
                Path.GetDirectoryName(installerPath)!,
                "ArgyllCMS_uninstall_USB.exe");

            return File.Exists(uninstallerPath) ? uninstallerPath : null;
        }

        /// <summary>
        /// Checks if the driver installer is available.
        /// </summary>
        public static bool IsDriverInstallerAvailable() => GetDriverInstallerPath() != null;

        /// <summary>
        /// True when <paramref name="path"/> is byte-for-byte one of the pinned installer builds.
        /// </summary>
        internal static bool IsTrustedInstaller(string? path) =>
            MatchesPinnedHash(path, TrustedInstallerSha256, "installer");

        /// <summary>
        /// Launches the ArgyllCMS USB driver installer.
        /// </summary>
        /// <param name="waitForExit">If true, waits for the installer to complete.</param>
        public static DriverInstallerLaunchOutcome LaunchDriverInstaller(bool waitForExit = true) =>
            LaunchVerified(GetDriverInstallerPath(), waitForExit);

        /// <summary>
        /// Launches the driver installer asynchronously.
        /// </summary>
        public static Task<DriverInstallerLaunchOutcome> LaunchDriverInstallerAsync() =>
            LaunchVerifiedAsync(GetDriverInstallerPath());

        /// <summary>
        /// Launch body, split out so the integrity gate can be exercised without a real elevated
        /// install. <paramref name="installerPath"/> is never trusted because it came from a
        /// caller: it still has to match <see cref="TrustedInstallerSha256"/>.
        /// </summary>
        internal static DriverInstallerLaunchOutcome LaunchVerified(string? installerPath, bool waitForExit)
        {
            using FileStream? verified = OpenVerifiedInstaller(installerPath, out DriverInstallerLaunchOutcome refusal);
            if (verified == null)
                return refusal;

            try
            {
                var process = Process.Start(new ProcessStartInfo(verified.Name)
                {
                    UseShellExecute = true,
                    Verb = "runas" // Request admin elevation
                });
                if (process == null)
                    return DriverInstallerLaunchOutcome.NotLaunched;

                if (!waitForExit)
                    return DriverInstallerLaunchOutcome.Started;

                process.WaitForExit();
                return process.ExitCode == 0
                    ? DriverInstallerLaunchOutcome.Started
                    : DriverInstallerLaunchOutcome.CancelledOrFailed;
            }
            catch (Exception ex)
            {
                Log.Info($"Failed to launch driver installer: {ex.Message}");
                return DriverInstallerLaunchOutcome.NotLaunched;
            }
        }

        /// <inheritdoc cref="LaunchVerified"/>
        internal static async Task<DriverInstallerLaunchOutcome> LaunchVerifiedAsync(string? installerPath)
        {
            using FileStream? verified = OpenVerifiedInstaller(installerPath, out DriverInstallerLaunchOutcome refusal);
            if (verified == null)
                return refusal;

            try
            {
                var process = Process.Start(new ProcessStartInfo(verified.Name)
                {
                    UseShellExecute = true,
                    Verb = "runas" // Request admin elevation
                });
                if (process == null)
                    return DriverInstallerLaunchOutcome.NotLaunched;

                await process.WaitForExitAsync();
                return process.ExitCode == 0
                    ? DriverInstallerLaunchOutcome.Started
                    : DriverInstallerLaunchOutcome.CancelledOrFailed;
            }
            catch (Exception ex)
            {
                Log.Info($"Failed to launch driver installer: {ex.Message}");
                return DriverInstallerLaunchOutcome.NotLaunched;
            }
        }

        /// <summary>
        /// Opens the installer only if its bytes match a pinned hash, keeping the handle open for
        /// the caller. Holding a <see cref="FileShare.Read"/> handle denies write and delete to
        /// everyone else, so the verified bytes are also the bytes that get executed - without it,
        /// an attacker with write access to the candidate directory could swap the file between the
        /// hash check and <c>Process.Start</c>.
        /// </summary>
        /// <param name="refusal">
        /// Why the stream is null: <see cref="DriverInstallerLaunchOutcome.InstallerNotFound"/> or
        /// <see cref="DriverInstallerLaunchOutcome.IntegrityCheckFailed"/>.
        /// </param>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the verified stream passes to the caller, which holds it " +
                            "for the duration of the elevated launch and disposes it via a using declaration.")]
        private static FileStream? OpenVerifiedInstaller(string? installerPath, out DriverInstallerLaunchOutcome refusal)
        {
            refusal = DriverInstallerLaunchOutcome.InstallerNotFound;
            if (string.IsNullOrEmpty(installerPath) || !File.Exists(installerPath))
                return null;

            FileStream stream;
            try
            {
                stream = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex)
            {
                // Cannot hash it, so cannot vouch for it: fail closed.
                Log.Info($"UsbDriverHelper: cannot open driver installer '{installerPath}': {ex.Message}");
                refusal = DriverInstallerLaunchOutcome.IntegrityCheckFailed;
                return null;
            }

            try
            {
                string actual = Convert.ToHexString(SHA256.HashData(stream));
                if (TrustedInstallerSha256.Any(pinned => pinned.Equals(actual, StringComparison.OrdinalIgnoreCase)))
                    return stream;

                Log.Info($"UsbDriverHelper: refusing to elevate '{installerPath}' - SHA-256 {actual} " +
                         "does not match the pinned ArgyllCMS USB driver installer.");
            }
            catch (Exception ex)
            {
                Log.Info($"UsbDriverHelper: cannot hash driver installer '{installerPath}': {ex.Message}");
            }

            stream.Dispose();
            refusal = DriverInstallerLaunchOutcome.IntegrityCheckFailed;
            return null;
        }

        private static bool MatchesPinnedHash(string? path, string[] pinned, string label)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                string actual = Convert.ToHexString(SHA256.HashData(stream));
                return pinned.Any(expected => expected.Equals(actual, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Info($"UsbDriverHelper: cannot hash {label} '{path}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks whether an error really indicates the ArgyllCMS USB driver is missing.
        /// Must be narrow: false positives here cause the app to prompt for reinstalling a
        /// driver that is already installed, which is confusing and unhelpful.
        /// </summary>
        /// <remarks>
        /// Things that are NOT driver errors even though they appear in spotread output:
        ///   - "SetCommState failed" / "LastError 31": benign serial-port enumeration
        ///     noise printed on every spotread invocation regardless of colorimeter state.
        ///   - "err 32" / "LastError 32" / "sharing violation": ERROR_SHARING_VIOLATION —
        ///     another process holds the HID handle. Reinstalling the driver doesn't help;
        ///     the user needs to close the holder or unplug/replug.
        /// </remarks>
        public static bool IsDriverError(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
                return false;

            // Genuine driver-missing signals from ArgyllCMS / libusb:
            //  - "Couldn't open USB device" (libusb_open failed without the Argyll driver)
            //  - "Driver not found" / "No driver installed"
            //  - libusb0/winusb specific failures
            return errorMessage.Contains("libusb", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("winusb", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("driver not found", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("no driver installed", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("could not open USB", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("couldn't open USB", StringComparison.OrdinalIgnoreCase);
        }
    }
}
