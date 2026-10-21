#:project ../../src/Filee.Engines/Filee.Engines.csproj
using System.Net;
using Filee.Engines.Infrastructure;

// Exercise production engine installation using the exact checksum-pinned archives already fetched by the build.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/engine-install-check");
Directory.CreateDirectory(root);
using var http = new HttpClient(new CachedArchives(Path.GetFullPath("build/.cache")));
var installer = new EngineInstaller(http, root);
foreach (var id in new[] { "7zip", "ffmpeg" })
{
    var package = EngineDownloads.Packages.Single(p => p.Id == id);
    await installer.InstallAsync(package, null, default);
    if (!installer.IsInstalled(package) || !installer.IsUpToDate(package)) throw new IOException(id + ": missing verified marker");
    var folder = EngineEnvironment.FolderWithPrograms(id, Path.Combine(root, id)) ?? throw new IOException(id + ": missing programs");
    var program = Path.Combine(folder, id == "7zip" ? "7z.exe" : "ffmpeg.exe");
    var check = await ProcessRunner.RunAsync(program, id == "7zip" ? ["i"] : ["-hide_banner", "-version"], TimeSpan.FromSeconds(30), default);
    if (check.ExitCode != 0) throw new IOException(id + ": cannot run: " + check.StandardError);
    Console.WriteLine(id + ": pinned checksum, extraction, marker and executable passed");
}
return 0;

sealed class CachedArchives(string cache) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var name = Uri.UnescapeDataString(Path.GetFileName(request.RequestUri!.AbsolutePath));
        var path = Path.Combine(cache, name);
        if (!File.Exists(path)) throw new FileNotFoundException("Fetch the pinned engine archive first.", path);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(path)) });
    }
}
