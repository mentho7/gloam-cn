using Gloam.Core;
using Xunit;

namespace Gloam.Tests;
public class DisplayOutputStateTests
{
    [Fact]
    public void LateSuccessCannotHideNewFailure()
    {
        var store = new DisplayOutputStateStore();
        var old = store.Begin("display", default, 2700, OutputPolicySource.NightSchedule);
        var latest = store.Begin("display", default, 6500, OutputPolicySource.ManualDaylight);
        store.Complete("display", latest.Revision, OutputApplyStatus.Failed);
        store.Complete("display", old.Revision, OutputApplyStatus.Applied);
        Assert.Equal(OutputApplyStatus.Failed, store.Get("display")!.Status);
        Assert.Equal(6500, store.Get("display")!.Kelvin);
    }

    [Fact]
    public void SavingRequestIsNotSuccessAndDisplaysAreIsolated()
    {
        var store = new DisplayOutputStateStore();
        var a = store.Begin("a", default, 2700, OutputPolicySource.Preview);
        store.Begin("b", default, 6500, OutputPolicySource.Display);
        store.Complete("a", a.Revision, OutputApplyStatus.Bypassed);
        Assert.Equal(OutputApplyStatus.Pending, store.Get("b")!.Status);
        Assert.Equal(OutputApplyStatus.Bypassed, store.Get("a")!.Status);
    }
}
