#!/usr/bin/env bash
# Один вопрос: не сдвинулись ли якоря детерминизма (docs/PLAYBOOK.md §4).
#
# Зачем прибор, а не память: в M4 организатор сверял восемь величин перед каждым
# коммитом руками — по одной команде на величину. Прибор отвечает одной командой
# и одним кодом выхода, и его же гоняет приёмщик после каждого негативного контроля.
#
# Значения ниже — те же, что в PLAYBOOK §4 и в тестах. Перепиновка якоря — только
# осознанно: правишь здесь — объясни в сообщении коммита, какая семантика изменилась,
# было A, стало B, почему. Молчаливый перепин — худшее, что может случиться с проектом
# про детерминизм.
#
# Девятый якорь — «150 контрольных точек эталона, все различны» — проверяет CI;
# здесь его нет сознательно: локальный прогон отвечает за хэши, не за раскладку точек.
set -u
cd "$(dirname "$0")/.." || exit 2

DLL="src/Quarp.Cli/bin/Release/net10.0/quarp.dll"
if [ ! -f "$DLL" ]; then
  echo "нет Release-сборки ($DLL) — сначала: dotnet build -c Release" >&2
  exit 2
fi

q() { dotnet "$DLL" "$@"; }

fail=0
check() { # имя ожидание факт
  if [ "$2" = "$3" ]; then
    printf 'OK\t%s\t%s\n' "$1" "$3"
  else
    printf 'СДВИГ\t%s\tожидалось %s, стало %s\n' "$1" "$2" "$3"
    fail=1
  fi
}

sim_out="$(q sim carts/snake --ticks 600)" || exit 2
check "sim-600, кадр" "37c481f3e17fab02" "$(printf '%s\n' "$sim_out" | tail -1)"
check "sim-600, звук" "f373b5bfd09755b9" "$(printf '%s\n' "$sim_out" | awk '/^audio/{print $2}' | tail -1)"

rep_out="$(q replay play carts/snake/replays/golden.qrpr)" || exit 2
check "реплей, кадр" "24a6eb974ff922e4" "$(printf '%s\n' "$rep_out" | tail -1)"
check "реплей, звук" "f93bf5cc36b83cba" "$(printf '%s\n' "$rep_out" | awk '/^audio/{print $2}' | tail -1)"

check "тишина, 0 тиков"    "cbf29ce484222325" "$(q audio silence --ticks 0    | awk '{print $2}')"
check "тишина, 600 тиков"  "54738d7161a01b25" "$(q audio silence --ticks 600  | awk '{print $2}')"
check "тишина, 3000 тиков" "220acbc2c817fb25" "$(q audio silence --ticks 3000 | awk '{print $2}')"

check "sha256 golden.qrpr" \
  "8d6842b337cf3fd8c99b4b0a3c3d9e1a4643c99fba63965a8f6471e49ba9712c" \
  "$(sha256sum carts/snake/replays/golden.qrpr | awk '{print $1}')"

if [ "$fail" -ne 0 ]; then
  echo
  echo "ЯКОРЯ СДВИНУЛИСЬ. Стоп и доклад (PLAYBOOK §4): не перепиновывать, разбираться."
fi
exit "$fail"
