# M1 — рабочий приказ (внутренний документ оркестрации)

Критерий вехи (ROADMAP): «змейка» лежит в `carts/`, правка её кода в VS Code
подхватывается на лету, исключение в коде картриджа показывает номер строки.

Контракт зафиксирован организатором: `src/Quarp.Api/IConsoleApi.cs` и `Cartridge.cs` —
**не менять** без крайней необходимости; всё остальное строится вокруг них.

## Разделение файлов (строгое — никто не трогает чужие)

| Бригада | Владеет | Не трогает |
|---|---|---|
| A core-graphics | `src/Quarp.Core/**` (новые файлы; можно дополнять Framebuffer), `src/Quarp.Api/SMath.cs` (новый) | CartKit, Shell, Cli, carts, tests |
| B cartkit | `src/Quarp.CartKit/**` | Core, Api, Shell, Cli, carts |
| C snake | `carts/snake/**` | всё остальное |
| D docs | `docs/API-8.md` (новый) | код |
| Integrator | `src/Quarp.Shell.Desktop/**`, `src/Quarp.Cli/**`, правка любых компиляционных ошибок | — |
| Test-писатель | `tests/**` | продакшн-код (кроме багов, найденных тестами — фикс через минимальный точечный патч с пометкой) |

**Правило сборки: бригады фазы Implement НЕ запускают dotnet вообще** (параллельные
сборки дерутся за obj/). Компилирует и чинит Integrator. Тест-фаза запускает
`dotnet test` свободно.

## Решения организатора (не пересматривать, вопросы — в отчёт)

- API: `Print` возвращает конечный X (да), `Sget`/`Sset` — да, `Oval` — нет в v1,
  `fillp` — нет в v1, `Sspr` — нет в профиле 8 (SPEC-8 §3). Всё это фиксирует API-8.md.
- `SMath` (в Quarp.Api, статический, только Fix/int): `Sin`/`Cos` — вход Q16-обороты
  (Fix, 1.0 = круг), таблица 1024 узла на четверть + лерп, выход Fix (ADR-014);
  `Sqrt(Fix)` — целочисленный Ньютон/бинарный на ulong ((raw)<<16); `Atan2(Fix y, Fix x)`
  — выход в оборотах, октанты + таблица atan 256 узлов с лерпом, точность ~1e-3 оборота.
- RNG: xoshiro128**, сид через splitmix64, состояние в VirtualConsole (часть симуляции).
- Системный шрифт: 4×6 (глиф 3×5 + 1px зазор), ASCII 32–126, **нарисовать свой**
  (никаких заимствованных шрифтов — нулевые лицензионные вопросы), данные — массив в Core.
- Ввод: `InputState` (struct, Core): по byte бит-маски на игрока (бит = (int)Button),
  2 игрока. Btnp = нажато в этом тике и не нажато в прошлом.
- Никакого float/double в Core/Api/carts (CODESTYLE); в тик-пути — ноль аллокаций.

## Форматы (Формат-спека v1; B реализует, C следует)

- Папка картриджа: `manifest.json` (`{"name","author","profile":8}`), `src/*.cs`,
  опционально `gfx.png` (ровно 128×128), `map.bin` (256×72 байт построчно),
  `flags.bin` (256 байт), `cover.png`. Отсутствующие ассеты = нули.
- `gfx.png`: собственный мини-декодер PNG (System.IO.Compression.ZLibStream — zlib
  в BCL есть; чанки IHDR/PLTE/tRNS/IDAT/IEND; типы цвета 3 (indexed), 2 (RGB), 6 (RGBA),
  8-бит; фильтры 0–4). Каждый непрозрачный пиксель обязан ТОЧНО совпасть с одним из 16
  видимых цветов `Palette.Master32[0..15]`, иначе ошибка с координатами (SPEC-8 §6);
  альфа 0 → индекс 0.
- `.quarp8` = zip папки (System.IO.Compression.ZipArchive), запись — `quarp pack`.
- Лимиты при pack и при загрузке: код ≤ 65536 байт UTF-8 **после удаления comment
  trivia** (Roslyn SyntaxTree, нормализация \r\n→\n); файл `.quarp8` ≤ 131072 байт;
  gfx строго 128×128.
- Сохранения: `<папка>/save.dat` или рядом с `.quarp8` — 64 × int32 LE (raw Fix).
  Пишет Shell при выходе и раз в секунду, если менялось.

## Конвейер картриджа (B)

Roslyn: `CSharpCompilation` Release, `AllowUnsafe=false`, deterministic, embedded
portable PDB. Ссылки: Quarp.Api + минимум BCL из TRUSTED_PLATFORM_ASSEMBLIES
(System.Runtime, System.Private.CoreLib, System.Collections, netstandard).
Два фильтра ПОСЛЕ компиляции:
1. **Синтаксический**: float/double/decimal-литералы и типы в исходниках картриджа —
   ошибка с файлом/строкой (честный бан из SPEC-8 §7; полноценный анализатор — M2).
