// GameObjectInitGenerator.cs
// Simplified single-file Incremental Generator — flat, minimal style, no namespace gymnastics.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Engine.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;


[Generator]
public sealed class GameObjectInitGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                transform: static (ctx, _) => GetCandidateMethod(ctx))
            .Where(static m => m is not null);

        var combined = context.CompilationProvider.Combine(methods.Collect());

        context.RegisterSourceOutput(combined, static (spc, source) =>
        {
            var (compilation, methodSymbols) = source;


            var validMethods = new List<IMethodSymbol>();
            var diagnostics = new List<Diagnostic>();

            foreach (var method in methodSymbols)
            {
                // Attribute check by simple name
                bool hasAttribute = method.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(GameObjectInitMethodAttribute));
                if (!hasAttribute)
                    continue;

                var type = method.ContainingType;

                // Validation: one method per type
                var otherInitMethods = type.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(GameObjectInitMethodAttribute)))
                    .ToList();

                if (otherInitMethods.Count > 1)
                {
                    foreach (var m in otherInitMethods)
                        spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MultipleInitMethods, m.Locations.FirstOrDefault(), type.Name));
                    continue;
                }

                // Method validations
                if (method.IsStatic)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MethodIsStatic, method.Locations.FirstOrDefault(), method.Name));
                    continue;
                }

                if (method.IsAbstract)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MethodIsAbstract, method.Locations.FirstOrDefault(), method.Name));
                    continue;
                }

                if (!method.DeclaredAccessibility.HasFlag(Accessibility.Public))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MethodNotPublic, method.Locations.FirstOrDefault(), method.Name));
                    continue;
                }

                if (method.ReturnType.SpecialType != SpecialType.System_Void)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MethodNotVoid, method.Locations.FirstOrDefault(), method.Name));
                    continue;
                }

                validMethods.Add(method);
            } 


            var src = SourceBuilder.Generate(validMethods, compilation);
            spc.AddSource("GameObject.g.cs", src);
        });
    }

    private static IMethodSymbol? GetCandidateMethod(GeneratorSyntaxContext ctx)
    {
        var methodSyntax = (MethodDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(methodSyntax) as IMethodSymbol;
        if (symbol is null)
            return null;

        bool hasAttribute = symbol.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(GameObjectInitMethodAttribute));
        return hasAttribute ? symbol : null;
    }
}

// ----------------------------------------------
// Diagnostics
// ----------------------------------------------
internal static class Diagnostics
{
    private const string Cat = "GameObjectInit";

