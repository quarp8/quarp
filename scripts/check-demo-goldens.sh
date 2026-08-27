#!/usr/bin/env bash
# The twelve pinned demo hashes of SPEC-SUBSET §10, checked on this machine with the same
# command CI runs. Exists so a change to the console can be shown not to move them before it
# reaches CI — the acceptance ADR-035 was measured against.
set -uo pipefail
cd "$(dirname "$0")/.."
QUARP_DLL="${QUARP_DLL:-src/Quarp.Cli/bin/Release/net10.0/quarp.dll}"
# Как звать .NET — та же развилка, что у check-anchors.sh, и по той же причине: под WSL по
# PATH лежит dotnet.exe, а не dotnet, и прибор, который не запускается, — не прибор.
DOTNET="${DOTNET:-}"
if [ -z "$DOTNET" ]; then
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET=dotnet
  elif command -v dotnet.exe >/dev/null 2>&1; then
    DOTNET=dotnet.exe
  else
    echo "не найден ни dotnet, ни dotnet.exe" >&2
    exit 2
  fi
fi
if [ ! -f "$QUARP_DLL" ]; then
  echo "нет Release-сборки ($QUARP_DLL) — сначала: dotnet build -c Release" >&2
  exit 2
fi
TMP="${TMPDIR:-/tmp}/quarp-demo-goldens.qrpr"
fail=0
while IFS=$'\t' read -r cart input ticks frame audio; do
  case "$cart" in \#*|"") continue ;; esac
  # </dev/null не украшение: dotnet.exe читает stdin, а stdin здесь — сам список картриджей.
  # Без затычки первый же прогон съедал остальные пять строк, цикл заканчивался после одной
  # проверки и прибор рапортовал успех, проверив шестую часть работы. Прибор, который молча
  # проверяет меньше обещанного, опаснее сломанного.
  out="$("$DOTNET" "$QUARP_DLL" replay record "$cart" -o "$TMP" --ticks "$ticks" --input-file "$cart/$input" 2>&1 </dev/null)" || {
    echo "FAIL	$cart/$input	replay record exited non-zero"; fail=1; continue; }
  got_frame="$(printf '%s\n' "$out" | tr -d '\r' | grep -Eo '^[0-9a-f]{16}$' | tail -n 1 || true)"
  got_audio="$(printf '%s\n' "$out" | tr -d '\r' | grep -Eo '^audio [0-9a-f]{16}$' | tail -n 1 | cut -d' ' -f2 || true)"
  if [ "$got_frame" != "$frame" ] || [ "$got_audio" != "$audio" ]; then
    echo "FAIL	$cart/$input	frame ${got_frame:-none} (pinned $frame), audio ${got_audio:-none} (pinned $audio)"
    fail=1
  else
    echo "OK	$cart/$input	$frame	$audio"
  fi
done < <(tr -d '\r' < carts/demo-goldens.tsv)
exit "$fail"
