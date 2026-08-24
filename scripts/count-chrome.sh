#!/usr/bin/env bash
# Один вопрос: сколько строк стоит хром оболочки (M9-WORKORDER.md, фальсификатор
# "хром полного каркаса с вкладками ≤ ~1500 строк").
#
# ВЕРДИКТ С ЭТОГО ПРИБОРА СНЯТ (2026-08-24, решение владельца, волна разделения модулей).
# Дословно: «Третьи стороны не привлекаем без крайней нужды. Продолжаем делать сами.» Тем
# самым вопрос, на который отвечал вердикт — «не заменить ли наш хром чужим ImGui» — закрыт
# не числом, а решением, и число больше не имеет права его переоткрывать. Скрипт считает и
# печатает, но НЕ выносит приговор и НЕ возвращает 1: exit 0 всегда, exit 2 по-прежнему —
# если маркер метода не найден, потому что молча посчитать неправильно нельзя ни при каком
# решении владельца.
#
# ПОЧЕМУ ФАЙЛ ОСТАВЛЕН, А НЕ УДАЛЁН (карточка предлагала оба пути). Три довода:
#   1. docs/milestones/M9-WORKORDER.md ссылается на `scripts/count-chrome.sh` по пути в
#      четырёх местах (§360, §397, §452, §592). Приказ правит организатор, не бригада;
#      удалить файл значило бы оставить в приказе четыре ссылки в пустоту.
#   2. У факта «сколько строк стоит наш собственный хром» по-прежнему должен быть ОДИН
#      владелец — иначе возвращается дефект 2g/2h, ради которого прибор и писался. Владелец
#      этого факта — этот файл; вопрос, который факт обслуживал, сменился, сам факт нет.
#   3. Число осталось полезным как справка о бюджете сложности СВОЕГО хрома: оно и сегодня
#      2657 при ориентире 1800, и это довод в пользу разделения модулей — то есть ровно
#      в пользу той работы, которую владелец назначил вместо покупки чужого.
#
# ГРАНИЦЫ МОДУЛЕЙ ЭТОТ ПРИБОР НЕ МЕРЯЕТ. Вопрос «не спагетти ли у нас» — у соседа,
# scripts/check-modules.sh: он про направление ссылок между слоями, и вердикт живёт там.
# Сознательно НЕ вызываем один прибор из другого: один прибор — один вопрос — один код
# выхода, иначе падение любого из них становится падением обоих, и никто не разберёт, чьё.
#
# Зачем прибор, а не память: волна 2g отчиталась 1815 строк, волна 2h — 1371, хотя
# кода между ними стало БОЛЬШЕ, а не меньше — метод счёта менялся молча между волнами,
# и теперь никто не может сказать, какое из двух чисел (если хоть одно) было верным.
# Это тот самый дефект, который кит запрещает: у факта «сколько строк стоит хром»
# должен быть один постоянный владелец. Этот скрипт — единственный владелец теперь:
# список файлов (и, где нужно, диапазонов внутри файла) ниже это ЯВНО и есть то
# определение, к которому надо придираться на ревью, а не к памяти дежурного.
#
# ЧТО СЧИТАЕТСЯ ХРОМОМ (решение этой карточки, обоснование):
#   Хром — это виджеты оболочки редактора спрайтов: вкладки, тулбар, статус-бар,
#   палитра-свотчи, скролл-слайдер листа, флаут-меню, подсказки по наведению —
#   и раскладка/роутинг кликов для них (ровно определение из карточки: "виджеты,
#   раскладка, иконки, подсказки и роутинг кликов оболочки"). Это цена СОБСТВЕННЫХ
#   виджетов против готового ImGui.
#
#   НЕ хром (даже если живёт в тех же файлах и рядом по коду) — прямое указание
#   карточки: пиксельный холст, композит слоёв, PNG-энкодер, модель документа
#   и undo. Они кастомны в любом мире (тот же довод, что в самом приказе M9,
#   раздел спайка UI: "доминирующие поверхности редакторов... кастомные в обоих
#   мирах; ImGui экономит только хром"). Сюда же — вся сессия/цикл движка/аудио/
#   библиотека картриджей: это инфраструктура оболочки в целом, а не хром ИМЕННО
#   редактора с вкладками, который меряет фальсификатор.
#
#   Битмап-маски иконок (src/.../EditorIcons.cs, массив _masks) — ДАННЫЕ, не код:
#   это пиксельные картинки 8×8, записанные как байтовые литералы, а не логика
#   виджетов. Считаются и печатаются ОТДЕЛЬНОЙ строкой и в итог не идут — то же
#   решение и та же причина, что не считать PNG-файлы демо-картриджей строками кода.
#
# ГРАНУЛЯРНОСТЬ. Для файлов, которые целиком служат хрому (список FILES_CHROME
# ниже), считается весь файл — как и раньше в спайке (258 строк = атлас + рендерер
# + палитра ЦЕЛИКОМ, потому что тот экран не содержал ничего, кроме хрома).
# Для двух файлов, что смешивают хром с холстом/сессией в одном файле
# (SpriteEditorRenderer.cs рисует и вкладки, и пиксели; QuarpGame.cs — это весь
# игровой цикл, а роутер кнопок редактора — один метод внутри него), применяется
# РАЗБОР ПО МЕТОДАМ: список сигнатур ниже — единственный источник границ, метод
# ищется по точному тексту сигнатуры (не по номеру строки — так правка выше по
# файлу не собьёт счёт), и если сигнатура не находится (метод переименован или
# удалён), прибор ПАДАЕТ с exit 2 вместо того, чтобы молча посчитать неправильно.
# Это и есть защита именно от дефекта 2g/2h: смена метода без обновления счёта
# теперь останавливает прибор, а не проезжает тихо.
#
# ЧЕСТНАЯ НЕТОЧНОСТЬ (см. отчёт, раздел «в чём не уверен»): QuarpGame.UpdateEditor
# (~336 строк) сама смешивает роутинг кликов по кнопкам с логикой рисования
# (жест карандаша, заливки и т.п.) настолько плотно, что метод не делится по
# границам операторов без хирургии, которую карточка запрещает («не переписывай
# сам хром»). Он исключён из хрома ЦЕЛИКОМ — это подтверждённый недосчёт
# (несколько строк реального роутинга кликов в хром не попали), а не выдумка.
#
# ВОЛНА УПРОЩЕНИЯ (2026-08-24, после пробития порога на 2592). Общее двух редакторов
# вынесено в два новых файла, и оба — хром целиком, поэтому оба в списке ниже:
#   EditorChrome.cs         — рама обоих экранов (полоса вкладок, статус-бар и его ряд кнопок,
#                             строка промпта, поля, размер кнопки, хит-тесты кнопок и верб).
#   EditorChromeRenderer.cs — пиксели этой рамы (роли палитры, рамка, полосы, кнопка, текст
#                             статуса, строка промпта, тултип).
# Оба редактора теперь ДЕЛЕГИРУЮТ туда, а не повторяют. Число падает ровно на то, что раньше
# существовало дважды; всё, что просто переехало, продолжает считаться — на то и прибор.
#
# ГРАНИЦА, НА КОТОРОЙ ВИСИТ ВЕРДИКТ: SpriteEditorLayout.cs считается ЦЕЛИКОМ
# (434 строки после волны), хотя часть его методов (TryCanvasPixel, TrySheetCell и подобные)
# это координатная математика холста/листа, а не кнопок. Считаю их хромом
# по букве определения карточки ("роутинг кликов оболочки" — курсор попадает
# в пиксель тоже через клик), но это спорно: более узкое прочтение (только
# кнопки/свотчи/слайдер/флаут/подсказки, без holst-хиттестов) даёт для этого
# файла не 434, а ориентировочно ~330 строк, и тогда общий итог опускается
# примерно на 120 строк. Это разошлось бы с порогом в другую сторону — записано
# в отчёте как «Расхождение», а не решено втихую.
set -u
cd "$(dirname "$0")/.." || exit 2

