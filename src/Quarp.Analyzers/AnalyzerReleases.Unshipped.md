; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
QRP1001 | Determinism | Error | FloatBanAnalyzer — float, double and decimal are banned in cartridge code (SPEC-8 §7).
QRP1002 | Determinism | Error | NonDeterministicApiAnalyzer — the banned non-deterministic BCL surface (SPEC-8 §7).
QRP1003 | Determinism | Warning | UnorderedIterationAnalyzer — foreach over Dictionary/HashSet has no stable order (SPEC-8 §7).
QRP1004 | Determinism | Error | DrawPurityAnalyzer — a state-mutating console call reached from Draw (SPEC-8 §7 rule 2).
