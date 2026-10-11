using System.ComponentModel;
using System.Collections.Specialized;
using Lertaro.App.ViewModels.QuickPanel;

namespace Lertaro.App.Views.QuickPanel;

// Observe only visible groups, so leaving a dragged tab restores the earlier pin state.
internal sealed class QuickPanelArrangePinSupport
{
    private readonly QuickPanelWindow _window;
    private readonly QuickPanelViewModel _viewModel;
    private readonly QuickPanelArrangePinState _state = new();
    private readonly HashSet<QuickPanelGroupViewModel> _groups = new();
    internal QuickPanelArrangePinSupport(QuickPanelWindow window, QuickPanelViewModel viewModel)
    {
        _window = window; _viewModel = viewModel;
        viewModel.Groups.CollectionChanged += Changed;
        window.Closed += (_, _) =>
        {
            viewModel.Groups.CollectionChanged -= Changed;
            foreach (var group in _groups) group.PropertyChanged -= GroupChanged;
        };
        Sync();
    }
    private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => Sync();
    private void GroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickPanelGroupViewModel.IsReordering)) Update();
    }
    private void Sync()
    {
        foreach (var group in _groups) group.PropertyChanged -= GroupChanged;
        _groups.Clear();
        foreach (var group in _viewModel.Groups) { _groups.Add(group); group.PropertyChanged += GroupChanged; }
        Update();
    }
    private void Update() => _window.IsStayOpen = _state.Update(_groups.Any(group => group.IsReordering), _window.IsStayOpen);
}
