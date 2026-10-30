using System.Security.AccessControl;
using System.Security.Principal;

using static Lertaro.Core.Services.Installation.InstallDirectoryLock;

namespace Lertaro.Core.Services.Installation;

/// <summary>
/// Locks a portable copy's folder before the LocalSystem service is pointed at it. It was unzipped wherever
/// its user chose, usually somewhere every user can write, and the service would otherwise load whatever
/// executable or plugin DLL anyone put there.
/// </summary>
/// <remarks>
/// Split out of <see cref="InstallDirectoryLock"/>, which holds the walk itself; this is only the layout of a
/// portable copy. Everything becomes read-only for Users except the portable per-user data. <c>Data\Machine</c>
/// is the service's own. Under <c>Data\Users</c> each existing <c>&lt;SID hash&gt;</c> folder is handed back to
/// the account whose SID it hashes (found through ProfileList) and closed to everyone else; a folder whose
/// account is not on this machine goes to Administrators. A folder is also created now for every account that
/// has a profile, so another user cannot create it first and own that account's settings and plugins.
/// ponytail: an account whose profile appears after this ran is still open to that; the upgrade is the App
/// refusing a data folder it does not own.
///
/// The per-user part only applies when the copy keeps its data beside itself (a Data folder exists, which
/// resolving the service's own data directory has created by now if that is where it lives). A copy that fell
/// back to %ProgramData% and %LocalAppData% is left to keep doing so.
/// </remarks>
public static class PortableDirectoryLock
{
    // Bump only when a release requires a full walk, including protected descendants whose root
    // already has the right ACL. Lives in the administrator-owned binary directory, never user data.
    private const string PermissionsRevision = "2";
    private const string RevisionFile = ".permissions-version";
    public static Report RepairUserDirectory(string directory, SecurityIdentifier user)
    {
        Directory.CreateDirectory(directory);
        return InstallDirectoryLock.Lock(directory, OwnedBy(user), _ => null);
    }

