


using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;



public static class SourceGenCommon
{

    public static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol nestedNs)
            {
                foreach (var t in GetAllTypes(nestedNs))
                    yield return t;
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;

                foreach (var nestedType in GetNestedTypes(type))
                    yield return nestedType;
            }
        }
    }

    public static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;

            foreach (var deeper in GetNestedTypes(nested))
                yield return deeper;
        }
    }

    public static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var t = type.BaseType; t != null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, baseType))
                return true;
        }

        return false;
    }



    public static string FormatLiteral(
        object? value,
        ITypeSymbol targetType)
    {
        if (value == null)
            return "null";

        // Enum constants
        if (targetType.TypeKind == TypeKind.Enum)
        {
            var enumType =
                targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            return $"{enumType}.{value}";
        }

        return value switch
        {
            string s => $"\"{s}\"",
            char c => $"'{c}'",
            bool b => b ? "true" : "false",

            float f => f.ToString("R") + "f",
            double d => d.ToString("R"),
            long l => l.ToString() + "L",
            ulong ul => ul.ToString() + "UL",
            uint ui => ui.ToString() + "u",

            byte or sbyte or short or ushort or int
                => value.ToString()!,

            _ => $"default({targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})"
        };
    }


    public static bool CallsMethod(
        Compilation compilation,
        IMethodSymbol caller,
        IMethodSymbol target)
    {
        foreach (var syntaxRef in caller.DeclaringSyntaxReferences)
        {
            var syntaxTree = syntaxRef.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            if (syntaxRef.GetSyntax() is not MethodDeclarationSyntax methodDecl)
                continue;

            var body = (SyntaxNode?)methodDecl.Body ?? methodDecl.ExpressionBody;
            if (body is null)
                continue;

            foreach (var invocation in body.DescendantNodes()
                         .OfType<InvocationExpressionSyntax>())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                if (symbolInfo.Symbol is not IMethodSymbol invoked)
                    continue;

                if (SymbolEqualityComparer.Default.Equals(
                    invoked.OriginalDefinition,
                    target.OriginalDefinition))
                {
                    return true;
                }
            }
        }

        return false;
    }



    internal static string GenerateIntoType(
        INamedTypeSymbol target,
        string injectedPretense,
        string injectedSource)
    {
        var sb = new StringBuilder();





        static string AccessibilityToString(Accessibility a) => a switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => ""
        };


        static string GetTypeKeyword(INamedTypeSymbol t) => t.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            _ => "class"
        };



        static IEnumerable<INamedTypeSymbol> GetContainingChain(INamedTypeSymbol t)
        {
            var stack = new Stack<INamedTypeSymbol>();
            var current = t;
            while (current != null)
            {
                stack.Push(current);
                current = current.ContainingType;
            }
            return stack;
        }




        if (!target.ContainingNamespace.IsGlobalNamespace)
        {
            sb.Append("namespace ");
            sb.Append(target.ContainingNamespace.ToDisplayString());
            sb.AppendLine(";");
            sb.AppendLine();
        }



        var chain = GetContainingChain(target).ToList();

        for (int i = 0; i<chain.Count; i++)
        {

            if (i == chain.Count-1)
                sb.AppendLine(injectedPretense);



            INamedTypeSymbol? type = chain[i];


            var accessibility = AccessibilityToString(type.DeclaredAccessibility);
            if (!string.IsNullOrWhiteSpace(accessibility))
            {
                sb.Append(accessibility);
                sb.Append(' ');
            }

            if (type.IsAbstract) sb.Append("abstract ");
            if (type.IsStatic) sb.Append("static ");
            sb.Append("partial ");
            sb.Append(GetTypeKeyword(type));
            sb.Append(' ');
            sb.Append(type.Name);

            if (type.TypeParameters.Length > 0)
            {
                sb.Append('<');
                sb.Append(string.Join(", ", type.TypeParameters.Select(p => p.Name)));
                sb.Append('>');
            }

            sb.AppendLine();
            sb.AppendLine("{");
        }




        sb.AppendLine(injectedSource);




        for (int i = 0; i < chain.Count; i++)
            sb.AppendLine("}");

        return sb.ToString();
    }


}