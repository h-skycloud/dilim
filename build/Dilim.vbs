Option Explicit
Dim shell, files, folder, executable, environment
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
folder = files.GetParentFolderName(WScript.ScriptFullName)
executable = files.BuildPath(folder, "Filee.exe")
If Not files.FileExists(executable) Then
  MsgBox "Filee.exe bulunamadi. Dilim klasorunu tum dosyalariyla birlikte tutun.", 48, "Dilim"
  WScript.Quit 1
End If
Set environment = shell.Environment("Process")
environment("FILEE_DATA_DIR") = files.BuildPath(folder, "data")
shell.CurrentDirectory = folder
shell.Run Chr(34) & executable & Chr(34), 1, False
