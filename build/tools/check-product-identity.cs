#:project ../../src/Filee.Platform.Windows/Filee.Platform.Windows.csproj
using Filee.Platform.Windows;
if (!OperatingSystem.IsWindows()) return 0;
var family = ExplorerMenuRegistration.ComputeFamilyName();
Console.WriteLine(family);
return family == ExplorerMenuPackage.FamilyName ? 0 : 1;
