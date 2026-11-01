using System.Reflection;
using System.Runtime.InteropServices;
using Lertaro.Plugins.ClipboardHistory.Capture;

namespace Lertaro.Plugins.ClipboardHistory.Tests.Capture;

// Verifies every Win32ClipboardNative P/Invoke resolves to a real OS export
// without ever touching the clipboard: loads the declared library and resolves
// each declared entry point. Catches a missing DllImport EntryPoint when the
// managed method name diverges from the native one (EntryPointNotFoundException).
// The test assembly targets net10.0-windows, so user32/kernel32 are guaranteed.
[TestClass]
public sealed class Win32ClipboardNativeBindingTests
{
    [TestMethod]
    public void AllDllImportMethods_ResolveToRealNativeExports()
    {
        var imports = typeof(Win32ClipboardNative)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<DllImportAttribute>()))
            .Where(import => import.Attribute != null)
            .ToList();

        Assert.IsGreaterThan(0, imports.Count);

        foreach (var (method, attribute) in imports)
        {
            var entryPoint = attribute!.EntryPoint ?? method.Name;
            var library = NativeLibrary.Load(attribute.Value);
            try
            {
                Assert.IsTrue(
                    NativeLibrary.TryGetExport(library, entryPoint, out _),
                    $"{attribute.Value}!{entryPoint} declared by {method.Name}");
            }
            finally
            {
                NativeLibrary.Free(library);
            }
        }
    }
}
