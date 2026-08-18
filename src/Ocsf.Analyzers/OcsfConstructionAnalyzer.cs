using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Ocsf.Analyzers;

/// <summary>
/// Analyzes construction of generated OCSF event classes and objects: required attributes,
/// the Other (99) sibling-label rule, SetActivity usage, and at_least_one/just_one constraints.
/// Analysis is strictly intra-method; instances that escape the method suppress the
/// whole-instance diagnostics rather than guessing.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OcsfConstructionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Diagnostics.RequiredAttributeMissing,
            Diagnostics.OtherRequiresLabel,
            Diagnostics.UseSetActivity,
            Diagnostics.ConstraintUnsatisfied);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext =>
        {
            var knownTypes = KnownTypes.TryCreate(startContext.Compilation);
            if (knownTypes is null)
                return;

            startContext.RegisterOperationBlockAction(blockContext => AnalyzeBlock(blockContext, knownTypes));
        });
    }

    private sealed class KnownTypes
    {
        public INamedTypeSymbol EventClassAttribute = null!;
        public INamedTypeSymbol ObjectAttribute = null!;
        public INamedTypeSymbol RequirementAttribute = null!;
        public INamedTypeSymbol? SiblingAttribute;
        public INamedTypeSymbol? ConstraintAttribute;
        public INamedTypeSymbol? JsonPropertyNameAttribute;

        public static KnownTypes? TryCreate(Compilation compilation)
        {
            var eventClass = compilation.GetTypeByMetadataName("Ocsf.OcsfEventClassAttribute");
            var obj = compilation.GetTypeByMetadataName("Ocsf.OcsfObjectAttribute");
            var requirement = compilation.GetTypeByMetadataName("Ocsf.OcsfRequirementAttribute");
            if (eventClass is null || obj is null || requirement is null)
                return null;

            return new KnownTypes
            {
                EventClassAttribute = eventClass,
                ObjectAttribute = obj,
                RequirementAttribute = requirement,
                SiblingAttribute = compilation.GetTypeByMetadataName("Ocsf.OcsfSiblingAttribute"),
                ConstraintAttribute = compilation.GetTypeByMetadataName("Ocsf.OcsfConstraintAttribute"),
                JsonPropertyNameAttribute = compilation.GetTypeByMetadataName(
                    "System.Text.Json.Serialization.JsonPropertyNameAttribute"),
            };
        }

        public bool IsOcsfType(ITypeSymbol? type) =>
            type is INamedTypeSymbol named && named.GetAttributes().Any(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, EventClassAttribute)
                || SymbolEqualityComparer.Default.Equals(a.AttributeClass, ObjectAttribute));

        public bool IsEventClass(ITypeSymbol? type) =>
            type is INamedTypeSymbol named && named.GetAttributes().Any(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, EventClassAttribute));
    }

    private sealed class Tracked
    {
        public Tracked(IObjectCreationOperation creation) => Creation = creation;

        public IObjectCreationOperation Creation { get; }

        public ILocalSymbol? Local;
        public bool Escaped;
        public readonly HashSet<string> Assigned = new(StringComparer.Ordinal);
        public readonly HashSet<string> ExplicitLabels = new(StringComparer.Ordinal);
        public readonly List<(IPropertySymbol Property, Location Location)> OtherAssignments = new();
        public readonly List<Location> DirectActivityAssignments = new();
    }

    private static void AnalyzeBlock(OperationBlockAnalysisContext context, KnownTypes known)
    {
        List<Tracked>? tracked = null;
        Dictionary<ILocalSymbol, Tracked>? byLocal = null;

        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                if (operation is IObjectCreationOperation creation && known.IsOcsfType(creation.Type))
                {
                    var item = new Tracked(creation);
                    ClassifyCreationBinding(item, known);
                    (tracked ??= new List<Tracked>()).Add(item);
                    if (item.Local is not null)
                    {
                        byLocal ??= new Dictionary<ILocalSymbol, Tracked>(SymbolEqualityComparer.Default);
                        byLocal[item.Local] = item;
                    }
                }
            }
        }

        if (tracked is null)
            return;

        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                switch (operation)
                {
                    case ISimpleAssignmentOperation assignment
                        when assignment.Target is IPropertyReferenceOperation propertyRef:
                        RecordPropertyAssignment(assignment, propertyRef, tracked, byLocal, known);
                        break;

                    case IInvocationOperation invocation:
                        RecordInvocation(invocation, byLocal, known);
                        break;

                    case ILocalReferenceOperation localRef
                        when byLocal is not null && byLocal.TryGetValue(localRef.Local, out var item):
                        if (IsEscape(localRef))
                            item.Escaped = true;
                        break;
                }
            }
        }

        foreach (var item in tracked)
            Report(context, item, known);
    }

    /// <summary>Determines the local (if any) a creation binds to, and whether the creation
    /// itself escapes: passed directly as an argument or returned.</summary>
    private static void ClassifyCreationBinding(Tracked item, KnownTypes known)
    {
        switch (WalkUpConversions(item.Creation))
        {
            case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator }:
                item.Local = declarator.Symbol;
                break;

            case ISimpleAssignmentOperation { Target: ILocalReferenceOperation localTarget }:
                item.Local = localTarget.Local;
                break;

            case IArgumentOperation argument when !IsTerminalUse(argument):
                item.Escaped = true;
                break;

            case IReturnOperation:
                item.Escaped = true;
                break;

            // Nested in another initializer (Metadata = new Metadata { ... }) or a plain
            // expression statement: analyzable from its own initializer alone.
        }
    }

    private static void RecordPropertyAssignment(
        ISimpleAssignmentOperation assignment,
        IPropertyReferenceOperation propertyRef,
        List<Tracked> tracked,
        Dictionary<ILocalSymbol, Tracked>? byLocal,
        KnownTypes known)
    {
        Tracked? item = null;
        switch (propertyRef.Instance)
        {
            // Object initializer: attach to the nearest enclosing creation.
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver }:
                for (IOperation? ancestor = assignment.Parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    if (ancestor is IObjectCreationOperation creation)
                    {
                        item = tracked.Find(t => t.Creation == creation);
                        break;
                    }
                }
                break;

            case ILocalReferenceOperation localRef when byLocal is not null:
                byLocal.TryGetValue(localRef.Local, out item);
                break;
        }

        if (item is null)
            return;

        var property = propertyRef.Property;
        item.Assigned.Add(property.Name);

        // A direct write to a sibling label property counts as an explicit label.
        item.ExplicitLabels.Add(property.Name);

        // Nullable enum targets wrap the value in a lifted conversion, which is never a
        // constant; the unwrapped operand carries the enum constant.
        if (SkipConversions(assignment.Value) is { ConstantValue: { HasValue: true, Value: not null } constant }
            && IsNinetyNine(constant.Value)
            && GetSiblingName(property, known) is not null)
        {
            item.OtherAssignments.Add((property, assignment.Syntax.GetLocation()));
        }

        if (property.Name == "ActivityId" && known.IsEventClass(property.ContainingType))
            item.DirectActivityAssignments.Add(assignment.Syntax.GetLocation());
    }

    private static void RecordInvocation(
        IInvocationOperation invocation, Dictionary<ILocalSymbol, Tracked>? byLocal, KnownTypes known)
    {
        if (byLocal is null
            || SkipConversions(invocation.Instance) is not ILocalReferenceOperation localRef
            || !byLocal.TryGetValue(localRef.Local, out var item))
        {
            return;
        }

        var method = invocation.TargetMethod;
        if (!method.Name.StartsWith("Set", StringComparison.Ordinal)
            || method.Parameters.Length != 2
            || !known.IsOcsfType(method.ContainingType))
        {
            return;
        }

        // Generated helpers follow SetX(XId, label): SetStatus assigns StatusId and its sibling.
        var enumPropertyName = method.Name.Substring(3) + "Id";
        var enumProperty = FindProperty(method.ContainingType, enumPropertyName);
        if (enumProperty is null)
            return;

        item.Assigned.Add(enumPropertyName);
        var sibling = GetSiblingName(enumProperty, known);
        if (sibling is not null)
            item.Assigned.Add(sibling);

        if (method.Name == "SetActivity")
        {
            item.Assigned.Add("TypeUid");
            item.Assigned.Add("TypeName");
        }

        var labelArgument = invocation.Arguments.FirstOrDefault(a =>
            a.Parameter?.Ordinal == 1 && a.ArgumentKind == ArgumentKind.Explicit);
        var hasExplicitLabel = labelArgument is not null
            && labelArgument.Value.ConstantValue is not { HasValue: true, Value: null };
        if (hasExplicitLabel && sibling is not null)
            item.ExplicitLabels.Add(sibling);

        var enumArgument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0);
        if (enumArgument?.Value.ConstantValue is { HasValue: true, Value: not null } constant
            && IsNinetyNine(constant.Value)
            && sibling is not null)
        {
            item.OtherAssignments.Add((enumProperty, invocation.Syntax.GetLocation()));
        }
    }

    /// <summary>An instance escapes when passed to a non-terminal method, returned, assigned
    /// to something other than its own property, or captured by a lambda/local function.</summary>
    private static bool IsEscape(ILocalReferenceOperation localRef)
    {
        for (IOperation? ancestor = localRef.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return true;
        }

        var parent = WalkUpConversions(localRef);
        return parent switch
        {
            IArgumentOperation argument => !IsTerminalUse(argument),
            IReturnOperation => true,
            ISimpleAssignmentOperation assignment when SkipConversions(assignment.Value) == localRef
                || assignment.Value == localRef => assignment.Target is not ILocalReferenceOperation,
            IVariableInitializerOperation => true, // aliased into another variable
            _ => false,
        };
    }

    /// <summary>Serialization and validation consume the finished event; they are terminal
    /// uses, not escapes.</summary>
    private static bool IsTerminalUse(IArgumentOperation argument)
    {
        var containingType = (argument.Parent as IInvocationOperation)?.TargetMethod.ContainingType;
        return containingType?.Name is "OcsfJson" or "OcsfValidator" or "OcsfEventReader";
    }

    private static void Report(OperationBlockAnalysisContext context, Tracked item, KnownTypes known)
    {
        var type = (INamedTypeSymbol)item.Creation.Type!;

        // Pointwise diagnostics stay active for escaped instances only when the violation is
        // already definitive; requiredness and constraints need the full local picture.
        foreach (var (property, location) in item.OtherAssignments)
        {
            var sibling = GetSiblingName(property, known)!;
            if (!item.Escaped && !item.ExplicitLabels.Contains(sibling))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.OtherRequiresLabel, location, property.Name, sibling));
            }
        }

        if (!item.Escaped && !item.Assigned.Contains("TypeUid"))
        {
            foreach (var location in item.DirectActivityAssignments)
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.UseSetActivity, location));
        }

        if (item.Escaped)
            return;

        var creationLocation = item.Creation.Syntax.GetLocation();
        foreach (var property in GetAllProperties(type))
        {
            if (!IsRequired(property, known) || item.Assigned.Contains(property.Name))
                continue;
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.RequiredAttributeMissing, creationLocation,
                GetSchemaName(property, known), property.Name, type.Name));
        }

        if (known.ConstraintAttribute is null)
            return;

        foreach (var attribute in type.GetAttributes().Where(a =>
                     SymbolEqualityComparer.Default.Equals(a.AttributeClass, known.ConstraintAttribute)))
        {
            if (attribute.ConstructorArguments.Length != 2)
                continue;
            var kind = attribute.ConstructorArguments[0].Value as int? ?? 0;
            var names = attribute.ConstructorArguments[1].Values
                .Select(v => v.Value as string)
                .Where(v => v is not null)
                .Select(v => v!)
                .ToArray();
            var assignedCount = names.Count(item.Assigned.Contains);

            if (kind == 0 && assignedCount == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.ConstraintUnsatisfied, creationLocation,
                    type.Name, "at least one", string.Join(", ", names), ""));
            }
            else if (kind == 1 && assignedCount != 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.ConstraintUnsatisfied, creationLocation,
                    type.Name, "exactly one", string.Join(", ", names), $", found {assignedCount}"));
            }
        }
    }

    private static bool IsRequired(IPropertySymbol property, KnownTypes known)
    {
        var attribute = property.GetAttributes().FirstOrDefault(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, known.RequirementAttribute));
        if (attribute is null || attribute.ConstructorArguments.Length != 1)
            return false;
        if (attribute.ConstructorArguments[0].Value is not int level || level != 2)
            return false;
        // Constructor-populated attributes need no assignment; profile-sourced requirements
        // only apply when the event declares the profile, which is not statically knowable.
        return !attribute.NamedArguments.Any(n =>
            (n.Key == "InitializedByConstructor" && n.Value.Value is true)
            || (n.Key == "Profile" && n.Value.Value is string));
    }

    private static string? GetSiblingName(IPropertySymbol property, KnownTypes known)
    {
        if (known.SiblingAttribute is null)
            return null;
        var attribute = property.GetAttributes().FirstOrDefault(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, known.SiblingAttribute));
        return attribute?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    private static string GetSchemaName(IPropertySymbol property, KnownTypes known)
    {
        if (known.JsonPropertyNameAttribute is not null)
        {
            var attribute = property.GetAttributes().FirstOrDefault(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, known.JsonPropertyNameAttribute));
            if (attribute?.ConstructorArguments.FirstOrDefault().Value is string name)
                return name;
        }
        return property.Name;
    }

    private static IEnumerable<IPropertySymbol> GetAllProperties(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property)
                    yield return property;
            }
        }
    }

    private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is not null)
                return property;
        }
        return null;
    }

    private static bool IsNinetyNine(object value) => value switch
    {
        int i => i == 99,
        long l => l == 99,
        short s => s == 99,
        byte b => b == 99,
        _ => false,
    };

    private static IOperation? SkipConversions(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;
        return operation;
    }

    private static IOperation? WalkUpConversions(IOperation operation)
    {
        var current = operation.Parent;
        while (current is IConversionOperation)
            current = current.Parent;
        return current;
    }
}

internal static class OperationTreeExtensions
{
    public static IEnumerable<IOperation> DescendantsAndSelf(this IOperation operation)
    {
        var stack = new Stack<IOperation>();
        stack.Push(operation);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            foreach (var child in current.ChildOperations)
                stack.Push(child);
        }
    }
}