    public static Report RepairProfileUserDirectory(string profile, SecurityIdentifier user)
    {
        // ProfileList supplies the trusted root. Pin each user-writable ancestor and refuse junctions
        // before resolving the final Lertaro folder; elevation must not repair an arbitrary link target.
        using var root = DirectoryLockNativeMethods.OpenWithoutFollowing(profile, migration: true);
        RejectLink(root);
        using var appData = DirectoryLockNativeMethods.OpenWithoutFollowing("AppData", root, migration: true);
        RejectLink(appData);
        using var local = DirectoryLockNativeMethods.OpenWithoutFollowing("Local", appData, migration: true);
        RejectLink(local);
        var directory = Path.Combine(profile, "AppData", "Local", "Lertaro");
        if (!Directory.Exists(directory)) return new Report();
        var report = RepairUserDirectory(directory, user);

        // A repair that returns without having changed the owner is the failure issue #342 could not see: the
        // walk reported success, every later start refused again, and the user had nothing to go on. The owner
        // is read back here, while the walk's own result is still in hand, and a mismatch is reported rather
        // than assumed away -- the App turns a non-empty Failed list into the reason it shows.
        var owner = UserDataAccess.TryGetOwner(directory);
        if (owner != null && !string.Equals(owner, user.Value, StringComparison.OrdinalIgnoreCase))
            report.Failed.Add($"{directory}: owner is still {owner}, expected {user.Value}");
        return report;

        static void RejectLink(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
        {
            if (((FileAttributes)DirectoryLockNativeMethods.GetInfo(handle).dwFileAttributes).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("The user profile data path contains a link; automatic elevated repair is refused.");
        }
    }

    /// <summary>
    /// A portable copy's <c>Data\Users</c>: every user may create a folder here (only here, not below) and
    /// owns what it creates, through CREATOR OWNER. Nobody gets into anyone else's.
    /// </summary>
    internal static Zone UsersDirectory { get; } = new(Administrators,
    [
        Allow(LocalSystem, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
        Allow(Administrators, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
        Allow(Users, FileSystemRights.ReadAndExecute | FileSystemRights.CreateDirectories, AceFlags.None),
        Allow(new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null), FileSystemRights.FullControl,
            AceFlags.ObjectInherit | AceFlags.ContainerInherit | AceFlags.InheritOnly),
    ]);

    /// <summary>One user's own data folder: that user, SYSTEM and Administrators, owned by the user.</summary>
    internal static Zone OwnedBy(SecurityIdentifier user) => new(user,
    [
        Allow(LocalSystem, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
        Allow(Administrators, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
        Allow(user, FileSystemRights.FullControl, AceFlags.ObjectInherit | AceFlags.ContainerInherit),
    ], PreserveLinks: true);

    public static Report Lock(string appDirectory)
    {
        appDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        var profileSids = UserProfiles.Read().Keys;
        var (zoneFor, userFolders) = Zones(appDirectory, profileSids);

        var dataDirectory = Path.Combine(appDirectory, "Data");
        var revisionPath = Path.Combine(appDirectory, RevisionFile);
        if (Directory.Exists(dataDirectory) &&
            SettingsFileReader.ReadIfPresent(revisionPath) == PermissionsRevision &&
            GrantsAtLeast(appDirectory, ReadOnlyForUsers) &&
            ExistingUsersRootIsCurrent(appDirectory, zoneFor))
        {
            var fastReport = EnsureUserDirectories(appDirectory, zoneFor, userFolders);
            if (fastReport.Failed.Count == 0 && fastReport.UserDataFailed.Count == 0)
                AtomicFileStore.Write(revisionPath, PermissionsRevision);
            return fastReport;
        }

        // A failed walk must not leave a previous success marker suppressing the next repair.
        File.Delete(revisionPath);
        var report = InstallDirectoryLock.Lock(appDirectory, ReadOnlyForUsers, zoneFor);
        if (!Directory.Exists(dataDirectory))
        {
            if (report.Failed.Count == 0 && report.UserDataFailed.Count == 0) AtomicFileStore.Write(revisionPath, PermissionsRevision);
            return report;
        }

        Merge(report, EnsureUserDirectories(appDirectory, zoneFor, userFolders));
        if (report.Failed.Count == 0 && report.UserDataFailed.Count == 0) AtomicFileStore.Write(revisionPath, PermissionsRevision);
        return report;
    }

    internal static Report EnsureUserDirectories(string appDirectory, Func<string, Zone?> zoneFor,
        IReadOnlyList<string> userFolders)
    {
        var report = new Report();
        // The application tree is already locked before this method runs, so a new account's directory
        // can be created safely without walking the unrelated machine index subtree.
        foreach (var folder in userFolders.Prepend(Path.Combine(appDirectory, "Data", "Users")))
        {
            var zone = zoneFor(folder)!;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Merge(report, InstallDirectoryLock.Lock(folder, zone, zoneFor));
            }
            else if (!GrantsAtLeast(folder, zone))
            {
                Merge(report, InstallDirectoryLock.Lock(folder, zone, zoneFor));
            }
        }
        return report;
    }

    private static bool ExistingUsersRootIsCurrent(string appDirectory, Func<string, Zone?> zoneFor)
    {
        var usersRoot = Path.Combine(appDirectory, "Data", "Users");
        return Directory.Exists(usersRoot) && GrantsAtLeast(usersRoot, zoneFor(usersRoot)!);
    }

    /// <summary>
    /// Whether the copy at <paramref name="appDirectory"/> is still locked the way <see cref="Lock"/> does it
    /// now. False for a copy installed before the lock existed, or locked by a build whose zones were wrong
    /// (5.8.2 granted Users read without SYNCHRONIZE, which refused even starting the App: issue #316). The
    /// service applies updates in place, without running --install again, so it checks this on every start.
    /// </summary>
    public static bool IsCurrent(string appDirectory)
    {
        appDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        if (SettingsFileReader.ReadIfPresent(Path.Combine(appDirectory, RevisionFile)) != PermissionsRevision) return false;
        return IsCurrent(appDirectory, Directory.Exists(Path.Combine(appDirectory, "Data")),
            UserProfiles.Read().Keys, (folder, zone) => Directory.Exists(folder) && GrantsAtLeast(folder, zone));
    }

    internal static bool IsCurrent(string appDirectory, bool hasPortableData, IEnumerable<string> profileSids,
        Func<string, Zone, bool> grantsAtLeast)
    {
        if (!grantsAtLeast(appDirectory, ReadOnlyForUsers))
            return false;
        if (!hasPortableData)
            return true;

        // #327: a correct binary-directory ACL says nothing about the separately protected user zones.
        var (zoneFor, userFolders) = Zones(appDirectory, profileSids);
        return userFolders.Prepend(Path.Combine(appDirectory, "Data", "Users"))
            .All(folder => grantsAtLeast(folder, zoneFor(folder)!));
    }

    /// <summary>
    /// Which zone starts where in a portable copy at <paramref name="appDirectory"/> (null: inherit), and the
    /// per-user folders that belong to the accounts in <paramref name="profileSids"/>.
    /// </summary>
    internal static (Func<string, Zone?> ZoneFor, IReadOnlyList<string> UserFolders) Zones(string appDirectory, IEnumerable<string> profileSids)
    {
        var users = Path.Combine(appDirectory, "Data", "Users");
        var indexes = Path.Combine(appDirectory, "Data", "Machine", "indexes");
        var logs = Path.Combine(appDirectory, "Data", "Machine", "logs");
        var legacyLog = Path.Combine(appDirectory, "service.log");
        var userFolders = profileSids
            .Where(UserProfiles.IsAccount)
            // Logger passes SidHash to ResolveUser, which hashes it again. Preserve existing data paths
            // and recognize both names; otherwise the live folder is locked as an unknown account (#327).
            .SelectMany(sid => new[] { CurrentUserIdentity.Hash(sid), CurrentUserIdentity.Hash(CurrentUserIdentity.Hash(sid)) }
                .Select(hash => (Path: Path.Combine(users, hash), Sid: sid)))
            .ToDictionary(user => user.Path, user => OwnedBy(new SecurityIdentifier(user.Sid)),
                StringComparer.OrdinalIgnoreCase);

        Zone? ZoneFor(string path) =>
            string.Equals(path, users, StringComparison.OrdinalIgnoreCase) ? UsersDirectory
            : string.Equals(Path.GetDirectoryName(path), users, StringComparison.OrdinalIgnoreCase)
                ? userFolders.GetValueOrDefault(path) ?? PrivateToService
            : string.Equals(path, indexes, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(path, logs, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(path, legacyLog, StringComparison.OrdinalIgnoreCase) ? PrivateToService
            : null;

        return (ZoneFor, userFolders.Keys.ToList());
    }
}
