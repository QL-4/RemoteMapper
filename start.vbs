' RemoteMic background launcher — starts RemoteMic.exe with no visible window.
' Double-click this to run RemoteMic in the background. Logs go to RemoteMic.log.
' Stop with stop.bat (or taskkill /F /IM RemoteMic.exe).

Set fso = CreateObject("Scripting.FileSystemObject")
Set sh  = CreateObject("WScript.Shell")
here = fso.GetParentFolderName(WScript.ScriptFullName)

If Not fso.FileExists(here & "\RemoteMic.exe") Then
    MsgBox "RemoteMic.exe not found next to this script:" & vbCrLf & here, vbCritical, "RemoteMic"
    WScript.Quit
End If

' Already running? stop it first so this launch always becomes the live instance.
Set wmi = GetObject("winmgmts:\\.\root\cimv2")
Set procs = wmi.ExecQuery("SELECT * FROM Win32_Process WHERE Name='RemoteMic.exe'")
For Each p In procs
    On Error Resume Next
    p.Terminate
    On Error GoTo 0
Next
WScript.Sleep 1500

' Launch hidden (0 = hidden window, False = don't wait).
sh.CurrentDirectory = here
sh.Run "RemoteMic.exe", 0, False