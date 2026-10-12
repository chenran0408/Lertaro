using Lertaro.App.Views.Controls.Results;

namespace Lertaro.App.Tests.Views.Controls.Results;

[TestClass]
public sealed class ResultClickGestureTests
{
    [TestMethod]
    public void PopupReleaseWithoutResultPressNeverLaunchesTheExposedRow()
    {
        var click = new ResultClickGesture();
        var row = new AppSearchResult();
        Assert.IsFalse(click.Release(row, true));
        click.Press(row, false);
        Assert.IsFalse(click.Release(row, true));
    }

    [TestMethod]
    public void OnlyTheSamePressedRowMayLaunchAndTheReleaseIsConsumedOnce()
    {
        var click = new ResultClickGesture();
        var row = new AppSearchResult();
        click.Press(row, true);
        Assert.IsFalse(click.Release(new AppSearchResult(), true));
        click.Press(row, true);
        Assert.IsTrue(click.Release(row, true));
        Assert.IsFalse(click.Release(row, true));
    }

    [TestMethod]
    public void MenuHandledReleaseHideAndDragCancelThePendingLaunch()
    {
        var click = new ResultClickGesture();
        var row = new AppSearchResult();
        click.Press(row, true);
        Assert.IsFalse(click.Release(row, false));
        Assert.IsFalse(click.Release(row, true));
        click.Press(row, true);
        click.Cancel();
        Assert.IsFalse(click.Release(row, true));
    }
}
