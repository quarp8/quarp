@echo off
rem The console's face: quarp with no arguments boots into the game library
rem (M9 stage 1). Arrows select, Z/Enter plays, X opens the editor stub, Esc quits.
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
pushd "%ROOT%"
"%QUARP%"
popd
if errorlevel 1 pause