# Порог перепинен владельцем 2026-08-24 (M9-WORKORDER, «Вердикт по фальсификатору вынесен»):
# было 1500 — спусковой крючок «взять ImGui»; стало 1800 — бюджет сложности СВОЕГО хрома,
# потому что третью сторону решено не брать, пока можно обойтись своим. Пробитие 1800 означает
# волну упрощения нашего кода, а не смену библиотеки. Перепин — только так же: строкой здесь
# и абзацем в приказе, что за семантика изменилась.
THRESHOLD=1800
SHELL_DIR="src/Quarp.Shell.Desktop"

# --- файлы, которые целиком — хром (виджеты/раскладка/иконки/подсказки/скролл) ---
FILES_CHROME_WHOLE=(
  "$SHELL_DIR/EditorChrome.cs"         # ОБЩЕЕ: рама обоих редакторов (волна упрощения)
  "$SHELL_DIR/EditorChromeRenderer.cs" # ОБЩЕЕ: пиксели этой рамы (волна упрощения)
  "$SHELL_DIR/EditorIconAtlas.cs"   # иконки: атлас, тот же приём, что PixelFontAtlas
  "$SHELL_DIR/IconHoverTracker.cs"  # подсказки по наведению (3-секундный контракт)
  "$SHELL_DIR/ToolbarFlyout.cs"     # флаут группового слота тулбара
  "$SHELL_DIR/SheetScroll.cs"       # владелец страничной раскладки листа + слайдер
  "$SHELL_DIR/SpriteEditorLayout.cs" # раскладка всего экрана редактора (см. оговорку выше)
)

