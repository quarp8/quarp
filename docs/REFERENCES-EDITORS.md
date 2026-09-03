<!-- Собрано разведкой 2026-08-24 по открытым исходникам TIC-80 и LIKO-12 и по мануалу
PICO-8. Это СПРАВОЧНИК, а не приказ: он говорит, как сделано у соседей, и служит одним
владельцем факта «как это принято» для всех волн паритета редакторов. Расхождение нашего
решения с референсом допустимо — но тогда причина пишется в приказе вехи, а не остаётся
в голове дежурного. Каждое утверждение про TIC-80 и LIKO-12 подкреплено файлом и функцией;
про PICO-8 — цитатой из мануала. -->

Собрал исходники TIC‑80 (`src/studio/editors/*.c`, `src/core/draw.c`, `src/api/luaapi.c`, `src/tilesheet.h`), LIKO‑12 (`src/OS/DiskOS/Editors/*.lua`, `src/OS/DiskOS/Libraries/map.lua`, `src/Peripherals/GPU/modules/palette.lua`) и полный текст мануала PICO‑8. Ниже — справочник.

---

# СПРАВОЧНИК: встроенные редакторы TIC‑80 / LIKO‑12 / PICO‑8

## 0. Как читать и что важно знать заранее

| | TIC‑80 | LIKO‑12 | PICO‑8 |
|---|---|---|---|
| Экран | 240×136 | 192×128 | 128×128 |
| Палитра | 16 цв. (настраиваемая), 2 vbank'а | 16 цв. (настраиваемая) | 16 цв. (фикс. + скрытая 2-я половина) |
| Спрайты | 128×128 px = 256 тайлов × **2 банка** (TILES/SPRITES) = 512; `TIC_SPRITES` = 512 | 24×16 = **384** спрайта, id **с 1**; 4 «банка» по 24×4 = 96 | 256 (0..255), 4 страницы по 64 |
| Флаги | 512 × 8 бит (`TIC_FLAGS`) | 384 × 8 бит | 256 × 8 бит |
| Карта | 240×136 тайлов (`TIC_MAP_WIDTH/HEIGHT`) | 144×128 тайлов | 128×32 (или 128×64 c shared) |
| SFX | 64 сэмпла × 30 тиков (`SFX_COUNT`, `SFX_TICKS`) | 64 слота × 32 ноты | 64 × 32 ноты |
| Музыка | 8 треков × 16 фреймов × 60 паттернов × 64 строки × 4 канала | **нет редактора** (заглушка) | 64 паттерна × 4 канала |
| Файлы | `src/studio/editors/{sprite,map,code,sfx,music}.c` | `src/OS/DiskOS/Editors/{sprite,tile,code,sfx,soon}.lua` | закрыт |

**LIKO‑12: музыкального редактора нет.** В `src/OS/DiskOS/Editors/init.lua` (`edit:initialize`) слот `music` грузит `soon.lua`:
```lua
local editors = {"soon","sfx","tile","sprite","code","soon"}
```
`soon.lua` рисует «WORK IN PROGRESS...».

---

## 1. Глобальная оболочка (общая для всех редакторов)

### TIC‑80 — `src/studio/studio.c`

Верхняя панель (`drawToolbar`): 5 иконок‑вкладок (CODE/SPRITE/MAP/SFX/MUSIC), иконка банка (только PRO), «extrabar» с 5 кнопками (`drawExtrabar`), и текстовое поле — **это и есть строка статуса**:

```c
if(strlen(studio->tooltip.text))
    tic_api_print(tic, studio->tooltip.text, TextOffset, 1, tic_color_dark_grey, ...);
else
    tic_api_print(tic, Names[mode], TextOffset, 1, tic_color_grey, ...);
```
То есть: по умолчанию имя редактора, при наведении на любой контрол — его подсказка (`showTooltip` / `SHOW_TOOLTIP`).

| Кнопка extrabar | Событие | Подсказка |
|---|---|---|
| cut | `TIC_TOOLBAR_CUT` | `CUT [ctrl+x]` |
| copy | `TIC_TOOLBAR_COPY` | `COPY [ctrl+c]` |
| paste | `TIC_TOOLBAR_PASTE` | `PASTE [ctrl+v]` |
| undo | `TIC_TOOLBAR_UNDO` | `UNDO [ctrl+z]` |
| redo | `TIC_TOOLBAR_REDO` | `REDO [ctrl+y]` |

Глобальные горячие клавиши (`processShortcuts`):

| Клавиша | Действие |
|---|---|
| `F1..F5` | CODE / SPRITE / MAP / SFX / MUSIC |
| `Alt+1..5` | то же (кроме AZERTY-раскладки) |
| `Alt+\`` | консоль |
| `Ctrl+PageUp/PageDown` | предыдущий/следующий редактор (`changeStudioMode`) |
| `Ctrl+R`, `Ctrl+Enter` | `runGame` |
| `Ctrl+S` | `saveProject` |
| `Ctrl+Q` | выход |
| `Ctrl+0..7` | переключение банка (только PRO) |
| `Esc` | консоль ↔ редактор |
| `F6` CRT-шейдер, `F8` скриншот, `F9` видео, `F11` фуллскрин | |

Системный буфер обмена (`studio.c`):
```c
void toClipboard(const void* data, s32 size, bool flip)  // tic_tool_buf2str -> hex-строка -> tic_sys_clipboard_set
bool fromClipboard(void* data, s32 size, bool flip, bool remove_white_spaces, bool sameSize)
```
**Всё межредакторное копирование идёт через системный буфер в виде hex‑текста.** `sameSize=true` в спрайтах/SFX → вставится только блок ровно той же длины.

### LIKO‑12 — `src/OS/DiskOS/Editors/init.lua` (`edit:loop`)

| Клавиша | Действие |
|---|---|
| `Esc` | выход в терминал |
| `Alt+Left` / `Alt+Right` | следующий / предыдущий редактор (циклично) |
| `Ctrl+S` | `term.execute("save")`, сообщение «Saved successfully» |
| `Ctrl+L` | `term.execute("load")`, «Reloaded successfully» |
| `Ctrl+R` | `term.ecommand("run")` и выход из редактора |
| ЛКМ по иконкам справа вверху | выбор редактора (`modeGrid`) |

Верхняя полоса — логотип + 5 иконок редакторов; нижняя полоса — «flavor»-цвет (у кода в неё пишется статус). Единой строки статуса на все редакторы нет.

### PICO‑8 — мануал

> «Press ESC to toggle between console and editor.
> Click editing mode tabs at top right to switch or press ALT+LEFT/RIGHT.»

> «CTRL-R | Reload / Run / Restart cartridge»
> «CTRL-S | Quick-Save working cartridge»

**Примечание про `Esc` (M9, этап 5, ADR-042, 2026-09-02).** У всех трёх референсов
`Esc` — это переключатель: PICO-8 «toggle between console and editor», TIC-80 «консоль
↔ редактор», LIKO-12 «выход в терминал». У нас `Esc` **в игре** поднимает меню паузы
поверх кадра — три строки: `RESUME`, перемотчик `STEP < тик >` (стрелки, удержание
разгоняется) и `EXIT`; строки «шаг назад/вперёд» и «перемотка на 60 тиков» были
в первой редакции этапа 5 и удалены самим этапом 5а, клавиши `,` `.` `BKSP` `Home`
остались. Переключение между игрой и пятью редакторами живёт отдельно, на вкладках `F1`–`F6`
и `Alt`+стрелках — том же самом наборе клавиш, которым референсы (строка 16 таблицы
в §8 ниже) уже переключают редакторы между собой. Это сознательное расхождение,
а не недоделка: `Esc` у соседей — единственная дверь между «играть» и «редактировать»,
и она ничего не говорит про состояние игры в момент нажатия. У нас с этого этапа
пауза, перемотка и «продолжить с того же тика» — это половина смысла всей консоли
(ADR-006, ADR-007, ADR-042), и она нужна из **игры**, а не только из «между игрой
и редактором»; отдавать `Esc` переключению вкладок означало бы либо не заводить меню
паузы вовсе, либо вешать его на другую клавишу, которую игрок не станет искать по
привычке из PICO-8. Переключение вкладок эту привычку не теряет — оно просто переехало
на клавиши, которые референсы и так используют для того же самого действия.

---

## 2. Редактор СПРАЙТОВ

### 2.1 TIC‑80 — `src/studio/editors/sprite.c` + `sprite.h`

Геометрия (`enum` в начале `sprite.c`):
```c
#define CANVAS_SIZE (64)
#define PALETTE_ROWS 2
#define BRUSH_SIZES 4
CanvasX = 24, CanvasY = 20, CanvasW = 64, CanvasH = 64,
PaletteX = 24, PaletteY = 112,
SheetX = TIC80_WIDTH - TIC_SPRITESHEET_SIZE - 1, SheetY = ToolbarH  // 111, 7; 128×128
```

**Инструменты** (`drawTools`, режимы `sprite->mode`):

| Иконка | Режим | Подсказка | Что делает |
|---|---|---|---|
| bigpen | `SPRITE_DRAW_MODE` | `BRUSH [1]` | Карандаш; ЛКМ = `color`, ПКМ = `color2`; интерполяция линией между кадрами (`paintLine`), кисть `brushSize`×`brushSize` пикселей (`paintPoint`) |
| bigpicker | `SPRITE_PICK_MODE` | `COLOR PICKER [2]` | ЛКМ берёт цвет в `color`, ПКМ — в `color2` |
| bigselect | `SPRITE_SELECT_MODE` | `SELECT [3]` | Прямоугольное выделение; при отпускании `copySelection()` вырезает область (залив её `color2`) в «плавающий» слой |
| bigfill | `SPRITE_FILL_MODE` | `FILL [4]` | ЛКМ/ПКМ — заливка (`floodFill`); **`Ctrl` + клик — «replace color»** по всему спрайту (`replaceColor`) |

**Кнопки трансформаций** (`drawSpriteTools`) — работают либо над всем спрайтом, либо над выделением:

