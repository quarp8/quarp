Зона: src/** + tests/Quarp.Core.Tests/TestPatternTests.cs (волна 1а этапа 4.0)
Майка: M

# Переезд ядра: Profile8 = 160×90, спайк под нож, TestPattern от размеров

По карте оракула 4.0: ConsoleProfile.cs:11-16 (160×90), Profile8Wide удалить (Р21);
--profile и help из Program.cs:55-56,289-291; TestPattern.cs:18-46 переверстать от
fb.Width/Height (Р25) и тестам зубы (fb.Width-1, не [127]); окно оболочки ×8 (Р24,
QuarpGame.cs:92-93); crash-баннер CartSession.cs:1007-1009 от ширины; док-комменты
Core/Api (SystemFont*, Font.cs, Cartridge.cs, IConsoleApi.cs).

Критерии: quarp pattern рисует паттерн во весь 160×90 без чёрной Г-полосы (тест);
негативный контроль — сломанный правый край красит TestPatternTests; grep
8w|Profile8Wide по src/ пуст. Якоря в этой волне КРАСНЫЕ до пачки 1б-1г — законно.

Non-goals: carts/**, docs/**, .github/**, чужие тесты; ScreenSize*-тесты — зона 1в.