    public static readonly DiagnosticDescriptor MethodNotPublic =
        new("GOI001", "Init method must be public",
            "Method '{0}' marked with [GameObjectInitMethod] must be public.",
            Cat, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MethodNotVoid =
        new("GOI002", "Init method must return void",
            "Method '{0}' marked with [GameObjectInitMethod] must return void.",
            Cat, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MethodIsStatic =
        new("GOI003", "Init method cannot be static",
            "Method '{0}' marked with [GameObjectInitMethod] cannot be static.",
            Cat, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MethodIsAbstract =
        new("GOI004", "Init method cannot be abstract",
            "Method '{0}' marked with [GameObjectInitMethod] cannot be abstract.",
            Cat, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MultipleInitMethods =
        new("GOI005", "Only one Init method allowed",
            "Type '{0}' has multiple [GameObjectInitMethod] methods.",
            Cat, DiagnosticSeverity.Error, true);
}

// ----------------------------------------------
// Source Builder
// ----------------------------------------------
internal static class SourceBuilder
{
    public static string Generate(IEnumerable<IMethodSymbol> methods, Compilation compilation)
    {
        var sb = new StringBuilder();

        // File header
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("namespace Engine.GameObjects;");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Numerics;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();



        // Root container
        sb.AppendLine("public partial class GameObject : Engine.Core.Freeable");
        sb.AppendLine("{");


        // ------------------------------------------------------------------------
        // ConstructObject()
        // ------------------------------------------------------------------------
        sb.AppendLine("        public static GameObject ConstructObject(string TypeName) => TypeName switch");
        sb.AppendLine("        {");

        foreach (var m in methods)
        {
            sb.AppendLine($"            \"{m.ContainingType.Name}\" => new {m.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}(),");
        }

        sb.AppendLine("            _ => throw new NotImplementedException()");
        sb.AppendLine("        };");
        sb.AppendLine();

        // ------------------------------------------------------------------------
        // CallObjectInit()
        // ------------------------------------------------------------------------
        sb.AppendLine("        public static void CallObjectInit(GameObject obj, Dictionary<string, object> args)");
        sb.AppendLine("        {");
        sb.AppendLine("            var type = obj.GetType();");
        sb.AppendLine();

        foreach (var m in methods)
        {
            sb.AppendLine($"            // --- {m.ContainingType.Name}.Init ---");
            sb.AppendLine($"            if (type == typeof({m.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}))");
            sb.AppendLine("            {");

            foreach (var p in m.Parameters)
            {
                var pname = $"{m.ContainingType.Name.ToLowerInvariant()}_{p.Name}";
                var pnamename = $"{m.ContainingType.Name.ToLowerInvariant()}_{p.Name}_name";
                var typeSymbol = p.Type;
                var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                sb.AppendLine($"\n                // --- {p.Name} ---");
                sb.AppendLine($"                const string {pnamename} = \"{p.Name}\";");
                sb.AppendLine($"                {typeName} {pname};");

                // UNIQUE variable t per parameter
                var tvar = $"__tmp_{pname}";

                if (p.HasExplicitDefaultValue)
                {
                    sb.AppendLine($"                if (!args.TryGetValue({pnamename}, out var {tvar}))");
                    sb.AppendLine($"                    {pname} = default;");
                    sb.AppendLine($"                else");
                    sb.AppendLine($"                    {pname} = {GenerateSafeCast(typeSymbol, tvar)};");
                }
                else
                {
                    sb.AppendLine($"                if (!args.TryGetValue({pnamename}, out var {tvar}))");
                    sb.AppendLine($"                    KeyException({pnamename}, type);");
                    sb.AppendLine($"                else");
                    sb.AppendLine($"                    {pname} = {GenerateSafeCast(typeSymbol, tvar)};");
                }
            }


            sb.AppendLine("\n                // --- Init ---");
            var paramList = string.Join(", ", m.Parameters.Select(p => $"{m.ContainingType.Name.ToLowerInvariant()}_{p.Name}"));
            sb.AppendLine($"                (({m.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})obj).{m.Name}({paramList});");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
        }

        sb.AppendLine("            static void KeyException(string notfound, Type type)");
        sb.AppendLine("                => throw new KeyNotFoundException($\"Required argument {notfound} for {type} not found.\");");
        sb.AppendLine("        }");


        sb.AppendLine("}");

        return sb.ToString();

        // ---------- Local helpers ----------

        bool IsNumericType(ITypeSymbol type)
        {
            return type.AllInterfaces.Any(i =>
                i.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "System.Numerics.INumber<TSelf>");
        }


        string GenerateSafeCast(ITypeSymbol typeSymbol, string variable, bool isArray = false)
        {
            var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // ENUMS
            if (typeSymbol.TypeKind == TypeKind.Enum)
            {
                if (isArray) return $"(({typeName}[])(((object[]){variable})?.Select(x => ({((INamedTypeSymbol)typeSymbol).EnumUnderlyingType!})x).ToArray()))";
                else return $"({typeName})({((INamedTypeSymbol)typeSymbol).EnumUnderlyingType!})({variable})";
            }

            // NUMERIC TYPES
            if (IsNumericType(typeSymbol))
            {
                if (isArray) return $"(({typeName}[])(((object[]){variable})?.Select(x => ({typeName})x).ToArray()))";
                else return $"({typeName})({variable})";
            }

            return $"(({typeName}){variable})";
        }

    }
}




