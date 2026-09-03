@echo off
rem The console itself: quarp with no arguments boots the menu, and from there the library.
rem Since M9 stage 5 this is also the only way into the editors, because the game is a tab:
rem   F1 game   F2 code   F3 sprites   F4 map   F5 sounds   F6 music
rem Esc in a game raises the pause menu over the frame - RESUME, a STEP row that scrubs time
rem with the arrow keys (hold one and it accelerates), and EXIT - plus the tab bar on top.
rem It does not quit any more. Edit on a tab, Ctrl+S, and the cartridge continues on the
rem very same tick with the new code (ADR-042).
rem
rem The other .cmd files here launch one cartridge each straight into play. They pass a path,
rem which is a DIRECT launch: the editors stay out of reach there, so use this file to edit.
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
rem The library scans <cwd>\carts as well as the folder beside the exe, so the working
rem directory is the repository root and not this folder.
pushd "%ROOT%"
"%QUARP%"
popd
if errorlevel 1 pause
