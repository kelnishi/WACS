; Unshipped analyzer releases.
; Format: https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------------------------------------
WACS001 | Wacs.HostBindings | Warning | No host binding for wasm import
WACS002 | Wacs.HostBindings | Error | Host binding signature mismatch
WACS003 | Wacs.HostBindings | Error | Ambiguous host bindings for wasm import
WACS004 | Wacs.HostBindings | Disabled | Host binding matched (disabled by default)
