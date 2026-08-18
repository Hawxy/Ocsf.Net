using Microsoft.CodeAnalysis;

namespace Ocsf.Analyzers;

internal static class Diagnostics
{
    private const string Category = "Ocsf.Usage";

    public static readonly DiagnosticDescriptor RequiredAttributeMissing = new(
        "OCSF001",
        "Required OCSF attribute is not populated",
        "Required OCSF attribute '{0}' ({1}) is not set on {2}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The OCSF schema marks this attribute as required. Events missing required "
            + "attributes fail validation. Populate it in the object initializer, a later "
            + "assignment, or a Set* helper.");

    public static readonly DiagnosticDescriptor OtherRequiresLabel = new(
        "OCSF002",
        "Enum set to Other (99) requires an explicit sibling label",
        "'{0}' is set to Other (99) but no source-specific label is assigned to '{1}'",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The OCSF spec requires the sibling label attribute to carry the "
            + "source-specific value when an enum attribute is Other (99). Pass the label to "
            + "the Set* helper or assign the sibling property.");

    public static readonly DiagnosticDescriptor UseSetActivity = new(
        "OCSF003",
        "Assign activity via SetActivity to keep type_uid consistent",
        "ActivityId is assigned directly; type_uid will not match class_uid * 100 + activity_id — use SetActivity",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "type_uid must equal class_uid * 100 + activity_id. SetActivity assigns "
            + "the activity and recomputes type_uid and type_name in one call.");

    public static readonly DiagnosticDescriptor ConstraintUnsatisfied = new(
        "OCSF004",
        "OCSF constraint is not satisfied",
        "{0} requires {1} of [{2}]{3}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "OCSF classes and objects declare at_least_one/just_one constraints over "
            + "attribute sets. Events violating them fail validation.");
}
