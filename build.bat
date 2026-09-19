@echo off
setlocal
echo Compiling CodexSwitcherTray...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /optimize+ /win32icon:zai.ico /out:CodexSwitcherTray.exe /r:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,Microsoft.CSharp.dll CodexSwitcherTray.cs

if %ERRORLEVEL% equ 0 (
    echo Compilation successful: CodexSwitcherTray.exe
) else (
    echo Compilation failed with error %ERRORLEVEL%
)
pause
