using System.Windows.Controls;
using Xunit;

namespace Gloam.Tests;

public class CalibrationWorkflowTests
{
    [Fact]
    public void HistoricalReportIsNotPresentedAsCurrentVerification()
    {
        WpfTestHost.Run(() =>
        {
            var window = (CalibrationReportWindow)UiFixtures.BuildReport();
            try
            {
                Assert.Equal(CalibrationWorkflowStage.Historical, window.Vm.WorkflowStage);
                var banner = Assert.IsType<TextBlock>(window.FindName("WorkflowStatus"));
                window.Show();
                WpfTestHost.Pump(System.TimeSpan.FromMilliseconds(100));
                string historical = banner.Text;
                Assert.False(string.IsNullOrWhiteSpace(historical));
                window.Vm.WorkflowStage = CalibrationWorkflowStage.Cancelled;
                WpfTestHost.Pump(System.TimeSpan.FromMilliseconds(100));
                Assert.NotEqual(historical, banner.Text);
                Assert.False(string.IsNullOrWhiteSpace(banner.Text));
            }
            finally { window.Close(); }
        });
    }
}
