@echo off
rem SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
rem Builds SCHaulingManager.exe with the C# compiler that is already part of Windows (.NET Framework 4.x).
rem Nothing is downloaded or installed.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.x.
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:src\app.ico /resource:src\ships.json,ships.json /resource:src\places.json,places.json /out:SCHaulingManager.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
)
echo.
echo Built SCHaulingManager.exe
pause
