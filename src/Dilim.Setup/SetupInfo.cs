using System.Reflection;

namespace Dilim.Setup;

/// <summary>Facts about this Setup program.</summary>
public static class SetupInfo
{
    /// <summary>GitHub repository from Directory.Build.props (DilimRepositoryUrl).</summary>
    public static string RepositoryUrl =>
        typeof(SetupInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value is { Length: > 0 } url ? url : Core.ProductIdentity.Website;

    /// <summary>The website: one big download button.</summary>
    public const string WebsiteUrl = Core.ProductIdentity.Website;

    /// <summary>The version this Setup program was built as (the payload says which Dilim it installs).</summary>
    public static string Version =>
        typeof(SetupInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
