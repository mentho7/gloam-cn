using System;
using Gloam.Localization.Wpf;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Gloam.Core.Calibration;

namespace Gloam.Features.Calibration
{
    /// <summary>
    /// Dialog for guiding users through ArgyllCMS USB driver installation.
    /// </summary>
    public partial class DriverInstallDialog : Window
    {
        private bool _driverInstalled;

        /// <summary>
        /// Gets whether the user wants to retry calibration after driver installation.
        /// </summary>
        public bool ShouldRetry { get; private set; }

        /// <summary>
        /// Gets whether the driver was installed successfully.
        /// </summary>
        public bool DriverInstalled => _driverInstalled;

        public DriverInstallDialog()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ShouldRetry = false;
            DialogResult = false;
            Close();
        }

        private void Install_Click(object sender, RoutedEventArgs e)
            => SafeAsync.FireAndForget(() => Install_ClickAsync(sender, e), nameof(Install_Click));

        private async Task Install_ClickAsync(object sender, RoutedEventArgs e)
        {
            // Check if driver installer is available
            if (!UsbDriverHelper.IsDriverInstallerAvailable())
            {
                ShowResult(false, Loc.Get("calibration.driver.installer.not.found"),
                    Loc.Get("calibration.the.argyllcms.usb.driver.installer.could.not.be.found"));
                return;
            }

            // Switch to installing view
            ExplanationPanel.Visibility = Visibility.Collapsed;
            InitialButtons.Visibility = Visibility.Collapsed;
            InstallingPanel.Visibility = Visibility.Visible;

            try
            {
                // Launch the installer and wait for it to complete
                DriverInstallerLaunchOutcome outcome = await UsbDriverHelper.LaunchDriverInstallerAsync();

                // A refused or missing installer must be reported, not papered over: claiming
                // success here would send the user looking for a driver that was never installed.
                switch (outcome)
                {
                    case DriverInstallerLaunchOutcome.InstallerNotFound:
                        ShowResult(false, Loc.Get("calibration.driver.installer.not.found"),
                            Loc.Get("calibration.the.argyllcms.usb.driver.installer.could.not.be.found"));
                        return;

                    case DriverInstallerLaunchOutcome.IntegrityCheckFailed:
                        ShowResult(false, Loc.Get("calibration.driver.installer.integrity.failed"),
                            Loc.Get("calibration.driver.installer.integrity.failed.detail"));
                        return;

                    case DriverInstallerLaunchOutcome.NotLaunched:
                        ShowResult(false, Loc.Get("calibration.failed.to.launch.installer"),
                            Loc.Get("calibration.if.the.installer.was.cancelled.or.failed.click.close"));
                        return;
                }

                // We consider it successful if the installer ran (user may have installed or cancelled)
                // The real test is whether calibration works afterward
                _driverInstalled = true;
                ShowResult(true, Loc.Get("calibration.driver.installer.completed"),
                    "If you installed the driver for your colorimeter, click 'Retry Calibration' to continue.\n\n" +
                    Loc.Get("calibration.if.the.installer.was.cancelled.or.failed.click.close"));
            }
            catch (Exception ex)
            {
                ShowResult(false, Loc.Get("calibration.failed.to.launch.installer"), ex.Message);
            }
        }

        private void ShowResult(bool success, string message, string detail)
        {
            InstallingPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
            ResultButtons.Visibility = Visibility.Visible;

            if (success)
            {
                ResultIcon.Text = "OK";
                ResultIcon.Foreground = (Brush)FindResource("SuccessBrush");
                RetryButton.Visibility = Visibility.Visible;
            }
            else
            {
                ResultIcon.Text = Loc.Get("calibration.failed");
                ResultIcon.Foreground = (Brush)FindResource("ErrorBrush");
                RetryButton.Visibility = Visibility.Collapsed;
            }

            ResultText.Text = message;
            ResultDetailText.Text = detail;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            ShouldRetry = false;
            DialogResult = _driverInstalled;
            Close();
        }

        private void Retry_Click(object sender, RoutedEventArgs e)
        {
            ShouldRetry = true;
            DialogResult = true;
            Close();
        }
    }
}