| Подсказка | `SpriteToolsFunc` (нет выделения) | `CanvasToolsFunc` (есть выделение) |
|---|---|---|
| `FLIP HORZ [5]` | `flipSpriteHorz` | `flipCanvasHorz` |
| `FLIP VERT [6]` | `flipSpriteVert` | `flipCanvasVert` |
| `ROTATE [7]` | `rotateSprite` | `rotateCanvas` |
| `ERASE [8]` | `deleteSprite` | `deleteCanvas` |

Прочие контролы:

| Контрол | Функция | Подсказка |
|---|---|---|
| Ползунок кисти (слева от канвы, 4 позиции) | `drawBrushSlider` | `BRUSH SIZE` |
| «Зум канвы» в тулбаре, 4 позиции | `drawSpriteToolbar` → `updateSpriteSize` | `CANVAS ZOOM` |
| Вкладки банков (слева от листа) | `drawBankTabs` | `TILES [tab]` / `SPRITES [tab]` |
| Кнопки страниц (в тулбаре, если `pages>1`) | `drawSpriteToolbar` | `PAGE %i` |
| Кнопка «advanced» (слева вверху) | `drawAdvancedButton` | `ADVANCED MODE` |
| Переключатель BPP (только advanced) | `drawBitMode` | `4/2/1 BITS PER PIXEL` |
| `bank0`/`bank1` палитры (advanced) | `drawPaletteVBank1` | `VBANK0 PALETTE` / `VBANK1 PALETTE` |
| Кнопка RGB (advanced) | `drawPaletteVBank1` | `EDIT PALETTE` |
| RGB-слайдеры + copy/paste палитры | `drawRGBSliders`, `drawRGBTools` | `COPY PALETTE`, `PASTE PALETTE` |
| Стрелки сдвига (при выделении) | `drawMoveButtons` | — |

**Выбор спрайта / лента / страницы / банки** (`drawSheetVBank1`, `selectSprite`, `updateIndex`):
- Лист 128×128 px = 16×16 клеток, справа вверху, показывается **целиком** (`drawSheet` рисует `tic_api_spr(tic, 0, ..., 16, 16, NULL, 0, ...)` — начиная со спрайта 0, с его реальной картинкой).
- Клик/протяжка ЛКМ по листу — выбор; выделение всегда квадрат `sprite->size` (8/16/32/64 px = 1×1, 2×2, 4×4, 8×8 тайлов), центрируется под курсором: `offset = (size - 8)/2`.
- Размер меняется: колесом мыши (`tick`: `scrolly>0 → size<<=1`, вниз → `>>=1`), либо «CANVAS ZOOM».
- Страницы: `blit.pages = 4 / bpp` (`tic_blit_update_bpp` в `src/tilesheet.h`) → 4bpp = 1 стр., 2bpp = 2 стр., 1bpp = 4 стр. Переключение `Ctrl+Left/Right` (`leftViewport`/`rightViewport`) или кликом по цифре в тулбаре. Наличие соседних страниц показано штрихами по бокам рамки листа.
- Банки: 2 (`TIC_SPRITE_BANKS`) — TILES и SPRITES, клавиша `Tab` (`switchBanks`), обе с анимацией прокрутки.
- Индекс спрайта печатается над канвой; **клик по нему переключает dec/hex**:
```c
sprintf(buf, sprite->hexindex ? "0x%02X" : "#%i", index);
...
if(checkMouseClick(..., tic_mouse_left)) sprite->hexindex = !sprite->hexindex;
```

**Флаги** (`drawFlags`, только при `sprite->advanced && is4bpp`):
- 8 флагов, квадратики 5×5, цвет флага `i+2`, справа подпись `0..7`.
- Подсказка: `set flag [%i]`.
- Клик переключает флаг **сразу у всех спрайтов текущего выделения** (`getSpriteIndexes` возвращает все индексы блока `size/8 × size/8`).
- Индикация: `or` (хотя бы у одного) — точка в центре; `and` (у всех) — заполненный квадрат + белый пиксель.
- Под ними — hex‑поле на 2 цифры; клик входит в режим правки, `Left/Right` — выбор ниббла, ввод hex‑символа записывает значение всем спрайтам выделения.

**Клавиатура** (`processKeyboard`):

| Клавиша | Действие |
|---|---|
| `1/2/3/4` | инструмент |
| `5/6/7/8` | flip‑h / flip‑v / rotate / erase |
| `-` / `=` | размер кисти −/+ (`updateBrushSize`, циклично 1..4) |
| `[` / `]` | предыдущий/следующий цвет (`updateColorIndex`, по модулю `1<<bpp`) |
| стрелки | перемещение по листу (`upSprite`…`rightSprite`); при активном выделении — сдвиг содержимого (`upCanvas`…) |
| `Delete` | очистить спрайт / выделение |
| `Tab` | переключить банк |
| `Ctrl+Left/Right` | страница |
| `Ctrl+Tab` | цикл BPP 4→2→1→4 |
| `Ctrl+Z` / `Ctrl+Y` | undo / redo |
| `Ctrl+X/C/V` | буфер (через `getClipboardEvent`) |
| В режиме правки палитры | стрелки + hex‑цифры правят RGB |
| `Alt` зажат | обработка клавиш полностью пропускается (`if(tic_api_key(tic, tic_key_alt)) return;`) |

**Мышь:**

| Действие | Эффект |
|---|---|
| ЛКМ по канве | рисовать `color` / выделять / заливать / пипетка |
| ПКМ по канве | рисовать `color2` / пипетка в `color2` / заливать `color2` |
| **СКМ по канве** | пипетка в `color` (`drawCanvasVBank1`: `checkMouseDown(..., tic_mouse_middle)`) |
| Колесо | размер спрайта (зум) |
| ЛКМ/ПКМ по палитре | выбрать `color` / `color2` |
| Протяжка по листу | выбор спрайта |
| Панорамирования нет | канва фиксированного размера |

**Undo/redo:** `history_create(src, TIC_SPRITES * sizeof(tic_tile))` — история на **весь лист спрайтов**. `history_add` вызывается:
- в `processDrawCanvasMouse` — **каждый кадр**, пока зажата кнопка (но `history_add` возвращает `false`, если байты не изменились → `if (memcmp(history->state, history->data, history->size) == 0) return false;`);
- в `pasteSelection`, `processFillCanvasMouse`, `copyFromClipboard`, во всех трансформациях.

**⇒ гранулярность шага = один кадр рисования, а не мазок.** Реализация — XOR‑дифф всего региона с обрезкой нулей слева/справа (`trim_left`/`trim_right`), двусвязный список (`src/ext/history.c`).

**Копирование/вставка** (`copyToClipboard`/`copyFromClipboard`):
```c
static inline s32 getClipboardSpritesSize(Sprite* sprite) { return sprite->size*sprite->size*TIC_PALETTE_BPP/BITS_IN_BYTE; }
static inline s32 getClipboardFlagsSize(Sprite* sprite)   { return is4bpp(sprite) ? sprite->size*sprite->size/(8*8) : 0; }
```
**Вместе с пикселями в буфер кладутся ФЛАГИ всех скопированных спрайтов** и восстанавливаются при вставке. Формат — hex‑строка в системном буфере. `Ctrl+X` = copy + `deleteSprite`.

### 2.2 LIKO‑12 — `src/OS/DiskOS/Editors/sprite.lua`

**Инструменты** (`tools` + `toolshold = {true,true,false,false,false}`):

| # | Название | Тип | Действие |
|---|---|---|---|
| 1 | Pencil | режим | рисует квадратом `sizes[size]` = **{1,2,3,5}** px; ЛКМ → `colsL`, ПКМ/Shift → `colsR` |
| 2 | Fill (Bucket) | режим | `ImageUtils.queuedFill` в пределах текущего (зумленного) спрайта |
| 3 | Clone (Copy) | кнопка | `se:copy()` |
| 4 | Stamp (Paste) | кнопка | `se:paste()` |
| 5 | Delete (Erase) | кнопка | залить нулём всю область, сообщение `DELETED SPRITE <id>` |

**Трансформации** (`transformations`, 5 кнопок):

| # | Действие | Клавиша |
|---|---|---|
| 1 | Rotate right | `r` |
| 2 | Rotate left | `shift-r` |
| 3 | Flip horizontal | `f` |
| 4 | Flip vertical | `shift-f` |
| 5 | Flip H+V | `i` |

**Ползунки:** Zoom (3 позиции → `zscale = 2^(zoom-1)` = 1×1, 2×2, 4×4 спрайта) и Size (4 позиции — размер кисти).

**Выбор спрайта:** лента внизу экрана шириной во весь экран, высотой `bankH = 4` строки по 24 клетки = **96 спрайтов в банке**, всего 4 банка. Кнопки банков — 4 иконки над лентой (`sprsbanksgrid`), плюс клавиши `1/2/3/4`. Рядом — поле ID (с ведущими нулями) и превью 8×8 в натуральную величину (`revdraw`).

**Флаги** (`redrawFLAG`, `flagsgrid`): **8 кружков** над лентой, шаг 7 px, системные спрайты 125 (выкл) / 126..133 (вкл по биту). Клик по кружку `cx` → `cx = 9-cx`, XOR соответствующего бита. Флаги хранятся в строке `flagsData` (по байту на спрайт), экспорт — hex через `;`.

**Клавиатура** (`se.keymap` + `se:keypressed`):

| Клавиша | Действие |
|---|---|
| `q` / `e` | предыдущий/следующий цвет ЛКМ (`colsL`) |
| `shift+q` / `shift+e` | то же для `colsR` |
| `w`/`a`/`s`/`d` | перемещение выбора спрайта вверх/влево/вниз/вправо (с переходом между банками) |
| `1..4` | банк |
| `z` / `x` | инструмент Pencil / Fill |
| `delete` | стереть спрайт |
| `r`, `shift-r`, `f`, `shift-f`, `i` | трансформации |
| `ctrl-c` / `ctrl-v` | копировать / вставить |

**Мышь:** ЛКМ рисует; **ПКМ или зажатый Shift = второй цвет** (`if isKDown("lshift","rshift") or isMDown(2) then b = 2 end`); курсор меняется на `pencil`/`eraser`/`bucket`. Колеса и панорамирования нет.

