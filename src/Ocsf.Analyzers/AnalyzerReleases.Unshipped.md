; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
OCSF001 | Ocsf.Usage | Warning | Required OCSF attribute is not populated
OCSF002 | Ocsf.Usage | Warning | Enum set to Other (99) requires an explicit sibling label
OCSF003 | Ocsf.Usage | Info | Assign activity via SetActivity to keep type_uid consistent
OCSF004 | Ocsf.Usage | Warning | OCSF constraint is not satisfied
