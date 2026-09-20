using System.Reflection;
using Vora.Application.Settings.ViewModels;

namespace Vora.Application.Settings;

public static class ServerVersion
{
    public static ServerVersionVM Read(Assembly assembly) =>
        Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
              ?? assembly.GetName().Version?.ToString());

    public static ServerVersionVM Parse(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return new ServerVersionVM { Version = "unknown" };
        }

        var plusIndex = informationalVersion.IndexOf('+');
        var version = plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;
        var commit = plusIndex >= 0 && plusIndex + 1 < informationalVersion.Length
            ? informationalVersion[(plusIndex + 1)..]
            : null;

        return new ServerVersionVM
        {
            Version = version,
            Commit = string.IsNullOrWhiteSpace(commit) || commit == "local" ? null : commit,
            IsPrerelease = version.Contains('-')
        };
    }
}