**Undo/redo: ОТСУТСТВУЕТ ПОЛНОСТЬЮ.** В `sprite.lua` нет ни `undo`, ни истории.

**Буфер обмена** — системный, текстовый, с поддержкой формата PICO‑8:
```lua
if data:sub(1,5) == "[gfx]" then -- PICO-8 Paste
  data = data:sub(6,-7)
  local width  = tonumber(data:sub(1,2),16)
  local height = tonumber(data:sub(3,4),16)
  ...
```
Копирование — `imagedata:encode()` без заголовка, только hex‑пиксели. Вставка требует квадратный размер ≥ 8.

### 2.3 PICO‑8 — мануал

> «The sprite editor is designed to be used both for sprite-wise editing and for freeform pixel-level editing. The sprite navigator at the bottom of the screen provides an 8x8 sprite-wise view into the sprite sheet, but it is possible to use freeform tools (pan, select) when dealing with larger or oddly sized areas.»

| Инструмент | Цитата |
|---|---|
| Draw | «Click and drag on the sprite to plot pixels, or use RMB to select the colour under the cursor. All operations apply only to the visible area, or the section if there is one. **Hold CTRL to search and replace colour.**» |
| Stamp | «Click to stamp whatever is in the copy buffer. **Hold CTRL to treat colour 0 (black) as transparent.**» |
| Select (`SHIFT` или `S`) | «Click and drag to create a rectangular selection. To remove the selection, press ENTER or click anywhere.» + «To select sprites, shift-drag in the sprite navigator. **To select the sprite sheet press CTRL-A (repeat to toggle off the bottom half shared with map data)**» |
| Pan (`SPACE`) | «Click and drag to move around the sprite sheet.» |
| Fill | «Fill with the current colour. This applies only to the current selection, or the visible area if there is no selection.» |
| Shape | «Click the tool button to cycle through: oval, rectangle, line options. Hold CTRL to get a filled oval or rectangle. **Hold SHIFT to snap to circle, square, or low-integer-ratio line.**» |

Клавиши:

| Клавиша | Цитата из мануала |
|---|---|
| `CTRL-Z` | Undo |
| `CTRL-C/X` | «Copy/Cut selected area or selected sprites» |
| `CTRL-V` | «Paste to current sprite location» |
| `Q/A, W/Z` | «Switch to previous/next sprite» |
| `1,2` | «Switch to previous/next colour» |
| `TAB` | «Toggle fullscreen view» (+ `SHIFT-TAB` — «full-fullscreen mode (with no red menu bars)») |
| Колесо / `<` `>` | «to zoom (centered in fullscreen)» |
| `CTRL-H` | «to toggle hex view (shows sprite index in hexadecimal)» |
| `CTRL-G` | «to toggle black grid lines when zoomed in» |
| `F` / `V` / `R` | «Flip sprite horizontally» / «vertically» / «Rotate (requires a square selection)» |
| стрелки | «to shift (loops if sprite selection)» |
| `DEL`/`BACKSPACE` | «to clear selected area» |
| `CTRL-B` | «paste 2x2 original size ("paste big")» *(из changelog)* |

Флаги:
> «The 8 coloured circles are sprite flags for the current sprite. These have no particular meaning, but can be accessed using the FGET() / FSET() functions. **They are indexed from 0 starting from the left.**»

Импорт:
> «To load a png file of any size into the sprite sheet, first select the sprite that should be the top-left corner destination, and then either type "IMPORT IMAGE_FILE.PNG" or drag and drop the image file into the PICO-8 window.»

---

## 3. Редактор КАРТЫ

### 3.1 TIC‑80 — `src/studio/editors/map.c` + `map.h`

Константы:
```c
#define MAP_X (0)
#define MAP_Y (TOOLBAR_SIZE)
#define MAX_SCROLL_X (TIC_MAP_WIDTH * TIC_SPRITESIZE)   // 240*8
#define MAX_SCROLL_Y (TIC_MAP_HEIGHT * TIC_SPRITESIZE)  // 136*8
#define FILL_STACK_SIZE (TIC_MAP_WIDTH*TIC_MAP_HEIGHT)
```

**Инструменты** (`map->mode`, кнопки в тулбаре справа налево):

| Функция | Режим | Подсказка |
|---|---|---|
| `drawPenButton` | `MAP_DRAW_MODE` | `DRAW [1]` |
| `drawHandButton` | `MAP_DRAG_MODE` | `DRAG MAP [2]` |
| `drawSelectButton` | `MAP_SELECT_MODE` | `SELECT [3]` |
| `drawFillButton` | `MAP_FILL_MODE` | `FILL [4]` |

Дополнительные кнопки:

| Функция | Подсказка | Действие |
|---|---|---|
| `drawGridButton` | `SHOW/HIDE GRID [\`]` | сетка (`canvas.grid`, по умолчанию **включена**) |
| `drawWorldButton` | `WORLD MAP [tab]` | мини‑карта всей карты (`src/studio/editors/world.c`) |
| `drawSheetButton` | `SHOW TILES [shift]` | выдвинуть/убрать панель тайлов (анимация) |
| `drawBankButtons` | `TILES` / `SPRITES` | банк листа |
| `drawBppButtons` | `4/2/1 BITS PER PIXEL` | BPP листа |
| `drawPagesButtons` | `PAGE %i` | страница листа |

**Палитра тайлов** (`drawSheetReg` / `drawSheetVBank1`): панель 128×128 справа вверху, выезжает при удержании `Shift` или кнопкой. Рисуется целиком со спрайта 0:
```c
tic_api_spr(tic, 0, pos.x, pos.y + map->anim.pos.sheet, TIC_SPRITESHEET_COLS, TIC_SPRITESHEET_COLS, NULL, 0, 1, ...);
```
Выделение прямоугольником протяжкой ЛКМ (`map->sheet.rect`, любой размер N×M, не только 2×2/4×4). Начальное значение — `.sheet.rect = {0, 0, 1, 1}`, то есть **тайл 0**.

**Строка статуса.** Отдельной нет; информация распределена:
- `drawTileIndex(map, TIC80_WIDTH/2 - TIC_FONT_WIDTH, 1)` — в центре тулбара печатает `#%03i`: индекс тайла под курсором на карте (`tic_api_mget`) или индекс тайла под курсором в палитре;
- `drawCursorPos` — рядом с курсором «плашка» `%03i:%03i` (координаты тайла), а в режиме FILL с зажатым Ctrl под ней добавляется слово `replace`;
- в vbank1 рисуются серые линии, отмечающие границы «экранов» (`screenScrollX/Y`).

**Мышь:**

| Действие | Эффект | Код |
|---|---|---|
| ЛКМ (DRAW) | ставит выбранный блок тайлов, но **только на сетке, кратной размеру блока**: `if(w % sheet.rect.w == 0 && h % sheet.rect.h == 0) setMapSprite(...)` | `processMouseDrawMode` |
| **СКМ (DRAW)** | «пипетка тайла»: берёт тайл под курсором в палитру: `map->sheet.rect = (tic_rect){index % 16, index / 16, 1, 1}` | `processMouseDrawMode` |
| **ПКМ (везде)** | панорамирование карты | `drawMapReg`: `... || checkMouseDown(..., tic_mouse_right)` |
| `Space` + ЛКМ | панорамирование | `bool space = tic_api_key(tic, tic_key_space);` |
| ЛКМ (DRAG) | панорамирование | `processMouseDragMode` |
| ЛКМ (SELECT) | прямоугольное выделение; при отпускании 1×1 сбрасывается | `processMouseSelectMode` |
| ЛКМ (FILL) | заливка `fillMap` (BFS-стек на `FILL_STACK_SIZE`), **`Ctrl` → `replaceTile`** (замена по всей карте/выделению с сохранением фазы паттерна через `moduloWrap`) | `processMouseFillMode` |
| Колесо **вниз** | переход в WORLD MODE | `tick`: `if(tic->ram->input.mouse.scrolly < 0) setStudioMode(..., TIC_WORLD_MODE)` |
| Карта **зациклена** по обеим осям | `normalizeMap`, `tic_modulo` | |

**Клавиатура** (`processKeyboard`):

