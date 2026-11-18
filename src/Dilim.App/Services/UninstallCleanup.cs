// Removes per-user integration and downloaded engines after Dilim exits. User settings remain for reinstalling.

using Dilim.Core.Platform;
using Dilim.Core.Settings;
using Dilim.Platform.Linux;
using Dilim.Platform.MacOS;
using Dilim.Platform.Windows;

namespace Dilim.App.Services;

internal static class UninstallCleanup
{
    public static bool Run()
    {
        IPlatformServices platform = OperatingSystem.IsWindows() ? new WindowsPlatformServices()
            : OperatingSystem.IsMacOS() ? new MacOSPlatformServices()
            : OperatingSystem.IsLinux() ? new LinuxPlatformServices() : new NullPlatformServices();
        var succeeded = true;
        // A copy with its own data folder (DILIM_DATA_DIR) never registered anything: only its engines go.
        var integrated = UserDataStore.OverrideDirectory is null;
        if (integrated)
        {
            Clean(() => platform.SetContextMenu(false, "", ""));
            Clean(() => platform.SetStartWithSystem(false, ""));
        }
        if (integrated && OperatingSystem.IsLinux() && platform is LinuxPlatformServices linux)
            Clean(() => linux.SetThunarShortcut(null));
        if (integrated && OperatingSystem.IsWindows() && ExplorerMenuRegistration.IsSupportedOs)
        {
            Clean(() =>
            {
                var task = ExplorerMenuRegistration.RemoveAsync(allowElevation: false);
                if (!task.Wait(TimeSpan.FromSeconds(40)))
                    throw new TimeoutException("Timed out removing the Explorer menu.");
                if (!task.Result.Succeeded)
                    throw new IOException(task.Result.Error ?? "Could not remove the Explorer menu.");
            });
        }
        if (succeeded)
        {
            Clean(() =>
            {
                var engines = Dilim.Engines.Infrastructure.EngineEnvironment.DownloadRoot;
                if (Directory.Exists(engines))
                    Directory.Delete(engines, recursive: true);
            });
        }
        return succeeded;

        void Clean(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                succeeded = false;
                Console.Error.WriteLine(ex.Message);
            }
        }
    }
}
