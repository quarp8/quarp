@echo off
rem Shared launcher: %1 = cart folder name under carts\, %2 %3 = extra quarp args.
rem Paths are relative to this file, so the folder works from any checkout location.
setlocal
set "ROOT=%~dp0.."
set "QUARP=%ROOT%\src\Quarp.Cli\bin\Release\net10.0\quarp.exe"
if not exist "%QUARP%" (
  echo First run: building Quarp, this takes a moment...
  pushd "%ROOT%"
  dotnet build -c Release
  popd
)
if not exist "%QUARP%" (
  echo Build failed - see the output above.
  pause
  exit /b 1
)
"%QUARP%" run "%ROOT%\carts\%~1" %2 %3
if errorlevel 1 pause
