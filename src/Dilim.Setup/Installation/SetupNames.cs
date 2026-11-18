// Names Setup shares with Dilim 1.x's Inno Setup installer, the app and Windows. Changing one breaks updates.

namespace Dilim.Setup.Installation;

public static class SetupNames
{
    /// <summary>
    /// Inno Setup's AppId of Dilim (installer/Dilim.iss, 1.x). Apps &amp; Features lists Dilim under the uninstall key
    /// "{id}_is1"; Setup keeps that key, so an update from 1.x leaves one entry.
    /// </summary>
    public const string AppGuid = Core.ProductIdentity.InstallId;

    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{" + AppGuid + "}_is1";

    /// <summary>Dilim 1.1 and earlier: Velopack installed it per user and registered it here (HKCU).</summary>
    public const string VelopackUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Dilim";

    public const string MachineEnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    public const string UserEnvironmentKey = "Environment";

    public const string AppName = Core.ProductIdentity.Name;
    public const string Publisher = Core.ProductIdentity.Publisher;
    public const string AppExe = "Dilim.exe";

    /// <summary>
    /// What the app's executable was called before 0.3.0 (Dilim 0.2 still shipped it as Filee.exe). An update finds an
    /// installation by either name, and asks either one to quit.
    /// </summary>
    public static readonly IReadOnlyList<string> LegacyAppExes = ["Filee.exe"];

    /// <summary>The app's executable in <paramref name="folder"/>: this version's name first, then older ones; null when none.</summary>
    public static string? AppExeIn(string folder) =>
        new[] { AppExe }.Concat(LegacyAppExes).Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);

    /// <summary>The uninstaller next to Dilim.exe (UpdateService.IsInstalled looks for it).</summary>
    public const string UninstallerExe = "uninstall.exe";

    /// <summary>The list of installed files next to Dilim.exe: what the uninstaller removes and an update replaces.</summary>
    public const string InstallRecordFile = "uninstall.json";

    /// <summary>The "dilim" command (cli\dilim.cmd), the folder Setup puts on PATH.</summary>
    public const string CliFolder = "cli";

    /// <summary>Inno Setup's uninstaller files, removed when Setup takes over a 1.x installation.</summary>
    public static readonly IReadOnlyList<string> InnoUninstallerFiles = ["unins000.exe", "unins000.dat", "unins000.msg"];

    /// <summary>Start menu and desktop shortcut name.</summary>
    public const string ShortcutName = "Dilim.lnk";

    /// <summary>Registry values Setup adds to the uninstall key (Inno Setup kept the same in "Inno Setup: …").</summary>
    public const string TasksValue = "Dilim Setup: Tasks";
    public const string LanguageValue = "Dilim Setup: Language";
    public const string ScopeValue = "Dilim Setup: Scope";
    public const string InnoTasksValue = "Inno Setup: Selected Tasks";
    public const string InnoAppPathValue = "Inno Setup: App Path";
    public const string InnoVersionValue = "Inno Setup: Setup Version";
    public const string InnoLanguageValue = "Inno Setup: Language";

    /// <summary>Windows 10 1809: the oldest Windows Dilim runs on (Inno Setup's MinVersion).</summary>
    public const int MinimumBuild = 17763;

    /// <summary>Where Microsoft offers the Visual C++ Redistributable (x64).</summary>
    public const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
}
