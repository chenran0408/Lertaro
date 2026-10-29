using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lertaro.App.Views.QuickSearchWindow.Helpers;
using Lertaro.App.Tests;

namespace Lertaro.App.Tests.Views.QuickSearchWindow.Helpers;

[TestClass, DoNotParallelize]
public sealed class QuickSearchWindowFocusHelperTests
{
    [StaTestMethod]
    public void PendingFocus_DoesNotOverwriteNewCaretAfterHideAndReopen()
    {
        var (window, textBox, helper) = CreateWindow();
        try
        {
            textBox.Text = "old query";
            helper.FocusWhenForeground(new IntPtr(-1), selectSearchText: true);

            After(window.Dispatcher, 30, () =>
            {
                window.Hide();
                helper.CancelPendingFocus();
                textBox.Text = "new query";
            });
            After(window.Dispatcher, 100, () =>
            {
                window.Show();
                window.Activate();
                helper.FocusWhenForeground(IntPtr.Zero, selectSearchText: false, caretAtEnd: true);
            });
            PumpFor(window.Dispatcher, 350);

            Assert.IsTrue(window.IsVisible);
            Assert.AreSame(textBox, Keyboard.FocusedElement);
            Assert.AreEqual(textBox.Text.Length, textBox.CaretIndex);
            Assert.AreEqual(0, textBox.SelectionLength);
        }
        finally
        {
            window.Close();
        }
    }

    [StaTestMethod]
    public void NewFocusRequest_ReplacesPendingRequest()
    {
        var (window, textBox, helper) = CreateWindow();
        try
        {
            textBox.Text = "query";
            helper.FocusWhenForeground(new IntPtr(-1), selectSearchText: true);
            After(window.Dispatcher, 40, () =>
                helper.FocusWhenForeground(IntPtr.Zero, selectSearchText: false, caretAtEnd: true));
            PumpFor(window.Dispatcher, 300);

            Assert.AreSame(textBox, Keyboard.FocusedElement);
            Assert.AreEqual(textBox.Text.Length, textBox.CaretIndex);
            Assert.AreEqual(0, textBox.SelectionLength);
        }
        finally
        {
            window.Close();
        }
    }

    [StaTestMethod]
    public void FocusRequest_PreservesSelectAllAndCaretAtEndPriority()
    {
        var (window, textBox, helper) = CreateWindow();
        try
        {
            textBox.Text = "query";
            helper.FocusWhenForeground(IntPtr.Zero, selectSearchText: true, caretAtEnd: true);
            PumpFor(window.Dispatcher, 40);

            Assert.AreEqual(0, textBox.SelectionStart);
            Assert.AreEqual(textBox.Text.Length, textBox.SelectionLength);

            helper.FocusWhenForeground(IntPtr.Zero, selectSearchText: false, caretAtEnd: true);
            PumpFor(window.Dispatcher, 40);

            Assert.AreEqual(textBox.Text.Length, textBox.CaretIndex);
            Assert.AreEqual(0, textBox.SelectionLength);
        }
        finally
        {
            window.Close();
        }
    }

    [TestMethod]
    public void ResolveSearchBoxFocus_ClipboardHandoff_SelectsEverything() =>
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.SelectAll,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: true, caretAtEnd: false));

    [TestMethod]
    public void ResolveSearchBoxFocus_ClipboardHandoffBeatsCarriedOverQuery() =>
        // Select-all exists so the next keystroke replaces the imported text; parking the caret after it
        // instead would leave the two behaviours fighting over the same keystroke.
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.SelectAll,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: true, caretAtEnd: true));

    [TestMethod]
    public void ResolveSearchBoxFocus_CarriedOverQuery_ParksTheCaretAtTheEnd() =>
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.CaretAtEnd,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: false, caretAtEnd: true));

    [TestMethod]
    public void ResolveSearchBoxFocus_PlainSummon_LeavesTheCaretAlone() =>
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.LeaveCaret,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: false, caretAtEnd: false));

    private static (Window Window, TextBox TextBox, QuickSearchWindowFocusHelper Helper) CreateWindow()
    {
        var textBox = new TextBox { Width = 240 };
        var window = new Window { Width = 300, Height = 80, Content = textBox, ShowInTaskbar = false };
        window.Show();
        window.Activate();
        textBox.Focus();
        Keyboard.Focus(textBox);
        return (window, textBox, new QuickSearchWindowFocusHelper(textBox));
    }

    private static void After(Dispatcher dispatcher, int milliseconds, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private static void PumpFor(Dispatcher dispatcher, int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
