using Lertaro.App.Views.QuickPanel;

namespace Lertaro.App.Tests.Views.QuickPanel;

[TestClass]
public sealed class QuickPanelArrangePinStateTests
{
    [TestMethod]
    public void RepeatedUpdatesKeepTheOriginalPinUntilAllArrangingEnds()
    {
        var state = new QuickPanelArrangePinState();
        Assert.IsTrue(state.Update(true, false));
        Assert.IsTrue(state.Update(true, true));
        Assert.IsFalse(state.Update(false, true));
        Assert.IsTrue(state.Update(false, true));
    }
    [TestMethod]
    public void AnAlreadyPinnedPanelStaysPinnedAfterArranging()
    {
        var state = new QuickPanelArrangePinState();
        Assert.IsTrue(state.Update(true, true));
        Assert.IsTrue(state.Update(false, true));
    }
}
