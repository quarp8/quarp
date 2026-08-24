Зона: src/Quarp.Analyzers/Quarp.Analyzers.csproj (ссылки пакета) — код анализатора не трогать
Майка: XS

# Сборка не 0/0: предупреждение MSB3106 в Quarp.Analyzers

Находка организатора на записи состояния «до» перед закрытием этапа 2 M9 (2026-08-24,
Windows, SDK 10.0.100-rc.2): `dotnet build -c Release` даёт 0 ошибок и **1 предупреждение**:

    MSB3106: Assembly strong name "C:\Users\<user>\.nuget\packages\system.numerics.vectors\
    4.5.0\ref\netstandard2.0\System.Numerics.Vectors.dll" is either a path which could not be
    found or it is a full assembly name which is badly formed
    [src/Quarp.Analyzers/Quarp.Analyzers.csproj]

Приказы вехи требуют «сборка 0/0» буквой, и этот пункт приёмки этапа 2 выполнен
с оговоркой, а не чисто. Предупреждение идёт от транзитивной ссылки на
system.numerics.vectors 4.5.0 (тянется пакетами Roslyn в netstandard2.0-проекте
анализатора), а не от нашего кода.

Что выяснить до правки:
1. Красное ли это в CI (ubuntu/windows раннеры со свежим кэшем) или только на машине
   владельца — иначе лечим симптом, которого у CI нет.
2. Откуда ссылка приходит: `dotnet build -c Release /v:n` + `dotnet list package
   --include-transitive` по проекту анализатора.
3. Лечение из известных: явный `PackageReference` на System.Numerics.Vectors нужной
   версии, либо `NoWarn`/`MSBuildTreatWarningsAsMessages` — второе только если первое
   не работает, и с записью причины: заглушить предупреждение проще, чем понять его.

Критерий: `dotnet build -c Release` печатает `0 Warning(s)` и `0 Error(s)` на чистой
машине; в приёмке вехи «0/0» перестаёт быть с оговоркой.

Non-goals: не менять версию Roslyn-пакетов ради косметики; не трогать код анализатора
и его тесты (75 зелёных — якорь поведения, не предмет этой карточки).
