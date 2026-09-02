Зона: src/Quarp.Cli (Program.cs, новая CartImageCommands.cs)
Майка: S

# CLI: `quarp pack --png`, `quarp image unpack`, `quarp run` по картинке

[CART-IMAGE-FORMAT.md](../../docs/CART-IMAGE-FORMAT.md) §10. Три входа, ни одного нового
владельца факта «как пакуется картридж»:

- `quarp pack <folder> [--png] [-o <file>]` — флаг, а не новая команда: упаковывает
  по-прежнему `Quarp8Package.Pack`, картинка обёртывает его результат. Без `-o` пишет
  `<folder>.quarp8.png`.
- `quarp image unpack <file.png> [-o <file.quarp8>] [--check]` — обратная операция;
  печатает sha256 вынутого пакета и идентичность картриджа. `--check` проверяет и печатает,
  ничего не записывая — та же конвенция, что у `quarp audio build --check` и
  `quarp map build --check`.
- `quarp run <file.png>` — третий путь запуска рядом с папкой и `.quarp8`; `sim`, `shot`,
  `bench` получают его тем же изменением, потому что путь разбирается в одном месте.
- Блок `usage` в `Program.cs` пополняется тремя строками — он единственный владелец
  справки.

Отказы — теми фразами, что выписаны в §5 спецификации: не PNG; не та геометрия («её
отресайзили»); нет маркера `QIMG` («это картинка, а не картридж»); версия новее; CRC
не сошёлся («пересжали»); пакет больше ёмкости носителя — с обоими числами и с тем,
сколько из них занимают банки.

Критерий: на чистой машине `quarp pack carts/snake --png && quarp run carts/snake.quarp8.png`
открывает змейку; `quarp image unpack --check` на том же файле печатает sha256, равный
sha256 от `quarp pack carts/snake`; `quarp run docs/assets/test-pattern.png` печатает фразу
про отсутствие данных картриджа и возвращает 1.

Non-goals: `quarp image pack` не заводить (второе имя для того же действия); формат
командной строки существующих команд не менять.