# --- EditorIcons.cs: код хром, но внутри лежит блок битовых масок — данные ---
ICONS_FILE="$SHELL_DIR/EditorIcons.cs"
ICONS_MASK_START_SIG='private static readonly byte[][] _masks'
ICONS_MASK_END_SIG='public static int IconCount => _masks.Length;'

# --- SpriteEditorRenderer.cs: один файл, метод — либо хром, либо холст/инфра ---
# chrome  = рисует виджет (кнопка/вкладка/статус-бар/свотч/слайдер/флаут/подсказка/рамка)
# content = рисует пиксельный контент (холст, лист, выделение, штамп) — НЕ хром
# infra   = конструктор/Dispose/диспетчер Draw/аплоуд текстуры — не хром и не контент,
#           отдельная строка учёта, чтобы сумма по файлу сходилась без остатка
RENDERER_FILE="$SHELL_DIR/SpriteEditorRenderer.cs"
RENDERER_METHODS=(
  "public SpriteEditorRenderer(GraphicsDevice device)|infra"
  "public void Draw(|infra"
  "public void Dispose()|infra"
  "private static string SheetCoordinates(SpriteEditorSession editor)|chrome"
  "private static string? StandingNotice(SpriteEditorSession editor) =>|chrome"
  "private void UploadSheetIfChanged(SpriteEditorSession editor)|content"
  "private void DrawButtons(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, HoverTarget? hover)|chrome"
  "private static int CurrentVariant(SpriteEditorSession editor, EditorButton slot) => slot switch|chrome"
  "private void DrawGroupMarker(SpriteBatch batch, in SpriteEditorLayout layout, Rectangle slot, Color color)|chrome"
  "private void DrawFlyout(|chrome"
  "private void DrawCanvas(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, double timeSeconds)|content"
  "private static Rectangle PixelRect(in SpriteEditorLayout layout, int x, int y) =>|content"
  "private void DrawSelection(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, double timeSeconds)|content"
  "private void DrawStampGhost(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)|content"
  "private void DrawSwatches(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor)|chrome"
  "private void DrawSheet(SpriteBatch batch, in SpriteEditorLayout layout, SpriteEditorSession editor, int scroll)|content"
  "private void DrawSlider(SpriteBatch batch, in SpriteEditorLayout layout, SheetScroll scroll, HoverTarget? hover)|chrome"
  "private void DrawTooltip(|chrome"
)

# --- QuarpGame.cs: весь файл — игровой цикл оболочки (не предмет этого фальсификатора),
# кроме одного метода — диспетчера кликов по кнопкам редактора, это и есть
# "роутинг кликов оболочки" из определения карточки.
GAME_FILE="$SHELL_DIR/QuarpGame.cs"
# Дополнено после аудита сессии 2026-08-24: прибор ловил переименование маркера, но не
# ДОБАВЛЕНИЕ нового хрома в этот файл — волна 2k положила сюда ScrollSheetTo (слайдер листа),
# и метрика его не увидела. Правило теперь явное: любой новый метод QuarpGame, который
# двигает виджет или маршрутизирует клик, добавляется сюда строкой, иначе число врёт.
# ВНИМАНИЕ: список обязан идти в ПОРЯДКЕ ФАЙЛА — счёт ведётся от сигнатуры до следующей,
# поэтому пропущенный метод молча приписывается предыдущему. Аудит 2026-08-24 поймал ровно это:
# ScrollSheetTo (слайдер листа, чистый хром) не был в списке и его строки уходили в счёт «не хром».
GAME_METHODS=(
  "private void ScrollSheetTo(in SpriteEditorLayout layout, int column)|chrome"
  "private static void BeginCanvasGesture(SpriteEditorSession editor, int localX, int localY)|content"
  "private static void EndCanvasGesture(SpriteEditorSession editor)|content"
  "private static void RefreshGestures(SpriteEditorSession editor, in ShellCommands commands)|content"
  "private bool HandleEditorButton(SpriteEditorSession editor, EditorButton button)|chrome"
  "private bool HandleMapButton(MapEditorSession map, EditorButton button)|chrome"
  "protected override void Draw(GameTime gameTime)|infra"
)

