using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Ocsf.Analyzers;

namespace Ocsf.Analyzers.Tests;

internal static class AnalyzerTestHelper
{
    private static readonly string OcsfAssemblyPath = typeof(Ocsf.OcsfEvent).Assembly.Location;

    public static DiagnosticResult Diagnostic(string id) =>
        new(id, id switch
        {
            "OCSF003" => DiagnosticSeverity.Info,
            _ => DiagnosticSeverity.Warning,
        });

    /// <summary>Runs the analyzer over source compiled against the real generated Ocsf assembly.</summary>
    public static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<OcsfConstructionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(OcsfAssemblyPath));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

}
