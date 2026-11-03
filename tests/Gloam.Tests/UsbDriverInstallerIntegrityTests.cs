using System.IO;
using Gloam.Core.Calibration;
using Xunit;

namespace Gloam.Tests;

/// <summary>
/// Regression coverage for the elevated-launch integrity gate in <see cref="UsbDriverHelper"/>.
/// </summary>
/// <remarks>
/// The installer is started with <c>Verb="runas"</c> from directories the current user can write
/// to, so a planted <c>ArgyllCMS_install_USB.exe</c> used to be a full local privilege escalation.
/// These tests pin the two halves of the fix: a file that is not a pinned build is never handed to
/// <c>Process.Start</c>, and the caller is told why instead of receiving a bare <c>false</c>.
///
/// A positive case (pinned bytes actually launch) is deliberately absent: exercising it would pop a
/// real UAC prompt and install a driver on the test machine. The pinned hash itself is verified in
/// review against the SHA-256 of Argyll_V3.5.0_win64_exe.zip, which is pinned in
/// <see cref="ArgyllDownloader.ArgyllArchiveSha256"/>.
/// </remarks>
public class UsbDriverInstallerIntegrityTests
{
    [Fact]
    public void Untrusted_installer_is_refused_instead_of_elevated()
    {
        string planted = CreateTempFile("this is not the ArgyllCMS installer");
        try
        {
            DriverInstallerLaunchOutcome outcome = UsbDriverHelper.LaunchVerified(planted, waitForExit: true);
            Assert.Equal(DriverInstallerLaunchOutcome.IntegrityCheckFailed, outcome);
        }
        finally
        {
            File.Delete(planted);
        }
    }

    [Fact]
    public async Task Untrusted_installer_is_refused_by_the_async_path_too()
    {
        string planted = CreateTempFile("still not the ArgyllCMS installer");
        try
        {
            DriverInstallerLaunchOutcome outcome = await UsbDriverHelper.LaunchVerifiedAsync(planted);
            Assert.Equal(DriverInstallerLaunchOutcome.IntegrityCheckFailed, outcome);
        }
        finally
        {
            File.Delete(planted);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_installer_path_is_reported_separately_from_a_refused_one(string? installerPath)
    {
        Assert.Equal(DriverInstallerLaunchOutcome.InstallerNotFound,
            UsbDriverHelper.LaunchVerified(installerPath, waitForExit: true));
    }

    [Fact]
    public void Nonexistent_path_is_reported_as_not_found()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"Gloam_missing_{System.Guid.NewGuid():N}.exe");
        Assert.Equal(DriverInstallerLaunchOutcome.InstallerNotFound,
            UsbDriverHelper.LaunchVerified(missing, waitForExit: true));
    }

    [Fact]
    public void Trust_check_rejects_arbitrary_bytes()
    {
        string planted = CreateTempFile("definitely not a pinned binary");
        try
        {
            Assert.False(UsbDriverHelper.IsTrustedInstaller(planted));
            Assert.False(UsbDriverHelper.IsTrustedInstaller(null));
            Assert.False(UsbDriverHelper.IsTrustedInstaller(""));
            Assert.False(UsbDriverHelper.IsTrustedInstaller(Path.Combine(Path.GetTempPath(), "nope.exe")));
        }
        finally
        {
            File.Delete(planted);
        }
    }

    private static string CreateTempFile(string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"Gloam_usbdriver_{System.Guid.NewGuid():N}.exe");
        File.WriteAllText(path, contents);
        return path;
    }
}