# --- Редактор карт (M9 этап 3, волна 3b). Раскладка и вид — хром целиком, как SheetScroll;
# рендерер разбирается по методам, как рендерер спрайтов: полотно карты и полоса листа —
# контент (они кастомны в любом мире), кнопки/пикер/миникарта/статус — хром.
MAPUI_FILES_WHOLE=(
  "$SHELL_DIR/MapEditorLayout.cs"
  "$SHELL_DIR/MapEditorView.cs"
)
MAP_RENDERER_FILE="$SHELL_DIR/MapEditorRenderer.cs"
MAP_RENDERER_METHODS=(
  "public MapEditorRenderer(GraphicsDevice device)|infra"
  "public void Draw(|infra"
  "public void Dispose()|infra"
  "private static string MapCoordinates(MapEditorView view) =>|chrome"
  "private static string? StandingNotice(MapEditorSession map) =>|chrome"
  "private void UploadSheetIfChanged(SpriteEditorSession sheet)|content"
  "private void UploadMinimapIfChanged(MapEditorSession map)|content"
  "private void DrawCanvas(SpriteBatch batch, in MapEditorLayout layout, MapEditorSession map, MapEditorView view)|content"
  "private void DrawButtons(SpriteBatch batch, in MapEditorLayout layout, MapEditorSession map, HoverTarget? hover)|chrome"
  "private void DrawPicker(SpriteBatch batch, in MapEditorLayout layout, MapEditorSession map)|content"
  "private void DrawMinimap(SpriteBatch batch, in MapEditorLayout layout, MapEditorView view)|chrome"
  "private void DrawTooltip(|chrome"
)

fail_env=0
total_chrome=0
declare -a report_rows=()

check_file() {
  [ -f "$1" ] || { echo "нет файла: $1 — дерево не то, что ожидал прибор" >&2; fail_env=1; }
}

# Возвращает номер строки первого вхождения точного текста (fixed string) в файле,
# или пусто, если не найдено.
line_of() {
  local file="$1" sig="$2"
  grep -n -F -- "$sig" "$file" | head -1 | cut -d: -f1
}

# Считает строки метода/файла по разбору на именованные диапазоны сигнатур.
# ПОВЕДЕНИЕ ПРИ ПРОМАХЕ: сигнатура не найдена -> exit 2 (метод переименован/удалён,
# счёт нечестен без обновления списка выше) — это и есть защита от дефекта 2g/2h.
split_by_methods() {
  local file="$1"; shift
  local -a specs=("$@")
  local total; total=$(wc -l < "$file")
  local n=${#specs[@]}
  local -a lines=()
  local i sig
  for ((i = 0; i < n; i++)); do
    sig="${specs[$i]%%|*}"
    local ln; ln=$(line_of "$file" "$sig")
    if [ -z "$ln" ]; then
      echo "МАРКЕР НЕ НАЙДЕН в $file: '$sig'" >&2
      echo "метод переименован/удалён — обнови scripts/count-chrome.sh, счёт остановлен" >&2
      exit 2
    fi
    lines+=("$ln")
  done
  local sum_chrome=0 sum_content=0 sum_infra=0
  # зона 0: всё до первой сигнатуры (using/namespace/поля) — не хром, не контент
  local zero_end=$(( lines[0] - 1 ))
  sum_infra=$(( sum_infra + zero_end ))
  for ((i = 0; i < n; i++)); do
    local cat="${specs[$i]##*|}"
    local start=${lines[$i]}
    local end
    if [ $((i + 1)) -lt "$n" ]; then
      end=$(( lines[$((i + 1))] - 1 ))
    else
      end=$total
    fi
    local cnt=$(( end - start + 1 ))
    case "$cat" in
      chrome)  sum_chrome=$((sum_chrome + cnt)) ;;
      content) sum_content=$((sum_content + cnt)) ;;
      infra)   sum_infra=$((sum_infra + cnt)) ;;
    esac
  done
  printf '%s\t%d\t%d\t%d\t%d\n' "$file" "$total" "$sum_chrome" "$sum_content" "$sum_infra"
}

echo "=== хром: целые файлы (виджеты/раскладка/иконки/подсказки/скролл) ==="
printf '%-55s %8s\n' "файл" "строк"
for f in "${FILES_CHROME_WHOLE[@]}"; do
  check_file "$f"
  [ "$fail_env" -eq 1 ] && continue
  n=$(wc -l < "$f")
  printf '%-55s %8d\n' "$f" "$n"
  total_chrome=$((total_chrome + n))
