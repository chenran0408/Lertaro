using System.IO;
using System.Security.Principal;
using Lertaro.App.Services.Update;

// CI provisions this host at the installed App path, which the service authenticates through the
// pipe's process handle. No production authorization check or updater implementation is replaced.
if (args.Length != 2)
    return 2;
try
{
    Lertaro.Core.Logger.Initialize("update-smoke.log", Path.GetDirectoryName(args[1]));
    using var identity = WindowsIdentity.GetCurrent();
    if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        throw new InvalidOperationException("The update probe must run as a standard user.");
    try
    {
        using var denied = File.Create(Path.Combine(AppContext.BaseDirectory, "standard-user-must-not-write"));
        throw new InvalidOperationException("The standard user can write to the protected installation.");
    }
    catch (UnauthorizedAccessException) { }

    var installer = UpdateInstaller.Instance;
    if (await installer.StartSilentUpdateAsync(args[0] + "/missing.zip"))
        throw new InvalidOperationException("A failed download was reported as a successful handoff.");
    if (!await installer.StartSilentUpdateAsync(args[0] + "/package.zip"))
        throw new InvalidOperationException("The signed update was not accepted after the failed download. " +
            string.Join(" | ", Lertaro.Core.Logger.ReadLogLines(Path.Combine(Lertaro.Core.Logger.LogDir, "update-smoke.log"))));
    File.WriteAllText(args[1], "standard-user;download-retry;verified;service-accepted");
    return 0;
}
catch (Exception ex)
{
    File.WriteAllText(args[1], ex.ToString());
    return 1;
}