2. **Метаданные** (System.Reflection.Metadata): скан TypeRef/MemberRef собранной DLL;
   бан: System.IO.*, System.Net.*, System.Threading.*, System.Reflection.* (кроме
   атрибутов), System.Runtime.InteropServices.*, System.Random, System.DateTime*,
   System.Guid, System.Environment, System.Math, System.MathF, System.Console,
   System.AppDomain, System.Activator, System.GC. Ошибка со списком мест.
Загрузка: collectible AssemblyLoadContext; ровно один наследник Cartridge (0 или >1 —
ошибка). Хот-релоад: FileSystemWatcher на папку (src/, gfx.png, map.bin, flags.bin),
debounce 150 мс, потокобезопасный флаг «нужен перезапуск»; пересборку выполняет
вызывающая сторона (Shell) в главном потоке. Прогревочная компиляция при старте.
Кэш MetadataReference — статический.

## Ядро (A)

`VirtualConsole : IConsoleApi`: framebuffer (уже есть), лист спрайтов
byte[128×128] (индексы 0–15), map byte[256×72], flags byte[256], camera, clip,
palMap byte[16] (слот→мастер-индекс 0–31), palt bool[16] (по умолчанию только цвет 0
прозрачный), rng, input текущий/прошлый, tickCount, persistent int[64] + флаг Dirty.
`AttachCart(Cartridge)` → Attach + Init (тик 0). `Tick(InputState)` → Update() затем
Draw(). Никаких исключений не глотать — они летят наружу (Shell покажет).
Растеризатор: клиппинг по Clip-прямоугольнику ПОСЛЕ camera-смещения; Spr уважает
palMap+palt; Map рисует тайлы через тот же путь, flagFilter≠0 → только тайлы,
у которых есть все биты фильтра. Line — Брезенхэм, Circ — midpoint. Всё int.

## Shell + CLI (Integrator)

- Shell: два режима — паттерн (без аргументов) и картридж (путь к папке/.quarp8).
  MonoGame fixed step 60 Гц (строгий аккумулятор — M2). Ввод: P0 = стрелки, Z (O),
  X (X), Enter (Start) + геймпад 0 (D-pad, A=O, B=X, Start); P1 = геймпад 1.
  Исключение в Update/Draw картриджа: пауза, стектрейс с номерами строк в терминал,
  баннер «CRASHED — see terminal, edit code to reload» через Print прямо в framebuffer;
  правка файла → пересборка → рестарт. Ошибка компиляции: старый картридж продолжает
  работать, диагностика в терминал.
- CLI: `quarp run [path]`, `quarp pack <folder> [-o file]`, `quarp new <folder>`
  (шаблон: manifest + main.cs с заготовкой), `quarp sim <path> --ticks N` —
  headless-прогон без MonoGame, в конце FNV-1a-хэш framebuffer в stdout (для тестов
  и будущего CI детерминизма), `quarp pattern <file>`.

## Змейка (C) — carts/snake/

Поле 16×9 тайлов по 8px; ряд 0 — HUD (Print счёта), игра в рядах 1–8.
Змейка стартует длиной 3 в центре, шаг каждые 8 тиков, ускорение на каждое яблоко
до минимума 3; повороты через Btnp с очередью на 1 ход; яблоко через RndInt по
свободным клеткам; смерть о стену/себя → экран Game Over, Start — заново (свой сброс
состояния, не перезапуск консоли). Рисование примитивами (RectFill/Print), gfx.png
не нужен. Цвета: фон 0, змейка 7/23, яблоко 10, HUD 3. Код < 64 КБ с запасом,
без float, только API из Cartridge.

## API-8.md (D)

Полный справочник поверхности из IConsoleApi.cs: сигнатура, семантика, краевые случаи
(клиппинг, отрицательные размеры, выход за границы — везде «мягко обрезаем, не кидаем»),
пример на каждый вызов. Раздел «Рассмотрено и решено» с решениями организатора выше
+ Sspr/фракционные константы Fix (Ratio/Half; переполнение = wrap, деление на ноль =
исключение — черновик до ратификации). Статус: черновик, ратификация в M4 (ADR-012).

## Приёмка (Integrator, затем тесты)

1. `dotnet build` — 0 ошибок/предупреждений; `dotnet test` — все зелёные.
2. `quarp sim carts/snake --ticks 600` — работает, хэш печатается, два запуска подряд
   дают одинаковый хэш.
3. `quarp run carts/snake` — окно, змейка играется.
4. Порча кода змейки (синтаксическая ошибка) при работающем watch — диагностика,
   старый картридж жив; исправление — рестарт с новым кодом.