done

echo
echo "=== EditorIcons.cs: код (хром) отдельно от битмап-масок (данные) ==="
check_file "$ICONS_FILE"
if [ "$fail_env" -eq 0 ]; then
  icons_total=$(wc -l < "$ICONS_FILE")
  mstart=$(line_of "$ICONS_FILE" "$ICONS_MASK_START_SIG")
  mendmarker=$(line_of "$ICONS_FILE" "$ICONS_MASK_END_SIG")
  if [ -z "$mstart" ] || [ -z "$mendmarker" ]; then
    echo "МАРКЕР МАСОК НЕ НАЙДЕН в $ICONS_FILE — обнови scripts/count-chrome.sh" >&2
    exit 2
  fi
  mend=$(( mendmarker - 1 ))
  mask_lines=$(( mend - mstart + 1 ))
  icons_code=$(( icons_total - mask_lines ))
  printf '%-55s %8d  (код хрома)\n' "$ICONS_FILE" "$icons_code"
  printf '%-55s %8d  (данные — маски иконок, в итог НЕ входят)\n' "$ICONS_FILE (_masks)" "$mask_lines"
  total_chrome=$((total_chrome + icons_code))
fi

echo
echo "=== SpriteEditorRenderer.cs: разбор по методам (chrome / content / infra) ==="
check_file "$RENDERER_FILE"
if [ "$fail_env" -eq 0 ]; then
  row=$(split_by_methods "$RENDERER_FILE" "${RENDERER_METHODS[@]}") || exit 2
  IFS=$'\t' read -r rf rtotal rchrome rcontent rinfra <<< "$row"
  printf '%-55s всего %4d = хром %4d + холст/лист %4d + инфра %4d\n' \
    "$rf" "$rtotal" "$rchrome" "$rcontent" "$rinfra"
  total_chrome=$((total_chrome + rchrome))
fi

echo
echo "=== Редактор карт (волна 3b): раскладка и вид целиком, рендерер по методам ==="
for f in "${MAPUI_FILES_WHOLE[@]}"; do
  check_file "$f"
  [ "$fail_env" -eq 1 ] && continue
  n=$(wc -l < "$f")
  printf '%-55s %8d\n' "$f" "$n"
  total_chrome=$((total_chrome + n))
done
check_file "$MAP_RENDERER_FILE"
if [ "$fail_env" -eq 0 ]; then
  row=$(split_by_methods "$MAP_RENDERER_FILE" "${MAP_RENDERER_METHODS[@]}") || exit 2
  IFS=$'\t' read -r mf mtotal mchrome mcontent minfra <<< "$row"
  printf '%-55s всего %4d = хром %4d + полотно/лист %4d + инфра %4d\n' \
    "$mf" "$mtotal" "$mchrome" "$mcontent" "$minfra"
  total_chrome=$((total_chrome + mchrome))
fi

echo
echo "=== QuarpGame.cs: разбор по методам (только роутер кнопок — хром) ==="
check_file "$GAME_FILE"
if [ "$fail_env" -eq 0 ]; then
  row=$(split_by_methods "$GAME_FILE" "${GAME_METHODS[@]}") || exit 2
  IFS=$'\t' read -r gf gtotal gchrome gcontent ginfra <<< "$row"
  printf '%-55s всего %4d = хром %4d (остальное — цикл движка/сессии/аудио, не предмет фальсификатора)\n' \
    "$gf" "$gtotal" "$gchrome"
  total_chrome=$((total_chrome + gchrome))
fi

if [ "$fail_env" -ne 0 ]; then
  echo "ОШИБКА ОКРУЖЕНИЯ: не все файлы хрома найдены — дерево не соответствует ожиданию скрипта." >&2
  exit 2
fi

echo
echo "=== ИТОГ (справочно — вердикт с этого прибора снят, см. шапку) ==="
printf 'Цена собственного интерфейса: %d строк хрома. Ориентир бюджета: ~%d строк.\n' \
  "$total_chrome" "$THRESHOLD"
if [ "$total_chrome" -gt "$THRESHOLD" ]; then
  printf 'Сверх ориентира на %d строк — довод в пользу разделения модулей, НЕ в пользу\n' \
    "$((total_chrome - THRESHOLD))"
  echo "чужой библиотеки: третьи стороны не привлекаем (решение владельца 2026-08-24)."
else
  printf 'В пределах ориентира (%d <= %d).\n' "$total_chrome" "$THRESHOLD"
fi
echo "Границы модулей меряет scripts/check-modules.sh — вердикт живёт там."
exit 0
