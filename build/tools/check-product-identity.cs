#:project ../../src/Dilim.Platform.Windows/Dilim.Platform.Windows.csproj
using Dilim.Platform.Windows;
if (!OperatingSystem.IsWindows()) return 0;
var family = ExplorerMenuRegistration.ComputeFamilyName();
Console.WriteLine(family);
return family == ExplorerMenuPackage.FamilyName ? 0 : 1;
