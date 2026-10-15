@echo off
rem Build AIStation.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No Visual Studio or SDK required. Output: dist\AIStation.exe
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [error] csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)
if not exist dist mkdir dist
set "RES="
for %%f in (ui\*.*) do call set "RES=%%RES%% /resource:ui\%%~nxf,ui/%%~nxf"
if exist ui\icons for %%f in (ui\icons\*.*) do call set "RES=%%RES%% /resource:ui\icons\%%~nxf,ui/icons/%%~nxf"
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 /utf8output ^
  /win32icon:assets\app.ico /out:dist\AIStation.exe ^
  /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.Management.dll ^
  %RES% src\*.cs
if errorlevel 1 (
  echo [error] build failed
  exit /b 1
)
echo Built dist\AIStation.exe
