Зона: src/Quarp.Shell.Desktop (редактор спрайтов) — после приёмки v1
Майка: M (суммарно; каждый пункт отдельно XS–S)

# Редактор спрайтов v2 — отложенное с данными разведки

Разведка ниши (2026-08-19, отчёт scout по первоисточникам PICO-8/TIC-80/LIKO-12,
копия в скретчпаде сессии: scout-sprite-editor-v1.md) показала минимум жанра —
он весь взят в v1. Ниже — то, что есть не у всех или не нужно для критерия
приёмки «владелец рисует спрайт», отложено с записью:

- **ВЫЧЕРКНУТО (2026-08-19, вердикт владельца этапа 2.5, волна 2f, коммит
  `88d6743`).** выделение + перемещение (PICO-8, TIC-80; LIKO-12 жил без него) — S.
  Доказательство: `SelectionVariant` (Rectangle/Brush, wand добавлен волной 2g)
  в SpriteEditorSession.cs — «a press over an existing selection grabs and moves
  regardless of variant»; тесты SpriteEditorSelectionTests.cs
  (`AMoveIsOneUndoStepAndLeavesZeroBehind`, `AMoveClampsAtTheRegionBorderAndLosesNothing`,
  `UndoMidMoveCancelsTheFloatThenUndoesThePixels`). M9-WORKORDER.md: «выделение
  (прямоугольное и кисточкой) ... теперь в составе этапа».
- **ВЫЧЕРКНУТО частично (2026-08-19, волны 2f/2g, коммиты `88d6743`+`b4cfc90`).**
  копипаст спрайтов — ядро («захватить пиксели → разместить в другом месте
  листа») закрыто инструментом Stamp: `SpriteEditorTool.Stamp`,
  `CaptureStampSource`/`StampAt` в SpriteEditorSession.cs («Stamp source ...
  the captured selection's bounding box»); тест
  SpriteEditorStampTests.cs:`AStampPrintsTheCapturedPixelsCenteredAtTheCursor`
  (+ баг «штамп не выбирается мышью» найден и закрыт волной 2g, коммит
  `b4cfc90`). M9-WORKORDER.md называет это «штамп» прямо: «выделение
  ..., штамп, фигуры (овал/прямоугольник) — теперь в составе этапа
  (перекрывает отложенное v2)». ОСТАЁТСЯ ОТКРЫТЫМ: интероп с чужими
  буферами (LIKO-12 читает PICO-8-формат `[gfx]`) — в коде нет ни чтения,
  ни записи внешнего буфера обмена, это отдельный вопрос, как и было
  записано.
- фигуры: линия/прямоугольник/овал (только PICO-8; у TIC-80 просят годами —
  issues #1593/#1967/#1118) — S.
  **ВЫЧЕРКНУТО частично (2026-08-19, волна 2e, коммит `093a1d3`):**
  прямоугольник и овал сделаны — `ShapeVariant.Oval`/`Rectangle` в
  SpriteEditorSession.cs; тесты SpriteEditorShapeTests.cs
  (`AnOutlineOvalLeavesItsInteriorEmpty`, `CtrlCommitsAFilledOval`).
  M9-WORKORDER.md: «фигуры (овал/прямоугольник) — теперь в составе этапа»
  — линия НЕ упомянута вердиктом и инструмента «линия» в редакторе нет: в `ShapeVariant` только Oval и Rectangle, и ни одна кнопка тулбара её не даёт. (Поправка аудита 2026-08-24: прежняя формулировка ссылалась на «grep по Line — ничего кроме шрифта», а это неправда — `TraceLine` в SpriteEditorSession.cs существует и рисует брезенхэмом промежутки мазка. Вывод карточки верен, доказательство было ложным; сам `TraceLine` — готовый кирпич для будущего инструмента.)
- размер кисти 1–4 (TIC-80, LIKO-12; PICO-8 живёт без) — XS. Проверено:
  в коде нет `BrushSize`/переменного размера кисти — открыто.
- pan (только PICO-8) — XS. Проверено: нет `Pan`/прокрутки холста в
  SpriteEditorSession.cs — открыто.
- предпросмотр парных оттенков: view-режим Pal(i, i+16) без записи в файл
  (наша уникальная фича, ниша такого не имеет; в лист пишутся только 0–15,
  SPEC-8 §6 нерушима) — S. Проверено: нет `Pal(i, i+16)`/парного
  view-режима в коде — открыто.

Решать по спросу после того, как владелец порисует в v1; не раньше.

Non-goals: писать в gfx.png что-либо кроме слотов 0–15 нельзя никогда (железо).
