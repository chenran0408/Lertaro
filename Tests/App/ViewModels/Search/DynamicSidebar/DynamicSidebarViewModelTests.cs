using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.App.ViewModels.Search.DynamicSidebar;

namespace Lertaro.App.Tests.ViewModels.Search.DynamicSidebar;

[TestClass]
public sealed class DynamicSidebarViewModelTests
{
    [TestMethod]
    public void ClearSelectionSilently_ClearsSelectionWithoutInvokingSearchCallback()
    {
        var group = new SidebarFilterGroup
        {
            Id = "fixture",
            Header = "Fixture",
            AllowMultiSelect = true
        };
        group.Items.Add(new SidebarFilterItem
        {
            Id = "one",
            DisplayName = "One",
            MatchPredicate = _ => true
        });

        var viewModel = new DynamicSidebarGroupViewModel(group, mainVm: null!);
        viewModel.Items[0].SetSelectedSilently(true);

        Assert.IsTrue(viewModel.HasSelection);
        Assert.IsTrue(viewModel.ClearSelectionSilently());
        Assert.IsFalse(viewModel.HasSelection);
        Assert.IsFalse(viewModel.ClearSelectionSilently());
    }
}
