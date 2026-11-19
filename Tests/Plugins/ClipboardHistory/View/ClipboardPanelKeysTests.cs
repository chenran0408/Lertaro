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
            ClipboardPanelKeys.Resolve(Key.Enter, ModifierKeys.None, ClipboardPanelFocus.List, false).Action);
        Assert.AreEqual(ClipboardPanelAction.PasteSelected,
            ClipboardPanelKeys.Resolve(Key.Enter, ModifierKeys.None, ClipboardPanelFocus.Search, false).Action);
        Assert.AreEqual(ClipboardPanelAction.PasteSelectedPlainText,
            ClipboardPanelKeys.Resolve(Key.Enter, ModifierKeys.Control, ClipboardPanelFocus.List, false).Action);
    }

    [TestMethod]
    public void ArrowsNavigateFromSearch_WhileDeleteAndPinRequireListFocus()
    {
        Assert.AreEqual(ClipboardPanelAction.MoveUp,
            ClipboardPanelKeys.Resolve(Key.Up, ModifierKeys.None, ClipboardPanelFocus.List, false).Action);
        Assert.AreEqual(ClipboardPanelAction.MoveUp,
            ClipboardPanelKeys.Resolve(Key.Up, ModifierKeys.None, ClipboardPanelFocus.Search, false).Action);
        Assert.AreEqual(ClipboardPanelAction.DeleteSelected,
            ClipboardPanelKeys.Resolve(Key.Delete, ModifierKeys.None, ClipboardPanelFocus.List, false).Action);
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Delete, ModifierKeys.None, ClipboardPanelFocus.Search, false).Action);
        Assert.AreEqual(ClipboardPanelAction.TogglePinSelected,
            ClipboardPanelKeys.Resolve(Key.P, ModifierKeys.Control, ClipboardPanelFocus.List, false).Action);
        // In the search box Ctrl+P stays a text shortcut, not a pin.
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.P, ModifierKeys.Control, ClipboardPanelFocus.Search, false).Action);
    }

    [TestMethod]
    public void Escape_Closes_AndCtrlF_FocusesSearch_FromAnywhere()
    {
        Assert.AreEqual(ClipboardPanelAction.Close,
            ClipboardPanelKeys.Resolve(Key.Escape, ModifierKeys.None, ClipboardPanelFocus.Search, false).Action);
        Assert.AreEqual(ClipboardPanelAction.FocusSearch,
            ClipboardPanelKeys.Resolve(Key.F, ModifierKeys.Control, ClipboardPanelFocus.List, false).Action);
        Assert.AreEqual(ClipboardPanelAction.FocusSearch,
            ClipboardPanelKeys.Resolve(Key.F, ModifierKeys.Control, ClipboardPanelFocus.Search, false).Action);
    }

    [TestMethod]
    public void Digits_PasteByIndex_OnlyOutsideTheSearchBox()
    {
        var fromList = ClipboardPanelKeys.Resolve(Key.D3, ModifierKeys.None, ClipboardPanelFocus.List, false);
        Assert.AreEqual(ClipboardPanelAction.PasteByIndex, fromList.Action);
        Assert.AreEqual(3, fromList.Index);

        var fromBox = ClipboardPanelKeys.Resolve(Key.D3, ModifierKeys.None, ClipboardPanelFocus.Search, false);
        Assert.AreEqual(ClipboardPanelAction.None, fromBox.Action);

        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.D0, ModifierKeys.None, ClipboardPanelFocus.List, false).Action);
    }

    [TestMethod]
    public void UnboundKeys_DoNothing()
    {
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.A, ModifierKeys.None, ClipboardPanelFocus.List, false).Action);
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Space, ModifierKeys.None, ClipboardPanelFocus.List, false).Action);
    }

    [TestMethod]
    public void ShiftArrows_Extend_OnlyFromTheList()
    {
        Assert.AreEqual(ClipboardPanelAction.ExtendUp,
            ClipboardPanelKeys.Resolve(Key.Up, ModifierKeys.Shift, ClipboardPanelFocus.List, false).Action);
        Assert.AreEqual(ClipboardPanelAction.ExtendDown,
            ClipboardPanelKeys.Resolve(Key.Down, ModifierKeys.Shift, ClipboardPanelFocus.List, false).Action);
        // In the search box Shift+arrows stay native text selection.
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Up, ModifierKeys.Shift, ClipboardPanelFocus.Search, false).Action);
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Down, ModifierKeys.Shift, ClipboardPanelFocus.Search, false).Action);
    }

    [TestMethod]
    public void CtrlA_SelectsAllRows_OnlyFromTheList()
    {
        Assert.AreEqual(ClipboardPanelAction.SelectAll,
            ClipboardPanelKeys.Resolve(Key.A, ModifierKeys.Control, ClipboardPanelFocus.List, false).Action);
        // In the search box Ctrl+A stays the native select-all-text.
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.A, ModifierKeys.Control, ClipboardPanelFocus.Search, false).Action);
    }

    [TestMethod]
    public void PlainArrows_NeedBareArrows_OtherModifiersStayUnbound()
    {
        // Shift is claimed by extend, and Ctrl/Alt+arrows have no meaning -- keeping them unbound
        // prevents a held modifier from silently moving the selection.
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Up, ModifierKeys.Control, ClipboardPanelFocus.List, false).Action);
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Down, ModifierKeys.Control, ClipboardPanelFocus.List, false).Action);
    }

    [TestMethod]
    public void Escape_ClearsSearchFirst_ThenCloses()
    {
        // Typing state: the first Esc empties the query instead of killing the panel.
        Assert.AreEqual(ClipboardPanelAction.ClearSearch,
            ClipboardPanelKeys.Resolve(Key.Escape, ModifierKeys.None, ClipboardPanelFocus.Search, true).Action);
        // With an empty query, Esc closes. From a filtered list it clears the query first.
        Assert.AreEqual(ClipboardPanelAction.Close,
            ClipboardPanelKeys.Resolve(Key.Escape, ModifierKeys.None, ClipboardPanelFocus.Search, false).Action);
        Assert.AreEqual(ClipboardPanelAction.ClearSearch,
            ClipboardPanelKeys.Resolve(Key.Escape, ModifierKeys.None, ClipboardPanelFocus.List, true).Action);
        Assert.AreEqual(ClipboardPanelAction.Close,
            ClipboardPanelKeys.Resolve(Key.Escape, ModifierKeys.None, ClipboardPanelFocus.Search, false).Action);
    }

    [TestMethod]
    public void HomeEnd_JumpToTheEdges_OnlyFromTheList()
    {
        Assert.AreEqual(ClipboardPanelAction.MoveTop,
            ClipboardPanelKeys.Resolve(Key.Home, ModifierKeys.None, ClipboardPanelFocus.List, false).Action);
        Assert.AreEqual(ClipboardPanelAction.MoveBottom,
            ClipboardPanelKeys.Resolve(Key.End, ModifierKeys.None, ClipboardPanelFocus.List, false).Action);
        // In the search box they stay native caret moves.
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.Home, ModifierKeys.None, ClipboardPanelFocus.Search, false).Action);
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.End, ModifierKeys.None, ClipboardPanelFocus.Search, false).Action);
    }

    [DataRow(Key.Enter, ModifierKeys.None)]
    [DataRow(Key.Delete, ModifierKeys.None)]
    [DataRow(Key.D2, ModifierKeys.None)]
    [DataRow(Key.D2, ModifierKeys.Control)]
    [DataRow(Key.Up, ModifierKeys.None)]
    [TestMethod]
    public void PreviewControlsKeepTheirNativeKeys(Key key, ModifierKeys modifiers) =>
        Assert.AreEqual(ClipboardPanelAction.None, ClipboardPanelKeys.Resolve(key, modifiers, ClipboardPanelFocus.Other, true).Action);

    [TestMethod]
    public void CtrlDigitsPasteFromTheSearchBox_AndAltDigitsRemainUnbound()
    {
        Assert.AreEqual(new ClipboardPanelKeyResult(ClipboardPanelAction.PasteByIndex, 2),
            ClipboardPanelKeys.Resolve(Key.D2, ModifierKeys.Control, ClipboardPanelFocus.Search, true));
        Assert.AreEqual(new ClipboardPanelKeyResult(ClipboardPanelAction.PasteByIndex, 2),
            ClipboardPanelKeys.Resolve(Key.NumPad2, ModifierKeys.Control, ClipboardPanelFocus.Search, true));
        Assert.AreEqual(ClipboardPanelAction.None,
            ClipboardPanelKeys.Resolve(Key.D2, ModifierKeys.Alt, ClipboardPanelFocus.List, true).Action);
    }
}