| Клавиша | Действие |
|---|---|
| `1/2/3/4` | инструмент |
| `` ` `` | сетка |
| `Tab` | WORLD MODE |
| `Shift` (нажатие) | показать/скрыть палитру тайлов |
| стрелки | скролл на 1 px за кадр |
| `Delete` | `deleteSelection` — **заполнить выделение нулями** |
| `Ctrl+Z` / `Ctrl+Y` | undo / redo |
| `Ctrl+X/C/V` | буфер |
| `Alt` | обработка пропускается |

**Undo/redo:** `history_create(src, sizeof(tic_map))` — вся карта целиком. `history_add` в `setMapSprite` (то есть на каждый кадр установки блока), `drawPasteData`, `processMouseFillMode`, `deleteSelection`.

**Буфер:** `copySelectionToClipboard` кладёт `[w][h][данные...]` (2 байта заголовка + w*h байт) в системный буфер как hex. `copyFromClipboard` валидирует `data[0]*data[1] == size-2` и переводит редактор в `MAP_SELECT_MODE` с «плавающим» блоком, который ставится по клику ЛКМ (`drawPasteData`). `Ctrl+X` = copy + delete + reset selection.

**Мини‑карта (WORLD MODE)** — `src/studio/editors/world.c`: превью всей карты 1 px = 1 тайл, сетка по экранам, красная рамка вьюпорта; ЛКМ — переместить вьюпорт, клик — вернуться в MAP MODE; `Tab` или колесо вверх — назад.

### 3.2 LIKO‑12 — `src/OS/DiskOS/Editors/tile.lua`

Карта `MapW, MapH = math.floor(swidth*0.75), sheight` = **144 × 128** тайлов. Видимая область — весь экран минус тулбар 9 px справа.

**Инструменты** (`selectedTool`, вертикальная полоса справа, спрайты 114..118):

| # | Инструмент | Поведение |
|---|---|---|
| 0 | Pencil | `Map:cell(cx,cy,selectedTile)` |
| 1 | Bucket | `queuedFill(Map,cx,cy,selectedTile)` (BFS по всей карте) |
| 2 | Hand (Pan) | перетаскивание карты |
| 3 | Select | прямоугольное выделение с авто‑скроллом у краёв (`t:update`, `mvspeed = 64`) |
| 4 | Menu | открыть палитру тайлов на весь экран |

**Хотбар из 10 слотов тайлов** (`hotbarTiles`, вверху той же правой полосы). Клавиши `1..9`, `0`; **колесо мыши переключает слот** (`t:wheelmoved`). Пустой слот (tile 0) рисуется системным спрайтом 120.

**Палитра тайлов** (`t:drawMenu`) — сетка 22×12 = 264 ячейки (`spritesGrid`), открывается инструментом Menu **или удержанием `Alt`** (поверх затемнённого скриншота карты). Ячейки с `tid > 255` помечены штриховкой и недоступны (курсор `cross`).

**Строка статуса:** только при активном выделении — блок 31×29 px в левом нижнем углу с `x:`, `y:`, `w:`, `h:` выделения.

**Мышь:**

| Действие | Эффект |
|---|---|
| ЛКМ | текущий инструмент |
| **ПКМ** | `if isMDown(2) or (b and b == 2) then selectedTile = 0 end` — **ставит тайл 0**, то есть работает как ластик |
| **СКМ** | временное панорамирование: `if isMDown(3) then selectedTool = 2 end` (только когда активен Pencil или Bucket) |
| Колесо | смена слота хотбара |
| Alt (держать) | палитра тайлов |

**Undo/redo: ОТСУТСТВУЕТ.**
**Копирование/вставка карты: ОТСУТСТВУЕТ** — инструмент Select только показывает координаты и размер, буфер не задействован.

### 3.3 PICO‑8 — мануал

> «The PICO-8 map is a 128x32 (or 128x64 using shared space) block of 8-bit values. Each value is shown in the editor as a reference to a sprite (0..255), but you can of course use the data to represent whatever you like.»

> «WARNING: The second half of the sprite sheet (banks 2 and 3), and the bottom half of the map share the same cartridge space.»

> «The tools are similar to the ones used in sprite editing mode. Select a sprite and click and drag to paint values into the map.»

> «To draw multiple sprites, select from sprite navigator with shift+drag. To copy a block of values, use the selection tool and then stamp tool to paste. To pan around the map, use the pan tool or hold space. Q,W to switch to previous/next sprite. Mousewheel or < and > to zoom (centered in fullscreen). **CTRL-H to toggle hex view (shows tile values and sprite index in hexadecimal)**»

Перенос спрайтов без разрыва ссылок:
> «1. Select the area of the map you would like to alter (defaults to the top half of the map) press ctrl-A twice to select the full map including shared memory
> 2. Select the sprites you would like to move (while still in map view), and press Ctrl-X
> 3. Select the destination sprite (also while still in map view) and press Ctrl-V»
> «Note: this operation modifies the undo history for both the map and sprite editors, but PICO-8 will try to keep them in sync where possible.»

Из changelog:
> «Added: In map editor, **non-zero cels that are drawn all black are marked with a single blue dot**» — то есть редактор специально отличает «тайл 0» от «тайл с чёрным рисунком».
> «Added: DEL / backspace to clear selected region in gfx / map editors, and ctrl-x to cut»

---

## 4. Редактор КОДА

### 4.1 TIC‑80 — `src/studio/editors/code.c`

Один буфер (`TIC_CODE_SIZE`), **вкладок нет**. Режимы (`code->mode`): `TEXT_EDIT_MODE`, `TEXT_DRAG_CODE`, `TEXT_FIND_MODE`, `TEXT_GOTO_MODE`, `TEXT_BOOKMARK_MODE`, `TEXT_OUTLINE_MODE`.

**Тулбар** (`drawCodeToolbar`):

| Кнопка | Подсказка |
|---|---|
| hand | `DRAG [right mouse]` |
| find | `FIND [ctrl+f]` |
| goto | `GOTO [ctrl+g]` |
| bookmark | `BOOKMARKS [ctrl+b]` |
| outline | `OUTLINE [ctrl+o]` |
| `F` | `SWITCH FONT` (альт-шрифт) |
| shadow | `SHOW SHADOW` (тень текста) |
| run | `RUN [ctrl+r]` |

**Строка статуса** (`drawStatus`, `updateEditor`) — нижняя строка экрана:
```c
sprintf(code->status.line, "line %i/%i col %i", line + 1, getLinesCount(code) + 1, column + 1);
sprintf(code->status.size, "size %i/%i", codeLen, MAX_CODE);
code->status.color = codeLen > MAX_CODE ? tic_color_red : tic_color_white;
```
Слева — позиция курсора, справа — размер кода; фон краснеет при переполнении. (В режиме byte-battle вместо этого показываются таймер и лимит.)

**Клавиатура** (`processKeyboard`; есть три раскладки — `KEYBIND_STANDARD`, `KEYBIND_EMACS`, `KEYBIND_VI`, для vi — отдельный `processViKeyboard`). Стандартная:

| Клавиша | Действие |
|---|---|
| `Ctrl+A` | select all |
| `Ctrl+Z` / `Ctrl+Y` | undo / redo |
| `Ctrl+F` / `Ctrl+G` / `Ctrl+B` / `Ctrl+O` | find / goto / bookmarks / outline |
| `Ctrl+D` | дублировать строку (`dupLine`) |
| `Ctrl+K` | удалить строку (`deleteLine`) |
| `Ctrl+/` | закомментировать/раскомментировать (`commentLine`) |
| `Ctrl+J` | новая строка |
| `Ctrl+N/P/E` | вниз/вверх/конец строки |
| `Ctrl+Home/End` | начало/конец файла |
| `Ctrl+L` | центрировать скролл (`recenterScroll`) |
| `Ctrl+Up` / `Ctrl+Down` | `extirpSExp` / `sexpify` (структурное редактирование скобок) |
| `Ctrl+Tab` / `Shift+Tab` | отступ / снятие отступа (`doTab`) |
| `Alt+Left/Right` | по словам |
| `Alt+P` / `Alt+N` | page up / page down |
| `Ctrl/Alt+Delete/Backspace` | удалить слово |
| `F1` | следующая закладка; `Shift+F1` — предыдущая; `Ctrl+F1` — поставить/снять; `Ctrl+Shift+F1` — снять все |
| `Shift+Enter` | новая строка с автозакрытием (`newLineAutoClose`) |
| `Esc` | выход из find/goto/outline в edit |

**Мышь** (`processMouse`):

| Действие | Эффект |
|---|---|
| ЛКМ | поставить курсор / протяжкой выделять |
| `Shift`+ЛКМ | расширить выделение |
| Двойной клик | выделить слово (`leftWordPos`/`rightWordPos`) |
| **ПКМ (протяжка)** | панорамирование текста (`useDrag`), курсор `hand` |
| Колесо | скролл (в `textEditTick` читаются и `scrollx`, и `scrolly`) |

**Undo/redo:** `history_add(code->history)` в функции `history(Code*)`, вызываемой после каждой правки. **Гранулярность — одна элементарная операция** (символ, удаление, вставка, дублирование строки…). В vi-режиме шаг откладывается до выхода из INSERT:
```c
static void history(Code* code)
{
    //if we are in insert mode we want want all changes we make to be reflected
    //in the undo/redo history only when we leave it
    if (checkStudioViMode(code->studio, VI_INSERT)) return;
    packState(code); history_add(code->history);
}
```
В историю пакуется и позиция курсора (`packState`/`unpackState`).

**Буфер:** обычный системный текстовый (`copyToClipboard`/`cutToClipboard`/`copyFromClipboard`, `clipboardHasNewline` для построчного поведения).

### 4.2 LIKO‑12 — `src/OS/DiskOS/Editors/code.lua`

Буфер — таблица строк. Подсветка — `Libraries.SyntaxHighlighter` с темой (`text=7, keyword=10, number=12, comment=13, string=11, api=14, callback=15, selection=6, escape=12, error=8`). Вкладок нет.

**Строка статуса** (`ce:drawLineNum`) — нижняя полоса:
```lua
local linestr = "LINE "..tostring(self.cy).."/"..tostring(#buffer).."  CHAR "..tostring(self.cx-1).."/"..tostring(buffer[self.cy]:len())
```
В режиме инкрементального поиска вместо неё — `ISRCH: <текст>`.

**Клавиатура** (`ce.keymap`):

| Клавиша | Действие |
|---|---|
| стрелки, `home`, `end`, `pageup`, `pagedown` | навигация |
| `shift+`стрелки | выделение |
| `Alt+Up` / `Alt+Down` | к предыдущей / следующей `function ` (`searchPreviousFunction`/`searchNextFunction`) |
| `Ctrl+I` | вкл/выкл инкрементальный поиск |
| `Ctrl+K` | повторить поиск |
| `Ctrl+X` / `Ctrl+C` / `Ctrl+V` | вырезать / копировать / вставить |
| `Ctrl+A` | выделить всё |
| `Ctrl+Z` | undo |
| `Ctrl+Y` и `Shift+Ctrl+Z` | redo |
| `Tab` | вставляет **один пробел** (`self:textinput(" ")`) |

Есть флаг `readonly` — при попытке правки выводится `The file is readonly !`.

**Мышь:** ЛКМ ставит курсор; протяжка выделяет; **авто‑скролл** при уходе выше 10 % / ниже 90 % высоты экрана (`sflag`, `stime = 0.1`); колесо скроллит по осям X и Y. ПКМ не используется. Есть поддержка touch (`ce.touches`).

**Undo/redo:** `ce.undoStack` / `ce.redoStack`, **полные снимки текста** (`self:export()`) плюс состояние курсора/выделения. Обрамление `ce:beginUndoable()` / `ce:endUndoable()` **с поддержкой вложенности** (`currentUndo.count`) — то есть, например, вся вставка многострочного текста = **один** шаг undo. Любой новый шаг очищает redo-стек. Размер стека не ограничен.

**Буфер обмена** — системный, plain text; при вставке табы заменяются на пробелы.

### 4.3 PICO‑8 — мануал

| Клавиша | Цитата |
|---|---|
| `CTRL-X, C, V` | «to cut copy or paste selected» |
| `CTRL-Z, Y` | «to undo, redo» |
| `CTRL-F` | «to search for text in the current tab» |
| `CTRL-G` | «to repeat the last search again» |
| `CTRL-L` | «to jump to a line number» |
| `CTRL-UP, DOWN` | «to jump to start or end» |
| `ALT-UP, DOWN` | «to navigate to the previous, next function» |
| `CTRL-LEFT, RIGHT` | «to jump by word» |
| `CTRL-W, E` | «to jump to start or end of current line» |
| `CTRL-D` | «to duplicate current line» |
| `TAB` | «to indent a selection (shift to un-indent)» |
| `CTRL-B` | «to comment / uncomment selected block» |
| `CTRL-U` | «to get help on the keyword under the cursor» |
| `SHIFT-L,R,U,D,O,X` | «To enter special characters that represent buttons (and other glyphs)» |
| `CTRL-J / K / P` | Hiragana / Katakana / Puny шрифты |

**Вкладки кода** (единственный из трёх, у кого они есть):
> «Click the [+] button at the top to add a new tab. Navigate tabs by left-clicking, or with CTRL-TAB, SHIFT-CTRL-TAB. To remove the last tab, delete any contents and then moving off it (CTRL-A, DEL, CTRL-TAB). When running a cart, a single program is generated by concatenating all tabs in order.»

**Строка статуса:**
> «The number of code tokens is shown at the bottom right. One program can have a maximum of 8192 tokens. […] **Right click to toggle through other stats (character count, compressed size).** If a limit is reached, a warning light will flash. This can be disabled by right-clicking.»

---

## 5. Редактор ЗВУКА (SFX)

### 5.1 TIC‑80 — `src/studio/editors/sfx.c`

64 сэмпла × 30 тиков. Экран (`tick`):
```c
drawCanvas(sfx, 88, 12, sfx->volwave);       // WAVE или VOLUME (переключаемая панель)
drawCanvas(sfx, 88, 51, SFX_CHORD_PANEL);    // ARPEGG
drawCanvas(sfx, 88, 90, SFX_PITCH_PANEL);    // PITCH
drawSelector(sfx, 9, 12);
drawPiano(sfx, 5, 127);
drawWavePanel(sfx, 7, 41);
```

**Панели‑«светодиоды»** (`drawCanvasLeds`): 30 колонок × 16 строк, ячейка 4×2 px.

| Панель | Что редактирует | Цвета |
|---|---|---|
| `SFX_WAVE_PANEL` | номер волны на каждый тик | orange/red |
| `SFX_VOLUME_PANEL` | громкость 0..15 | light_blue/blue |
| `SFX_CHORD_PANEL` | арпеджио | light_green/green |
| `SFX_PITCH_PANEL` | питч (±, ноль по центру) | yellow/orange |

Подсказка при наведении: `[x=%02i y=%02i]`. Есть «hold»-режим (`hold`/`unhold`): при протяжке значение фиксируется по первой ячейке.

**Кнопки в каждой панели:**

| Контрол | Подсказка |
|---|---|
| `WAV` / `VOL` (переключатель верхней панели) | `wave data` / `volume data` |
| `L` / `R` (стерео) | `left stereo` / `right stereo` |
| `DOWN` | `up/down arpeggio` (`effect->reverse`) |
| `x16` | `x16 pitch` (`effect->pitch16x`) |
| `LOOP:` ◀▶ (2 пары стрелок, hex-значения) | `set loop start` / `set loop size` |

**Панель волн** (`drawWavePanel`):
- Большой редактор текущей волны 64×32 px (32 значения по 4 бита, `WAVE_VALUES`, `WAVE_MAX_VALUE`), рисуется ЛКМ, подсказка `[x=%02i y=%02i]`.
- Сетка 4×4 из **16 волн** (`WAVES_COUNT`), подсказка `select wave #%02i`; клик применяет волну ко **всем 30 тикам**.
- Вертикальная панель кнопок (`drawWavesBar`): `CUT WAVE`, `COPY WAVE`, `PASTE WAVE`, `UNDO WAVE`, `REDO WAVE` — **отдельная история для волн** (`sfx->waveHistory`).

**Селектор** (`drawSelector`, `drawSelectorPanel`): `IDX` + номер, сетка 64 квадратика 3×3 в 4 группах по 4×4; подсказка `edit sfx #%02i`; пустые сэмплы окрашены темнее (`memcmp` с нулём).

**Скорость** (`drawSpeedPanel`): 8 полосок, подсказка `set speed to %i`.

**Пианино** (`drawPiano` / `drawPianoOctave`): 8 октав (`OCTAVES`), в каждой 7 белых + 5 чёрных клавиш; подсказка `play %s%i note`.

**Клавиатура:**

| Клавиша | Действие |
|---|---|
| `z s x d c v g b h n j m , l . ; /` | нижняя октава (17 клавиш) |
| `q 2 w 3 e r 5 t 6 y 7 u` | +1 октава |
| `i 9 o 0 p` | ещё выше |
| `Space` | играть |
| `Shift+Z` / `Shift+X` | октава −/+ |
| `Left` / `Right` | предыдущий/следующий сэмпл (`sfx->index`) |
| `Delete` | `resetSfx` — обнулить сэмпл |
| `Ctrl+Z` / `Ctrl+Y` | undo / redo |
| `Ctrl+X/C/V` | буфер |

**Undo/redo:** две независимые истории — `history_create(&src->samples, sizeof(tic_samples))` и `history_create(&src->waveforms, sizeof(tic_waveforms))`. `history_add` вызывается **на каждый кадр изменения** (в `drawCanvasLeds`, `drawWavePanel`, `drawSpeedPanel`, `drawWaves`, `drawPianoOctave`, на кнопках LOOP).

**Буфер:** `toClipboard(effect, sizeof(tic_sample), true)` — весь сэмпл целиком; волна копируется отдельно как `sizeof(tic_waveform)`.

### 5.2 LIKO‑12 — `src/OS/DiskOS/Editors/sfx.lua`

64 слота × 32 ноты, 6 волн, громкость 0..7, октавы 1..8.

**Области:**

| Область | Grid | Что делает |
|---|---|---|
| Питч | `pitchGrid = {0,9, 32*4, 12*7, 32, 12*7}` | ЛКМ ставит ноту; **ПКМ протяжкой задаёт выделение** (`select_start`) |
| Громкость | `volumeGrid = {0, sh-8-16-1, 32*4, 16, 32, 8}` | ЛКМ ставит громкость 0..7 |
| SLOT ◀ N ▶ | | смена слота, **сбрасывает историю** (`se:clearHistory(); se:addHistory()`) |
| SPEED ◀ N ▶ | | шаг 0.25, от 0.25 до 24.75 |
| Кнопка Play | `playRect` | play/stop |
| 6 волн | `waveGrid` | выбор волны (клавиши `1..6`) |
| Selection ◀ A ▶ ✕ ◀ B ▶ | | начало/конец выделения и сброс к 1..32 |

**Панель инструментов** (`se:toolsMouse`, сетка 5×2):

| Ряд 1 | Ряд 2 |
|---|---|
| Pitch Up | Octave Up |
| Pitch Down | Octave Down |
| Copy | Flatten (усреднить высоту нот) |
| Paste | Undo |
| Delete | Redo |

Плюс ряд из 6 волн: клик применяет волну ко всем нотам выделения.

**Клавиатура** (`se.keymap`):

| Клавиша | Действие |
|---|---|
| `Space` | play/stop |
| `1..6` | волна |
| `z` / `x` | speed −/+ |
| `a` / `s` | слот −/+ |
| `f` | flatten |
| `m` / `n` | добавить шаг истории / очистить историю |
| `Up` / `Down` | pitch up/down по выделению |
| `Left` / `Right` | сдвинуть выделение |
| `Ctrl+C` / `Ctrl+V` / `Delete` | copy / paste / delete по выделению |
| `Ctrl+Z` / `Ctrl+Y` | undo / redo |

**Undo/redo:** кольцевая история **на 32 шага** (`history_size = 32`), только для **текущего слота**, полные снимки (`sfxdata[selectedSlot]:export()`). Шаг добавляется каждым инструментом, а для рисования мышью — один раз по отпусканию кнопки:
```lua
function se:checkGraphs()
  if(touched_graphs)then se:addHistory(); touched_graphs = false end
end
```
**⇒ у LIKO‑12 гранулярность undo в SFX = один «мазок», а не кадр.** При смене слота история очищается.

**Буфер:** системный, текстовый — фрагмент экспорта по 6 символов на ноту.

### 5.3 PICO‑8 — мануал

> «There are 64 SFX ("sound effects") in a cartridge, used for both sound and music. Each SFX has 32 notes, and each note has: A frequency (C0..C5), An instrument (0..7), A volume (0..7), An effect (0..7)»
> «A play speed (SPD): the number of 'ticks' to play each note for. This means that 1 is fastest, 3 is 3x as slow, etc.»
> «Loop start and end: this is the note index to loop back and to. Looping is turned off when the start index >= end index»
> «There are 2 modes for editing/viewing a SFX: Pitch mode […] and tracker mode […]. The mode can be changed using the top-left buttons, **or toggled with TAB**.»

Pitch mode:
> «Click and drag on the pitch area to set the frequency for each note, using the currently selected instrument (indicated by colour). **Hold shift to apply only the selected instrument. Hold CTRL to snap entered notes to the C minor pentatonic scale. Right click to grab the instrument of that note.**»

Tracker mode:
> «Each note shows: frequency octave instrument volume effect»
> «To enter a note, use q2w3er5t6y7ui zsxdcvgbhnjm (piano-like layout)»
> «Hold shift when entering a note to transpose -1 octave .. +1 octave»
> «To delete a note, use backspace or set the volume to 0»
> «Click and then shift-click to select a range that can be copied (CTRL-C) and pasted (CTRL-V). **Note that only the selected attributes are copied.** Double-click to select all attributes of a single note.»
> «PAGEUP/DOWN or CTRL-UP/DOWN to skip up or down 4 notes; HOME/END to jump to the first or last note; CTRL-LEFT/RIGHT to jump across columns»

Общее:
> «- + to navigate the current SFX; SPACE to play/stop; SHIFT-SPACE to play from the current SFX quarter (group of 8 notes); A to release a looping sample»
> «Left click or right click - to increase / decrease the SPD or LOOP values. **Hold shift when clicking to increase / decrease by 4. Alternatively, click and drag left/right or up/down**»
> «**Shift-click an instrument, effect, or volume to apply to all notes.**»

Эффекты 0..7: none / slide / vibrato / drop / fade in / fade out / arpeggio fast / arpeggio slow.
Фильтры (только в tracker mode): `NOIZ`, `BUZZ`, `DETUNE-1`, `DETUNE-2`, `REVERB`, `DAMPEN`.

---

## 6. Редактор МУЗЫКИ

### 6.1 TIC‑80 — `src/studio/editors/music.c`

8 треков × 16 фреймов × 4 канала; 60 паттернов × 64 строки. **Две вкладки** (`drawModeTabs`): `PIANO MODE` и `TRACKER MODE` (по умолчанию `MUSIC_PIANO_TAB`).

**Верхняя панель** (`drawTopPanel`) — 4 «свитча» с ◀▶ и редактируемым полем:

| Поле | Диапазон |
|---|---|
| `TRACK` | 0..7 (`MUSIC_TRACKS`) |
| `TEMPO` | 40..250 (`setTempo`, `DEFAULT_TEMPO=150`) |
| `SPD` | 1..31 (`setSpeed`, `DEFAULT_SPEED=6`) |
| `ROWS` | до 64 (`setRows`) |

**Кнопки транспорта** (`drawPlayButtons`):

| Кнопка | Подсказка |
|---|---|
| loop | `LOOP` |
| follow | `FOLLOW [ctrl+f]` |
| sustain | `SUSTAIN NOTES ... BETWEEN FRAMES` |
| play from now | `PLAY FROM NOW ... [shift+enter]` |
| play frame | `PLAY FRAME ... [enter]` |
| play track | `PLAY TRACK ... [space]` |
| stop | `STOP [enter]` |

Справа вверху — осциллограф (`drawWaveform(music, 205, 9)`).

**Трекер** (`drawTrackerLayout`): столбец из 16 фреймов (`drawTrackerFrames`, подсказка `select frame`, номер текущей строки над ним), 4 канала по 16 видимых строк (`TRACKER_ROWS = 64/4`). Формат строки — 8 колонок:

| Индекс | Колонка |
|---|---|
| 0 | `ColumnNote` |
| 1 | `ColumnSemitone` |
| 2 | `ColumnOctave` |
| 3 | `ColumnSfxHi` |
| 4 | `ColumnSfxLow` |
| 5 | `ColumnCommand` |
| 6 | `ColumnParameter1` |
| 7 | `ColumnParameter2` |

Над каждым каналом — editbox с номером паттерна и «тумблер» вкл/выкл канала (`drawTumbler`, подсказка `on/off channel`; **`Ctrl`+клик = solo**).

**Клавиатура трекера** (`processTrackerKeyboard`):

| Клавиша | Действие |
|---|---|
| `Space` | play/stop трек |
| `Enter` | play frame / stop; `Shift+Enter` — play from now |
| `Ctrl+F` | follow |
| `Ctrl+A` | `selectAll` — выделить весь канал |
| `Ctrl+Z` / `Ctrl+Y` | undo / redo |
| стрелки | навигация; `Shift`+стрелки — выделение |
| `Ctrl+Up/Down` | SFX −/+ у ноты |
| `Ctrl+Left/Right` | предыдущий/следующий фрейм |
| `Home`/`End`/`PageUp`/`PageDown` | навигация |
| `Tab` | `doTab` — переход между областью паттернов и трекером |
| `Delete` / `Backspace` / `Insert` | удалить / удалить со сдвигом / вставить строку |
| `Ctrl+F1..F4` | −полутон / +полутон / −октава / +октава |
| `A` | стоп‑нота (`setStopNote`) |
| `Shift+Z` / `Shift+X` | `music->last.octave` −/+ |
| `+` / `-` | изменить номер паттерна канала |
| ноты | `z s x d c v g b h n j m , l . ; /` + `q 2 w 3 e r 5 t 6 y 7 u` + `i 9 o 0 p` |
| в колонке октавы | цифры `1..8` |
| в колонках SFX | десятичные цифры |
| в колонке команды | буква из `MusicCommands` |
| в колонках параметров | hex‑цифры |

**Piano‑режим** (`drawPianoLayout`): колонки `PianoChannel1..4`, `PianoSfxColumn`, `PianoXYColumn`; отдельная клавиатура (`drawPianoRoll`); кнопка размера такта `4/4` ↔ `3/4` (`drawBeatButton`, `music->beat34`), влияющая на подсветку долей (`noteBeat`).

**Мышь:**

| Действие | Эффект |
|---|---|
| ЛКМ по каналу | поставить курсор; протяжка = выделение (`music->tracker.select`) |
| Колесо | скролл на `NOTES_PER_BEAT` (4) строки |
| **`Ctrl` + колесо** | `scrollNotes` — транспонировать выделенные ноты |
| ЛКМ по столбцу фреймов | выбрать фрейм |
| ЛКМ/протяжка по editbox | менять номер паттерна |

**Undo/redo:** `history_create(src, sizeof(tic_music))` — вся музыка. `history_add` в конце `processTrackerKeyboard` (после любого ввода), в `setTempo/setSpeed/setRows`, `scrollNotes`, при вставке.

**Буфер:** отдельные пути для трекера и пианино — `copyTrackerToClipboard` / `copyPianoToClipboard` / `copyTrackerFromClipboard` / `copyPianoFromClipboard`, через тот же системный hex‑буфер.

### 6.2 LIKO‑12

**Редактора музыки нет.** Слот занят заглушкой `Editors/soon.lua` («WORK IN PROGRESS...»), в `edit:initialize` его `saveid = -1` (ничего не сохраняет).

### 6.3 PICO‑8 — мануал

> «Music in PICO-8 is controlled by a sequence of 'patterns'. Each pattern is a list of 4 numbers indicating which SFX will be played on that channel.»

> «Playback flow can be controlled using the 3 buttons at the top right. When a pattern has finished playing, the next pattern is played unless: there is no data left to play (music stops), a STOP command is set on that pattern (the third button), a LOOP BACK command is set (the 2nd button), in which case the music player searches back for a pattern with the LOOP START command set (the first button) or returns to pattern 0 if none is found.»

> «When a pattern has SFXes with different speeds, the pattern finishes playing when the left-most non-looping channel has finished playing.»

> «To select a range of patterns: click once on the first pattern in the pattern navigator, then shift-click on the last pattern. Selected patterns can be copied and pasted with CTRL-C and CTRL-V. **When pasting into another cartridge, the SFX that each pattern points to will also be pasted (possibly with a different index) if it does not already exist.**»

> «In addition to the 8 built-in instruments, custom instruments can be defined using the first 8 SFX. Use the toggle button to the right of the instruments to select an index, which will show up in the instrument channel as green instead of pink.»

---

# 7. ОСОБО ВАЖНЫЕ ВОПРОСЫ

## 7.1 Тайл 0 на карте

### Рантайм: рисует ли `map()` тайл 0?

| Консоль | Ответ | Доказательство |
|---|---|---|
| **TIC‑80** | **ДА, рисует.** Никакой особой обработки нуля нет | `src/core/draw.c`, `drawMap()` |
| **LIKO‑12** | **НЕТ, пропускает** (и вообще id спрайтов с 1) | `src/OS/DiskOS/Libraries/map.lua`, `Map:draw()` |
| **PICO‑8** | **НЕТ, пропускает**, но поведение отключается | мануал, `MAP()` |

**TIC‑80** — `src/core/draw.c`, функция `drawMap`, полный цикл без единой проверки на 0:
```c
static void drawMap(tic_core* core, const tic_map* src, s32 x, s32 y, s32 width, s32 height,
                    s32 sx, s32 sy, u8* colors, s32 count, s32 scale, RemapFunc remap, void* data)
{
    ...
    for (s32 j = y, jj = sy; j < y + height; j++, jj += size)
        for (s32 i = x, ii = sx; i < x + width; i++, ii += size)
        {
            s32 mi = tic_modulo(i, TIC_MAP_WIDTH);
            s32 mj = tic_modulo(j, TIC_MAP_HEIGHT);
            s32 index = mi + mj * TIC_MAP_WIDTH;
            RemapResult retile = { *(src->data + index), tic_no_flip, tic_no_rotate };
            if (remap) remap(data, mi, mj, &retile);
            tic_tileptr tile = tic_tilesheet_gettile(&sheet, retile.index, true);
            drawTile(core, &tile, ii, jj, colors, count, scale, retile.flip, retile.rotate);
        }
}
```
Тайл 0 проходит через `drawTile` наравне со всеми. «Пустоты» в TIC‑80 достигаются не индексом, а прозрачным цветом (`colorkey`).

**LIKO‑12** — `src/OS/DiskOS/Libraries/map.lua`, `Map:draw`:
```lua
self:map(function(spx,spy,sprid)
  if sprid < 1 then return end
  spx, spy = spx-x, spy-y;
  (self.sheet or sheet):draw(sprid, dx + spx*8*sx, dy + spy*8*sy, 0, sx, sy)
end, mapX, mapY, w, h)
```
Плюс в `src/OS/DiskOS/Libraries/spritesheet.lua` id спрайтов **начинаются с 1**, а нулевой квад — вырожденный:
```lua
ss.quads[0] = quad(0,0,0,0,0,0) --Null quad, used by the map object for spritebatch mode.
```
То есть в spritebatch-режиме тайл 0 рисуется квадом нулевого размера — тоже ничего.

**PICO‑8** — мануал, `MAP([CELL_X], CELL_Y, [SX, SY], [CELL_W, CELL_H], [LAYERS])`:
> «Sprite 0 is taken to mean "empty" and is not drawn. To disable this behaviour, use:
> `POKE(0x5F36, 0x8)`»

То же дословно повторено для `TLINE()`. В changelog: «Added: set bit POKE(0x5F36, 0x8) to treat sprite 0 as opaque when drawn by map(), tline()».

### Редактор: показывает ли палитра тайлов спрайт 0 его настоящим рисунком и даёт ли им рисовать?

| Консоль | Показывает рисунок? | Можно рисовать? | Как выглядит на карте |
|---|---|---|---|
| **TIC‑80** | **ДА** — настоящий рисунок | **ДА**, более того это значение **по умолчанию** | как обычный тайл, с картинкой |
| **LIKO‑12** | **НЕТ** — иконка «пусто» | **ДА** (это и есть ластик) | ничего не рисуется |
| **PICO‑8** | **ДА** (навигатор = сам лист спрайтов) | **ДА** | пусто/прозрачно |

**TIC‑80.** Палитра тайлов рисуется целиком, начиная с индекса 0 — `src/studio/editors/map.c`, `drawSheetReg`:
```c
tic->ram->vram.blit.segment = tic_blit_calc_segment(&blit);
tic_api_spr(tic, 0, pos.x, pos.y + map->anim.pos.sheet, TIC_SPRITESHEET_COLS, TIC_SPRITESHEET_COLS,
            NULL, 0, 1, tic_no_flip, tic_no_rotate);
```
`colors = NULL, count = 0` → прозрачных цветов нет, спрайт 0 виден как есть. Начальное выделение — именно нулевой тайл (`initMap`): `.sheet = { .rect = {0, 0, 1, 1}, ... }`. И на самой карте он рисуется реальными пикселями (`drawMapReg`):
```c
tic_api_map(tic, map->scroll.x / TIC_SPRITESIZE, map->scroll.y / TIC_SPRITESIZE,
    TIC_MAP_SCREEN_WIDTH + 1, TIC_MAP_SCREEN_HEIGHT + 1, -scrollX, -scrollY, 0, 0, 1, NULL, NULL);
```
(7-й и 8-й аргументы — `colors=0, count=0`.)

**LIKO‑12.** В `tile.lua` палитра строится так:
```lua
local SpritesMap = MapObj(22,12)
SpritesMap:map(function(x,y)
  local tid = y*22+x
  if tid > 255 then tid = 0 end
  return tid
end)
```
и рисуется в `t:drawMenu`:
```lua
SpritesMap:draw(sdx+2,sdy+2,false,false,false,false,false,false,SpriteMap)
_SystemSheet:draw(120,sdx+2,sdy+2)   -- поверх ячейки 0 — системная иконка "пусто"
```
Так как `SpritesMap:draw` — это тот же `Map:draw` с `if sprid < 1 then return end`, ячейка 0 остаётся пустой, и на неё специально кладётся иконка 120. Та же иконка используется для пустых слотов хотбара:
```lua
if sprid == 0 then _SystemSheet:draw(120, swidth-8,8+i*8) else SpriteMap:draw(sprid, swidth-8,8+i*8) end
```
Выбрать тайл 0 можно (`if tid <= 255 then hotbarTiles[selectedSlot] = tid end`), и он рисуется на карте как «стереть».

**PICO‑8.** Навигатор спрайтов в map-режиме — тот же лист спрайтов, спрайт 0 показан своими пикселями и выбирается. Но на карте он рисуется пустым. С BBS (тред `tid=3375`), ответ jayminer:
> «Sprite 000 is a special case and is always blank/transparent. I usually draw an x in it to remind myself not to use it.»

И из changelog мануала — редактор специально отделяет тайл 0 от чёрных тайлов:
> «Added: In map editor, non-zero cels that are drawn all black are marked with a single blue dot»

**Вывод для Quarp.** Три референса делятся 2:1 не в пользу «тайл 0 = пусто». Рекомендуемая схема (совмещает обе): хранить тайл 0 как обычный тайл (как TIC‑80), а «пусто» задавать прозрачным цветом; при этом в редакторе карт держать переключатель «трактовать тайл 0 как пустой» — это ровно то, что PICO‑8 отдал через `POKE(0x5F36,0x8)`.

## 7.2 Прозрачность

| Консоль | Спрайт | Карта | Настраивается? |
|---|---|---|---|
| **TIC‑80** | **нет прозрачного цвета по умолчанию** | **нет по умолчанию** | Да, произвольный **список** цветов, аргумент функции |
| **LIKO‑12** | **цвет 0 (чёрный)** | цвет 0 | Да, глобальное состояние `palt()` |
| **PICO‑8** | **цвет 0** | цвет 0 | Да, глобальное `PALT()`, битовая маска на 16 цветов |

**TIC‑80** — `src/core/draw.c`:
```c
#define TRANSPARENT_COLOR 255

static u8* getPalette(tic_mem* tic, u8* colors, u8 count)
{
    static u8 mapping[TIC_PALETTE_SIZE];
    for (s32 i = 0; i < TIC_PALETTE_SIZE; i++) mapping[i] = tic_tool_peek4(tic->ram->vram.mapping, i);
    for (s32 i = 0; i < count; i++)
        if (colors[i] < TIC_PALETTE_SIZE) mapping[colors[i]] = TRANSPARENT_COLOR;
    return mapping;
}
```
Прозрачность — **список цветов, передаваемый в каждый вызов**, не глобальное состояние. В биндинге (`src/api/luaapi.c`, `lua_spr` и `lua_map`) счётчик инициализируется нулём:
```c
static u8 colors[TIC_PALETTE_SIZE];
s32 count = 0;
```
и остаётся нулём, если 4-й (для `spr`) / 7-й (для `map`) аргумент не передан. Аргумент может быть **числом или таблицей** цветов. **⇒ по умолчанию у TIC‑80 прозрачных цветов нет вообще.**

**LIKO‑12** — `src/Peripherals/GPU/modules/palette.lua`:
```lua
for i=1,16 do
  _ImageTransparent[i] = (i==1 and 0 or 1) --Black is transparent by default.
  ...
end
```
`GPU.palt(c,t)` меняет один цвет, `palt()` без аргументов сбрасывает к дефолту (0 — прозрачный, остальные — нет). Применяется ко всем `image:draw`, то есть и к спрайтам, и к карте.

Отдельно: **в редакторе карт LIKO‑12 карта рендерится с непрозрачным чёрным** — `tile.lua`, `t:drawMap`:
```lua
palt(0,false)
line(...) line(...)
Map:draw(math.floor(mapdx%8-8), math.floor(mapdy%8), ..., SpriteMap)
palt()
```

**PICO‑8** — мануал:
> «`PALT(C, [T])` — Set transparency for colour index to T (boolean) Transparency is observed by SPR(), SSPR(), MAP() AND TLINE()»
> «**PALT() resets to default: all colours opaque except colour 0**»
> «When C is the only parameter, it is treated as a bitfield used to set all 16 values.»

и в описаниях `SPR()` / `SSPR()`: «Colour 0 drawn as transparent by default (see PALT())».

## 7.3 Ластик на карте — отдельный инструмент или «поставить тайл 0»?

| Консоль | Ответ |
|---|---|
| **TIC‑80** | **Отдельного ластика НЕТ.** Есть `Delete` = обнулить выделение |
| **LIKO‑12** | **Отдельного ластика НЕТ**, но **ПКМ = поставить тайл 0** |
| **PICO‑8** | **Отдельного ластика НЕТ**; стирают спрайтом 0 или `DEL`/`BACKSPACE` по выделению |

**TIC‑80.** В `map.c` есть ровно 4 инструмента (`MAP_DRAW_MODE`, `MAP_DRAG_MODE`, `MAP_SELECT_MODE`, `MAP_FILL_MODE`) — иконки `tic_icon_pen`, `tic_icon_hand`, `tic_icon_select`, `tic_icon_fill`. Ластика нет. Обнуление — только по клавише `Delete` над выделением:
```c
static void deleteSelection(Map* map)
{
    tic_rect* sel = &map->select.rect;
    if(sel->w > 0 && sel->h > 0)
    {
        for(s32 j = sel->y; j < sel->y+sel->h; j++)
            for(s32 i = sel->x; i < sel->x+sel->w; i++)
            {
                s32 x = i, y = j; normalizeMapRect(&x, &y);
                map->src->data[x + y * TIC_MAP_WIDTH] = 0;
            }
        history_add(map->history);
    }
}
```
Точечно «стереть» = выбрать тайл 0 в палитре (СКМ по пустому месту карты как раз возвращает 0 в палитру) и рисовать им.

**LIKO‑12.** `tile.lua`, `t:mapmouse` — ластик встроен в правую кнопку:
```lua
local selectedTile = hotbarTiles[selectedSlot]
if isMDown(2) or (b and b == 2) then selectedTile = 0 end
```
Работает и для карандаша, и для заливки. Отдельной кнопки-ластика в панели инструментов нет (там Pencil / Bucket / Hand / Select / Menu).

**PICO‑8.** В map-режиме инструменты те же, что в спрайтовом («The tools are similar to the ones used in sprite editing mode»); ластика среди них нет. Есть:
> «Added: DEL / backspace to clear selected region in gfx / map editors, and ctrl-x to cut»

## 7.4 Флаги спрайтов на карте

| Консоль | Показ флагов выбранного тайла в редакторе карт | Оверлей флагов поверх карты |
|---|---|---|
| **TIC‑80** | **НЕТ** | **НЕТ** |
| **LIKO‑12** | **НЕТ** | **НЕТ** |
| **PICO‑8** | **НЕТ** в редакторе; но флаги — механизм слоёв в рантайме | **НЕТ** |

**TIC‑80.** В `map.c` нет ни одного обращения к `getBankFlags` / `tic_api_fget`; `drawFlags` определён только в `sprite.c` и вызывается только оттуда:
```c
// sprite.c, tick():
if(sprite->advanced)
{
    if(is4bpp(sprite)) drawFlags(sprite, 24+64+7, 20+8);
    drawBitMode(sprite, PaletteX, PaletteY + PaletteH + 2, PaletteW, 8);
}
```
В `drawMapToolbar` — только Sheet/Bank/Bpp/Pages/Fill/Select/Hand/Pen/Grid/World. Ни индикатора флагов, ни режима их подсветки нет.

**LIKO‑12.** `tile.lua` вообще не читает `flagsData` (флаги живут в `sprite.lua`: `se:getFlags()`), и `t:drawToolbar` / `t:drawMenu` их не отображают.

**PICO‑8.** Мануал описывает флаги только в разделе Sprite Editor:
> «The 8 coloured circles are sprite flags for the current sprite.»

а в описании `FSET()`:
> «The initial state of flags 0..7 are settable in the sprite editor, so can be used to create custom sprite attributes. **It is also possible to draw only a subset of map tiles by providing a mask in MAP().**»
и в `MAP()`:
> «LAYERS is a bitfield. When given, only sprites with matching sprite flags are drawn. For example, when LAYERS is 0x5, only sprites with flag 0 and 2 are drawn.»

То есть флаги = слои карты в рантайме, но редактор карт их не визуализирует ни у выбранного тайла, ни поверх карты.

**Вывод для Quarp.** Здесь у всех трёх — дыра. Показ флагов выбранного тайла в редакторе карт и переключаемый оверлей (например, «подсветить все тайлы с флагом N») — это то, чего у референсов нет, и это дешёвое улучшение, а не отклонение от «того же уровня».

---

# 8. Чего нам точно не хватает

Отсортировано по важности. Всё перечисленное есть **во всех трёх** референсах (или явно в двух из трёх при отсутствии у третьего самого редактора).

| # | Что | TIC‑80 | LIKO‑12 | PICO‑8 | Почему критично |
|---|---|---|---|---|---|
| 1 | **Единая система undo/redo во всех редакторах** с `Ctrl+Z`/`Ctrl+Y` | `src/ext/history.c`, XOR‑дифф всего региона | только в code и sfx | во всех | Без этого редакторы непригодны для работы. Проще всего повторить схему TIC‑80: одна `History` на каждый ресурс (лист спрайтов, карта, SFX, музыка, код), шаг = снимок/дифф, добавляемый после каждого изменяющего действия |
| 2 | **Системный буфер обмена + межредакторный обмен** (`Ctrl+X/C/V`) | hex-строка через `toClipboard`/`fromClipboard` | текстовый clipboard, **с импортом формата PICO‑8** | текстовый | Позволяет копировать спрайты/куски карты/SFX между картриджами и между людьми. Формат — короткий hex-текст, без бинаря |
| 3 | **Мультивыбор тайлов в палитре и «штамп» блоком N×M на карте** | `map->sheet.rect` протяжкой, любой размер | нет (только 1 тайл в слоте) | «shift+drag in sprite navigator» | Рисование картой по одному тайлу — главная жалоба на слабые редакторы |
| 4 | **Выделение + копирование/вставка блока карты** | `copySelectionToClipboard`, «плавающая» вставка по клику | **нет** | «use the selection tool and then stamp tool to paste» | То же |
| 5 | **Панорамирование карты ПКМ / пробелом + инструмент «рука»** | ПКМ, `Space`+ЛКМ, режим `MAP_DRAG_MODE` | инструмент Hand + СКМ | pan tool или `hold space` | При карте 256×72 (у нас — 32× ширины экрана) без этого невозможно |
| 6 | **Заливка (flood fill) на карте и в спрайте**, включая «replace» вариант | `fillMap` (BFS), `Ctrl` → `replaceTile`; в спрайте `floodFill` + `Ctrl`→`replaceColor` | `queuedFill` в обоих | Fill tool в обоих | |
| 7 | **Два цвета: ЛКМ / ПКМ**, и пипетка (в т.ч. на СКМ) | `color`/`color2`, СКМ = пипетка | `colsL`/`colsR`, ПКМ или Shift | «RMB to select the colour under the cursor» | |
| 8 | **Редактирование флагов спрайта — 8 переключателей + групповое применение** | 8 кружков + hex‑поле, применяется ко всему выделению | 8 кружков | «8 coloured circles» | У нас 256 спрайтов — флаги нужны с первого дня |
| 9 | **Мини‑карта / world view** для навигации по большой карте | `src/studio/editors/world.c`, `Tab` | нет (карта листается панорамированием) | fullscreen + зум (`TAB`, `<`/`>`) | При 256×72 без обзорного вида ориентироваться нельзя |
| 10 | **Флип/поворот выделения и спрайта** (H, V, 90°) | `5/6/7/8`, отдельно для спрайта и для выделения | `r`/`shift-r`/`f`/`shift-f`/`i` | `F`/`V`/`R` | |
| 11 | **Сетка тайлов с переключением** и координатная плашка у курсора | `drawGrid` + `` ` ``, `%03i:%03i` | линии по краям экрана | `CTRL-G`, hex‑вид `CTRL-H` | |
| 12 | **Размер кисти** в редакторе спрайтов | 4 размера, `-`/`=`, ползунок | 4 размера {1,2,3,5}, ползунок | shape tools + fill | |
| 13 | **Строка статуса в редакторе кода** (`line X/Y col Z`, `size N/MAX`) с красной подсветкой лимита | `drawStatus` | `LINE x/y CHAR a/b` | счётчик токенов справа внизу | |
| 14 | **Поиск / переход к строке / список функций (outline)** в редакторе кода | `Ctrl+F/G/B/O` + закладки | `Ctrl+I`/`Ctrl+K`, `Alt+Up/Down` по функциям | `CTRL-F/G/L`, `ALT-UP/DOWN` | |
| 15 | **Подсказки (tooltip) на каждом контроле** + имя редактора в свободном месте панели | `showTooltip`, вся верхняя панель | нет | — | Дешёвый способ сделать плотный 160×90 UI обучаемым |
| 16 | **Клавиши-цифры на инструменты** (`1..4`) и `F1..F5` / `Alt+←→` на переключение редакторов | `F1..F5`, `Alt+1..5`, `Ctrl+PgUp/PgDn` | `Alt+←/→` | `ALT+LEFT/RIGHT` | |
| 17 | **Пианино-раскладка `zsxdcvgbhnjm` / `q2w3er5t6y7ui`** в звуковых редакторах | да | нет (только выбор волны) | да, дословно та же | Стандарт де-факто; отклоняться нельзя |
| 18 | **Loop start/size у SFX** и предпросмотр волны | ◀▶ + hex, маркеры в панели | нет loop’а | «Loop start and end» | |
| 19 | **Трекер + список фреймов/паттернов, mute/solo каналов, follow-режим** | полностью | **отсутствует целиком** | pattern navigator + flow-кнопки | LIKO‑12 здесь не образец; ориентир — TIC‑80/PICO‑8. **С 2026-09-01 выполним** — см. примечание под таблицей |
| 20 | **Переключаемый показ индексов в hex/dec** | клик по номеру спрайта | нет | `CTRL-H` в обоих редакторах | Мелочь, но её ждут |

**Примечание к п. 19 (2026-09-01).** Этот пункт долго был невыполним не по вине редактора,
а по вине формата: прежний `music.bin` не хранил ноту — ячейка канала была номером SFX-слота
плюс бит «активен» (AUDIO-FORMAT §4), поэтому «трекер» на нём можно было построить только
как навигатор паттернов уровня PICO-8, и ROADMAP M9 так и записывал — «невыполним навсегда».
**Стена снята трекерной раскладкой музыки (ADR-040, AUDIO-FORMAT §4):** строка × канал теперь
хранит ноту, инструмент, громкость и эффект с параметром, у песни есть таблица инструментов
и отдельный порядок, а у паттерна — своя длина и своя дробная скорость. Ориентир из этой
строки — TIC-80 — стал достижим целиком, а не наполовину.

Что уже готово к моменту, когда за экран возьмутся: чтение и запись нот в модели, длины и
линейка с учётом дробного темпа, предпрослушка **одной строки** и **одного паттерна**
(`Apu.PreviewRow`, `Apu.PreviewPattern` — ровно «audition the row under the cursor» и «play
frame» из этого пункта), проверка банка перед сохранением и выгрузка в текст. Полный список
«что нужно редактору → чем брать» — AUDIO-FORMAT §10, раздел «Что оставлено трекеру
следующей бригады». Остаётся собственно экран: сетка, курсор по колонкам, пианино-раскладка
(п. 17), mute/solo и follow. Экран паттерн-навигатора (`MusicEditor*`) переписывать не надо —
после ADR-041 он внятно отказывается открывать любой `music.bin`, и трекер удаляет его целиком
вместе с `MusicPatternList.cs`.

**Отдельно — три решения, которые надо принять до начала работ (у референсов они расходятся):**
1. Рисует ли `map()` тайл 0 (TIC‑80 — да, LIKO‑12/PICO‑8 — нет). Рекомендую вариант TIC‑80 + флаг совместимости, как `POKE(0x5F36,0x8)` у PICO‑8.
2. Прозрачный цвет: список цветов в аргументе вызова (TIC‑80) или глобальное состояние `palt` (LIKO‑12/PICO‑8). Второй вариант дешевле и привычнее большинству.
3. Индексация спрайтов с 0 (TIC‑80/PICO‑8) или с 1 (LIKO‑12). У нас 256 спрайтов и 8-битные ячейки карты — берём с 0.

**Источники:** [TIC‑80](https://github.com/nesbox/TIC-80), [LIKO‑12](https://github.com/LIKO-12/LIKO-12), [PICO‑8 manual](https://www.lexaloffle.com/dl/docs/pico-8_manual.txt) (читал через зеркало [THE-ORONCO/pico-8](https://github.com/THE-ORONCO/pico-8/blob/master/pico-8_manual.md)), [BBS-тред про спрайт 0 на карте](https://www.lexaloffle.com/bbs/?tid=3375).