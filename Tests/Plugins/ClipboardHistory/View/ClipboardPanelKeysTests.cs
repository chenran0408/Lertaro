using System.Windows.Input;
using Lertaro.Plugins.ClipboardHistory.View;

namespace Lertaro.Plugins.ClipboardHistory.Tests.View;

[TestClass]
public sealed class ClipboardPanelKeysTests
{
    [TestMethod]
    public void Enter_Pastes_AndCtrlEnterPastesAsPlainText()
    {
        Assert.AreEqual(ClipboardPanelAction.PasteSelected,
            ClipboardPanelKeys.Resolve(Key.Enter, ModifierKeys.None, searchFocused: false).Action);
        Assert.AreEqual(ClipboardPanelAction.PasteSelected,
            ClipboardPanelKeys.Resolve(Key.Enter, ModifierKeys.None, searchFocused: true).Action);
        Assert.AreEqual(ClipboardPanelAction.PasteSelectedPlainText,
            ClipboardPanelKeys.Resolve(Key.Enter, ModifierKeys.Control, searchFocused: false).Action);
    }

    [TestMethod]
    public void Navigation_Del_Pin_OnlyFromTheList()
    {
        Assert.AreEqual(ClipboardPanelAction.MoveUp,
            ClipboardPanelKeys.Resolve(Key.Up, ModifierKeys.None, searchFocused: false).Action);
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Up, ModifierKeys.None, searchFocused: true).Action);
        Assert.AreEqual(ClipboardPanelAction.DeleteSelected,
            ClipboardPanelKeys.Resolve(Key.Delete, ModifierKeys.None, searchFocused: false).Action);
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Delete, ModifierKeys.None, searchFocused: true).Action);
        Assert.AreEqual(ClipboardPanelAction.TogglePinSelected,
            ClipboardPanelKeys.Resolve(Key.P, ModifierKeys.Control, searchFocused: false).Action);
        // In the search box Ctrl+P stays a text shortcut, not a pin.
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.P, ModifierKeys.Control, searchFocused: true).Action);
    }

    [TestMethod]
    public void Escape_Closes_AndCtrlF_FocusesSearch_FromAnywhere()
    {
        Assert.AreEqual(ClipboardPanelAction.Close,
            ClipboardPanelKeys.Resolve(Key.Escape, ModifierKeys.None, searchFocused: true).Action);
        Assert.AreEqual(ClipboardPanelAction.FocusSearch,
            ClipboardPanelKeys.Resolve(Key.F, ModifierKeys.Control, searchFocused: false).Action);
        Assert.AreEqual(ClipboardPanelAction.FocusSearch,
            ClipboardPanelKeys.Resolve(Key.F, ModifierKeys.Control, searchFocused: true).Action);
    }

    [TestMethod]
    public void Digits_PasteByIndex_OnlyOutsideTheSearchBox()
    {
        var fromList = ClipboardPanelKeys.Resolve(Key.D3, ModifierKeys.None, searchFocused: false);
        Assert.AreEqual(ClipboardPanelAction.PasteByIndex, fromList.Action);
        Assert.AreEqual(3, fromList.Index);

        var fromBox = ClipboardPanelKeys.Resolve(Key.D3, ModifierKeys.None, searchFocused: true);
        Assert.AreEqual(ClipboardPanelAction.None, fromBox.Action);

        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.D0, ModifierKeys.None, searchFocused: false).Action);
    }

    [TestMethod]
    public void UnboundKeys_DoNothing()
    {
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.A, ModifierKeys.None, searchFocused: false).Action);
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Space, ModifierKeys.None, searchFocused: false).Action);
    }

    [TestMethod]
    public void ShiftArrows_Extend_OnlyFromTheList()
    {
        Assert.AreEqual(ClipboardPanelAction.ExtendUp,
            ClipboardPanelKeys.Resolve(Key.Up, ModifierKeys.Shift, searchFocused: false).Action);
        Assert.AreEqual(ClipboardPanelAction.ExtendDown,
            ClipboardPanelKeys.Resolve(Key.Down, ModifierKeys.Shift, searchFocused: false).Action);
        // In the search box Shift+arrows stay native text selection.
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Up, ModifierKeys.Shift, searchFocused: true).Action);
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Down, ModifierKeys.Shift, searchFocused: true).Action);
    }

    [TestMethod]
    public void CtrlA_SelectsAllRows_OnlyFromTheList()
    {
        Assert.AreEqual(ClipboardPanelAction.SelectAll,
            ClipboardPanelKeys.Resolve(Key.A, ModifierKeys.Control, searchFocused: false).Action);
        // In the search box Ctrl+A stays the native select-all-text.
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.A, ModifierKeys.Control, searchFocused: true).Action);
    }

    [TestMethod]
    public void PlainArrows_NeedBareArrows_OtherModifiersStayUnbound()
    {
        // Shift is claimed by extend, and Ctrl/Alt+arrows have no meaning -- keeping them unbound
        // prevents a held modifier from silently moving the selection.
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Up, ModifierKeys.Control, searchFocused: false).Action);
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Down, ModifierKeys.Control, searchFocused: false).Action);
    }
}
