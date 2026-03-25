using Engine.Attributes;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static SourceGenCommon;



[Generator]
public sealed class SerializationGenerator : IIncrementalGenerator
{










    static (string serialize, string deserialize) AutoGenerateCodePathsForType(ITypeSymbol t)
    {



        if (t.TypeKind == TypeKind.Interface || t.IsAbstract)
        {
            return ("throw new NotSupportedException()", "throw new NotSupportedException()");
        }

        

        //unmanaged + contiguous

        static bool CanBeBlit(ITypeSymbol type)
            =>
            type.IsUnmanagedType
            &&
            !type.GetMembers()
                .OfType<IFieldSymbol>()
                    .Any(f => !f.IsStatic && f.GetAttributes()
                        .Any(a => a.AttributeClass?.Name == nameof(NonSerializedAttribute)));






        var tName = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);


        var deserializeLocal = new StringBuilder();
        var serializeLocal = new StringBuilder();




        static bool chooseKnownPath(ITypeSymbol t) => t.IsValueType;


        static string GetRead(ITypeSymbol t)
            => $"({t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}) {(chooseKnownPath(t) ? $"ReadKnownType<{t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>(ref reader)" : $"ReadUnknownType(ref reader)")}";


        static string GetWrite(ITypeSymbol t, string variable)
            => $"{(chooseKnownPath(t) ? $"WriteKnownType<{t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>({variable}, ref writer)" : $"WriteUnknownType({variable}, ref writer, writeTypeID: true)")}";



        serializeLocal.AppendLine($"\t\t\tvar value_cast = Unsafe.As<T, {tName}>(ref value);");

        


        // ARRAY

        if (t is IArrayTypeSymbol arr)
        {

            var elementName = arr.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            deserializeLocal.AppendLine($"\t\t\tvar len = reader.ReadVariableLengthUnsigned();");
            deserializeLocal.AppendLine($"\t\t\tvar arr = new {elementName}[len];");


            serializeLocal.AppendLine($"\t\t\twriter.WriteVariableLengthUnsigned((ulong)value_cast.Length);");


            if (CanBeBlit(arr.ElementType))
            {
                deserializeLocal.AppendLine($"\t\t\tSpan<byte> span = MemoryMarshal.AsBytes(arr.AsSpan());");
                deserializeLocal.AppendLine($"\t\t\treader.ReadUnmanagedSpan<{elementName}>((uint)len);");

                serializeLocal.AppendLine($"\t\t\twriter.WriteUnmanagedSpan(value_cast.AsSpan());");
            }
            else
            {
                deserializeLocal.AppendLine($"\t\t\tfor (ulong j = 0; j < len; j++)");
                deserializeLocal.AppendLine($"\t\t\t\tarr[j] = {GetRead(arr.ElementType)};");

                serializeLocal.AppendLine($"\t\t\tfor (int i = 0; i < value_cast.Length; i++)");
                serializeLocal.AppendLine($"\t\t\t\t{GetWrite(arr.ElementType, "value_cast[i]")};");
            }


            deserializeLocal.AppendLine($"\t\t\treturn Unsafe.As<{tName}, T>(ref arr);");


        }




        // DICTIONARY

        else if (IsDictionary(t, out var key, out var value))
        {

            var keyName = key.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var valueName = value.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            deserializeLocal.AppendLine($"\t\t\tvar count = reader.ReadVariableLengthUnsigned();");
            deserializeLocal.AppendLine($"\t\t\tvar dict = new {tName}((int)count);");

            deserializeLocal.AppendLine($"\t\t\tfor (ulong j = 0; j < count; j++)");
            deserializeLocal.AppendLine($"\t\t\t\tdict[{GetRead(key)}] = {GetRead(value)};");

            deserializeLocal.AppendLine($"\t\t\treturn Unsafe.As<{tName}, T>(ref dict);");


            serializeLocal.AppendLine($"\t\t\twriter.WriteVariableLengthUnsigned((ulong)value_cast.Count);");

            serializeLocal.AppendLine($"\t\t\tforeach (var kv in value_cast)");
            serializeLocal.AppendLine("\t\t\t{");
            serializeLocal.AppendLine($"\t\t\t\t{GetWrite(key, "kv.Key")};");
            serializeLocal.AppendLine($"\t\t\t\t{GetWrite(value, "kv.Value")};");
            serializeLocal.AppendLine("\t\t\t}");

        }




        // STRING

        else if (t.SpecialType == SpecialType.System_String)
        {
            deserializeLocal.AppendLine($"\t\t\tvar str = reader.ReadString();");
            deserializeLocal.AppendLine($"\t\t\treturn Unsafe.As<string, T>(ref str);");

            serializeLocal.AppendLine($"\t\t\twriter.WriteString(value_cast);");
        }




        // BLITTABLE      (UNMANAGED)

        else if (CanBeBlit(t))
        {
            deserializeLocal.AppendLine($"\t\t\tvar value = reader.ReadUnmanaged<{tName}>();");
            deserializeLocal.AppendLine($"\t\t\treturn Unsafe.As<{tName}, T>(ref value);");

            serializeLocal.AppendLine($"\t\t\twriter.WriteUnmanaged(value_cast);");
        }




        // NON-BLITTABLE   (MANAGED OR PARTICULAR)

        else
        {
            deserializeLocal.AppendLine($"\t\t\t{tName} temp_var = new();");

            foreach (var member in t.GetMembers())
            {
                if (member.DeclaredAccessibility == Accessibility.Public && !member.IsStatic)
                {
                    void commit(string name, ITypeSymbol type)
                    {
                        deserializeLocal.AppendLine($"\t\t\ttemp_var.{name} = {GetRead(type)};");

                        serializeLocal.AppendLine($"\t\t\t{GetWrite(type, $"value_cast.{name}")};");
                    }

                    if (member is IFieldSymbol field && !field.IsReadOnly)
                        commit(field.Name, field.Type);
                }
            }

            deserializeLocal.AppendLine($"\t\t\treturn Unsafe.As<{tName}, T>(ref temp_var);");
        }



        return (serializeLocal.ToString(), deserializeLocal.ToString());

    }










    static bool IsDictionary(ITypeSymbol type, out ITypeSymbol key, out ITypeSymbol value)
    {
        if (type is INamedTypeSymbol named)
        {
            var dictInterface = named.AllInterfaces
                .FirstOrDefault(i =>
                    i.OriginalDefinition.ToDisplayString() ==
                    "System.Collections.Generic.IDictionary<TKey, TValue>");

            if (dictInterface != null)
            {
                key = dictInterface.TypeArguments[0];
                value = dictInterface.TypeArguments[1];
                return true;
            }
        }

        key = null;
        value = null;
        return false;
    }








    public void Initialize(IncrementalGeneratorInitializationContext context)
    {





        context.RegisterSourceOutput(
            context.CompilationProvider,
            static (spc, compilation) =>
            {


                var AllInvolvedDynamicCastTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);





                var serializerDeserializerBase =
                    compilation.GetTypeByMetadataName(
                        "Engine.Core.Parsing+BinarySerializerDeserializerBase");



                var serializerDeserializers =
                    GetAllTypes(compilation.Assembly.GlobalNamespace)
                        .Where(t => DerivesFrom(t, serializerDeserializerBase))
                        .ToArray();



                if (serializerDeserializerBase == null)
                {
                    EmitDiagnostic(spc, "Base not found", null);
                    return;
                }



                var staticTypeIDSB = new StringBuilder();
                staticTypeIDSB.AppendLine("\t\tpublic static partial ulong GetTypeID<TSerializerDeserializer>(Type type) where TSerializerDeserializer : BinarySerializerDeserializerBase");
                staticTypeIDSB.AppendLine("\t\t{");


                var staticIntermediateSB = new StringBuilder();
                staticIntermediateSB.AppendLine("\t\tpublic static partial Type GetTypeIntermediateRepresentation<TSerializerDeserializer>(Type type) where TSerializerDeserializer : BinarySerializerDeserializerBase");
                staticIntermediateSB.AppendLine("\t\t{");




                foreach (var serializerClass in serializerDeserializers)
                {
                    
                    staticTypeIDSB.AppendLine($"\t\t\tif (typeof(TSerializerDeserializer) == typeof({serializerClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}))");
                    staticTypeIDSB.AppendLine("\t\t\t{");

                    staticIntermediateSB.AppendLine($"\t\t\tif (typeof(TSerializerDeserializer) == typeof({serializerClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}))");
                    staticIntermediateSB.AppendLine("\t\t\t{");





                    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------------


                    var straightforwardTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
                    var intermediateTypes = new Dictionary<ITypeSymbol, (ITypeSymbol intermediate, string serializeBody, string deserializeBody)>(SymbolEqualityComparer.Default);




                    var cl = serializerClass;
                    while (cl.BaseType != null)
                    {
                        foreach (var attr in cl.GetAttributes().Where(x => SymbolEqualityComparer.Default.Equals(
                                x.AttributeClass,
                                compilation.GetTypeByMetadataName(typeof(BinarySerializableTypeAttribute).FullName)
                            )))
                        {



                            var attrLocation = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation();


                            var attributeArguments = GetAttributeConstructorValues(attr);



                            if (!attributeArguments.Any())
                            {
                                EmitDiagnostic(spc, $"Redundant/broken attribute", attrLocation);
                                return;
                            }




                            //simple type codepath

                            if (attributeArguments.TryGetValue("type", out var typeget) && typeget != null)
                            {

                                var type = (ITypeSymbol)typeget;

                                straightforwardTypes.Add(type);


                            }






                            //intermediate codepath

                            else
                            {

                                IMethodSymbol serializeMethod = null;
                                IMethodSymbol deserializeMethod = null;



                                ITypeSymbol type = null;
                                ITypeSymbol intermediate = null;


                                if (attributeArguments.TryGetValue("serializeMethod", out var serializeMethodGet) && serializeMethodGet != null)
                                {
                                    serializeMethod = cl.GetMembers((string)serializeMethodGet).OfType<IMethodSymbol>().FirstOrDefault();


                                    if (serializeMethod == null)
                                    {
                                        EmitDiagnostic(spc, $"Serialization method cannot be found, consider using nameof()", attrLocation);
                                        return;
                                    }


                                    if (serializeMethod.Parameters.Length != 1)
                                    {
                                        EmitDiagnostic(spc, $"Serialization method must accept a single parameter of the type to be serialized", attrLocation);
                                        return;
                                    }

                                    if (SymbolEqualityComparer.Default.Equals(serializeMethod.ReturnType, compilation.GetSpecialType(SpecialType.System_Void)))
                                    {
                                        EmitDiagnostic(spc, $"Serialization method must return the intermediate serialized type", attrLocation);
                                        return;
                                    }

                                    intermediate = serializeMethod.ReturnType;

                                    type = serializeMethod.Parameters[0].Type;
                                }





                                if (attributeArguments.TryGetValue("deserializeMethod", out var deserializeMethodGet) && deserializeMethodGet != null)
                                {
                                    deserializeMethod = cl.GetMembers((string)deserializeMethodGet).OfType<IMethodSymbol>().FirstOrDefault();


                                    if (deserializeMethod == null)
                                    {
                                        EmitDiagnostic(spc, $"Deserialization method cannot be found, consider using nameof()", attrLocation);
                                        return;
                                    }


                                    if (deserializeMethod.Parameters.Length != 1)
                                    {
                                        EmitDiagnostic(spc, $"Deserialization method must accept a single parameter of the intermediate type to be deserialized", attrLocation);
                                        return;
                                    }

                                    if (SymbolEqualityComparer.Default.Equals(deserializeMethod.ReturnType, compilation.GetSpecialType(SpecialType.System_Void)))
                                    {
                                        EmitDiagnostic(spc, $"Deserialization method must return the deserialized type", attrLocation);
                                        return;
                                    }



                                    if (intermediate == null)
                                        intermediate = deserializeMethod.Parameters[0].Type;

                                    else if (!SymbolEqualityComparer.Default.Equals(intermediate, deserializeMethod.Parameters[0].Type))
                                    {
                                        EmitDiagnostic(spc, "Custom serialization and deserialization methods must exchange the same intermediate type", attrLocation);
                                        return;
                                    }


                                    if (type == null)
                                        type = deserializeMethod.ReturnType;

                                    else if (!SymbolEqualityComparer.Default.Equals(type, deserializeMethod.ReturnType))
                                    {
                                        EmitDiagnostic(spc, "Custom serialization and deserialization methods must exchange the same main type", attrLocation);
                                        return;
                                    }


                                }





                                var intermediateName = intermediate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                                var intermediateAssignment = intermediateTypes[type] =
                                    (
                                        intermediate,
                                        serializeMethod == null ? "\t\t\tthrow new Exception();" : $"\t\t\tWriteKnownType<{intermediateName}>({serializeMethod.Name}(value_cast), ref writer);\n\t\t\treturn;",
                                        deserializeMethod == null ? "\t\t\tthrow new Exception();" : $"\t\t\tvar value = {deserializeMethod.Name}(ReadKnownType<{intermediateName}>(ref reader));\n\t\t\treturn Unsafe.As<{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, T>(ref value);"
                                    );


                            }

                        }



                        cl = cl.BaseType;
                    }




                    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------------


                    var final_map = new Dictionary<ITypeSymbol,
                            (ITypeSymbol intermediate, string serializebody, string deserializebody)>(
                            SymbolEqualityComparer.Default);




                    foreach (var kv in intermediateTypes)
                    {
                        Expand(kv.Key);
                    }


                    foreach (var t in straightforwardTypes)
                    {
                        Expand(t);
                    }





                    void Expand(ITypeSymbol t)
                    {
                        if (final_map.ContainsKey(t))
                            return;


                        if (intermediateTypes.TryGetValue(t, out var intermediateInfo))
                        {
                            Expand(intermediateInfo.intermediate);

                            final_map[t] = intermediateInfo;
                            return;
                        }


                        if (t is IArrayTypeSymbol arr)
                        {
                            Expand(arr.ElementType);
                        }

                        else if (IsDictionary(t, out var key, out var value))
                        {
                            Expand(key);
                            Expand(value);
                        }

                        else if (t.TypeKind == TypeKind.Enum)
                        {
                            Expand(((INamedTypeSymbol)t).EnumUnderlyingType);
                        }

                        else if (t is INamedTypeSymbol named)
                        {
                            foreach (var member in named.GetMembers())
                            {
                                if (member is IFieldSymbol field &&
                                    !field.IsStatic &&
                                    !field.IsReadOnly &&
                                    !field.GetAttributes().Any(x => x.AttributeClass.Name == nameof(NonSerializedAttribute)))
                                {
                                    Expand(field.Type);
                                }
                            }
                        }

                        var (serialize, deserialize) = AutoGenerateCodePathsForType(t);

                        final_map[t] = (t, serialize, deserialize);
                    }



                    var final_this_all_types = final_map.Keys
                        .OrderBy(t => t.ToDisplayString())
                        .ToArray();




                    AllInvolvedDynamicCastTypes.UnionWith(final_this_all_types);




                    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------------



                    var getIntermediate = new StringBuilder();
                    getIntermediate.AppendLine("\tpublic override Type GetTypeIntermediateRepresentation(Type type)");
                    getIntermediate.AppendLine("\t{");




                    var deserializeUnknown = new StringBuilder();
                    deserializeUnknown.AppendLine("\tpublic override object ReadUnknownType(ref ValueReader reader)");
                    deserializeUnknown.AppendLine("\t{");
                    deserializeUnknown.AppendLine("\t\tvar id = reader.ReadVariableLengthUnsigned();");
                    deserializeUnknown.AppendLine("\t\tswitch (id)");
                    deserializeUnknown.AppendLine("\t\t{");



                    var gettypeID = new StringBuilder();
                    gettypeID.AppendLine("\tpublic override ulong GetTypeID(Type type)");
                    gettypeID.AppendLine("\t{");




                    var serializeUnknown = new StringBuilder();
                    serializeUnknown.AppendLine("\tpublic override void WriteUnknownType(object value, ref ValueWriter writer, bool writeTypeID = false)");
                    serializeUnknown.AppendLine("\t{");
                    serializeUnknown.AppendLine("\t\tvar objType = value.GetType();");




                    var serializeKnown = new StringBuilder();
                    serializeKnown.AppendLine("\tpublic override void WriteKnownType<T>(T value, ref ValueWriter writer, bool writeTypeID = false)");
                    serializeKnown.AppendLine("\t{");
                    serializeKnown.AppendLine("\t\tvar objType = typeof(T);");




                    var deserialize = new StringBuilder();
                    deserialize.AppendLine("\tpublic unsafe override T ReadKnownType<T>(ref ValueReader reader)");
                    deserialize.AppendLine("\t{");





                    string GetException(string gettype) => $"\t\tthrow new NotImplementedException(\n#if DEBUG\n$\"Path for type '{gettype}' not generated, mark via attribute\"\n#endif\n);";




                    for (int i = 0; i < final_this_all_types.Length; i++)
                    {
                        var t = final_this_all_types[i];
                        var tName = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);


                        deserializeUnknown.AppendLine($"\t\t\tcase {i}:");
                        deserializeUnknown.AppendLine($"\t\t\t\treturn ReadKnownType<{tName}>(ref reader);");

                        gettypeID.AppendLine($"\t\tif (type == typeof({tName})) return {i};");
                        staticTypeIDSB.AppendLine($"\t\t\t\tif (type == typeof({tName})) return {i};");


                        getIntermediate.AppendLine($"\t\tif (type == typeof({tName})) return typeof({final_map[t].intermediate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)});");
                        staticIntermediateSB.AppendLine($"\t\t\t\tif (type == typeof({tName})) return typeof({final_map[t].intermediate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)});");




                        deserialize.AppendLine($"\t\tif (typeof(T) == typeof({tName}))");
                        deserialize.AppendLine("\t\t{");
                        deserialize.Append(final_map[t].deserializebody);
                        deserialize.AppendLine();
                        deserialize.AppendLine("\t\t}");


                        serializeKnown.AppendLine($"\t\tif (objType == typeof({tName}))");
                        serializeKnown.AppendLine("\t\t{");


                        serializeKnown.AppendLine($"\t\t\tif (writeTypeID) writer.WriteVariableLengthUnsigned((ulong){i});");

                        serializeKnown.Append(final_map[t].serializebody);
                        serializeKnown.AppendLine($"\t\t\treturn;");
                        serializeKnown.AppendLine("\t\t}");



                        serializeUnknown.AppendLine($"\t\tif (objType == typeof({tName}))");
                        serializeUnknown.AppendLine("\t\t{");
                        serializeUnknown.AppendLine($"\t\t\tWriteKnownType<{tName}>(({tName})value, ref writer, writeTypeID);");
                        serializeUnknown.AppendLine("\t\t\treturn;");
                        serializeUnknown.AppendLine("\t\t}");
                    }







                    deserialize.AppendLine();
                    deserialize.AppendLine(GetException("{typeof(T).FullName}"));
                    deserialize.AppendLine("\t}");

                    serializeKnown.AppendLine(GetException("{typeof(T).FullName}"));
                    serializeKnown.AppendLine("\t}");



                    serializeUnknown.AppendLine(GetException("{value.GetType().FullName}"));
                    serializeUnknown.AppendLine("\t}");



                    deserializeUnknown.AppendLine($"\t\t\tdefault:\n\t\t\t {GetException("ID = {id}")}");
                    deserializeUnknown.AppendLine("\t\t}");
                    deserializeUnknown.AppendLine("\t}");

                    gettypeID.AppendLine(GetException("{type.FullName}"));
                    gettypeID.AppendLine("\t}");


                    getIntermediate.AppendLine();
                    getIntermediate.AppendLine("\t\treturn null;");
                    getIntermediate.AppendLine("\t}");





                    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------------



                    var usingSB = new StringBuilder();


                    usingSB.AppendLine("// <auto-generated />");
                    usingSB.AppendLine("using System;");
                    usingSB.AppendLine("using System.Runtime.CompilerServices;");
                    usingSB.AppendLine("using static Engine.Core.Parsing;");
                    usingSB.AppendLine();
                    usingSB.AppendLine();




                    var SB = new StringBuilder();

                    SB.Append(gettypeID);
                    SB.Append(deserializeUnknown);
                    SB.Append(getIntermediate);
                    SB.Append(serializeKnown);
                    SB.Append(serializeUnknown);
                    SB.Append(deserialize);


                    spc.AddSource(GetValidSourceFileNameFromTypeName(serializerClass), usingSB.ToString() + GenerateIntoType(serializerClass, string.Empty, SB.ToString()));




                    staticIntermediateSB.AppendLine("\t\t\t}");
                    staticTypeIDSB.AppendLine("\t\t\t}");

                }







                staticTypeIDSB.AppendLine("\t\t\tthrow new Exception();");
                staticTypeIDSB.AppendLine("\t\t}");

                staticIntermediateSB.AppendLine("\t\t\tthrow new Exception();");
                staticIntermediateSB.AppendLine("\t\t}");


                spc.AddSource(GetValidSourceFileNameFromTypeName(serializerDeserializerBase), GenerateIntoType(serializerDeserializerBase, string.Empty, staticTypeIDSB.ToString() + "\n\n\n" + staticIntermediateSB.ToString()));








                //  ---------------------------------------------- INDEXABLE GENERATION ----------------------------------------------






                static ISymbol[] GetDataValueFields(ITypeSymbol type, Compilation compilation)
                {
                    var attributeSymbol = compilation.GetTypeByMetadataName(
                        typeof(IndexableAttribute).FullName);

                    var results = new List<ISymbol>();

                    for (var current = type; current != null; current = current.BaseType)
                    {
                        foreach (var member in current.GetMembers())
                        {
                            if (member is not (IFieldSymbol or IPropertySymbol))
                                continue;

                            if (member.GetAttributes().Any(a =>
                                SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeSymbol)))
                            {
                                results.Add(member);
                            }
                        }
                    }

                    return results.OrderBy(x => x.Name).ToArray();
                }





                var idxSB = new StringBuilder();




                idxSB.AppendLine("// <auto-generated />");
                idxSB.AppendLine("namespace Engine.Core;");
                idxSB.AppendLine("using System;");
                idxSB.AppendLine("using System.Runtime.CompilerServices;");
                idxSB.AppendLine();

                idxSB.AppendLine("public static partial class Parsing");
                idxSB.AppendLine("{");



                var getString = new StringBuilder();
                var getUshort = new StringBuilder();
                var setString = new StringBuilder();
                var setUshort = new StringBuilder();


                getUshort.AppendLine($"\tpublic static partial T GetIndexable<T>(this object obj, ushort idx)");
                getUshort.AppendLine("\t{");
                getUshort.AppendLine("\t\tvar objType = obj.GetType();");
                getUshort.AppendLine();



                getString.AppendLine($"\tpublic static partial T GetIndexable<T>(this object obj, string idx)");
                getString.AppendLine("\t{");
                getString.AppendLine("\t\tvar objType = obj.GetType();");
                getString.AppendLine();



                setUshort.AppendLine($"\tpublic static partial void SetIndexable<T>(this object obj, ushort idx, T value)");
                setUshort.AppendLine("\t{");
                setUshort.AppendLine("\t\tvar objType = obj.GetType();");
                setUshort.AppendLine();



                setString.AppendLine($"\tpublic static partial void SetIndexable<T>(this object obj, string idx, T value)");
                setString.AppendLine("\t{");
                setString.AppendLine("\t\tvar objType = obj.GetType();");
                setString.AppendLine();






                foreach (var t in GetAllTypes(compilation.Assembly.GlobalNamespace))
                {
                    var members = GetDataValueFields(t, compilation);


                    AllInvolvedDynamicCastTypes.UnionWith(members.Select(f => f is IFieldSymbol fieldSymbol ? fieldSymbol.Type : ((IPropertySymbol)f).Type));


                    if (members.Any())
                    {

                        var pre = new StringBuilder();

                        pre.AppendLine($"\t\tif (objType == typeof({t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}))");
                        pre.AppendLine("\t\t{");
                        pre.AppendLine($"\t\t\tvar value_cast = ({t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})obj;");
                        pre.AppendLine("\t\t\tswitch (idx)");
                        pre.AppendLine("\t\t\t{");


                        getString.Append(pre);
                        getUshort.Append(pre);
                        setString.Append(pre);
                        setUshort.Append(pre);



                        for (int i = 0; i < members.Length; i++)
                        {
                            ISymbol? f = members[i];

                            var memberTypeName = (f is IFieldSymbol fieldSymbol ? fieldSymbol.Type : ((IPropertySymbol)f).Type).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);



                            if (f is not IPropertySymbol prop || (prop.GetMethod != null && prop.GetMethod.DeclaredAccessibility == Accessibility.Public))
                            {
                                var logic = $@"

                    if (typeof(T) == typeof({memberTypeName}))
                    {{
                        var temp_val_{i} = value_cast.{f.Name};
                        return Unsafe.As<{memberTypeName}, T>(ref temp_val_{i});
                    }}

                    
                    return UnknownCast<T>(value_cast.{f.Name});
                                ";


                                getString.AppendLine($"\t\t\t\tcase \"{f.Name}\": {logic}");
                                getUshort.AppendLine($"\t\t\t\tcase {i}: {logic}");
                            }


                            if (f is not IPropertySymbol prop2 || (prop2.SetMethod != null && prop2.SetMethod.DeclaredAccessibility == Accessibility.Public))
                            {
                                var logic = $@"
                    if (typeof(T) == typeof({memberTypeName}))
                        value_cast.{f.Name} = Unsafe.As<T, {memberTypeName}>(ref value);

                    value_cast.{f.Name} = UnknownCast<{memberTypeName}>(value);
                    
                    return;
                                ";


                                setString.AppendLine($"\t\t\t\tcase \"{f.Name}\": {logic} ");
                                setUshort.AppendLine($"\t\t\t\tcase {i}: {logic} ");
                            }
                        }



                        var post = new StringBuilder();

                        post.AppendLine("\t\t\t\tdefault: throw new KeyNotFoundException();");
                        post.AppendLine("\t\t\t}");
                        post.AppendLine("\t\t}");


                        getString.Append(post);
                        getUshort.Append(post);
                        setString.Append(post);
                        setUshort.Append(post);




                        var memberEntries = members
                            .Select((param, index) => new
                            {
                                Left = $"<see cref = \"{param.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{param.Name}\"/>",
                                Index = index,
                                param.Name
                            })
                            .ToArray();


                        int maxLen = memberEntries.Max(x => x.Left.Length);

                        var formattedLines = memberEntries.Select(x =>
                        {
                            var paddedLeft = x.Left.PadRight(maxLen);
                            return $"/// <br/> - <c>{paddedLeft} -> [{x.Index}] / [\"{x.Name}\"] </c>";
                        });

                        var docBlock =
                            "/// <remarks>" +
                            "<br/> --------------------------------------------- " +
                            $"<br/> [<see cref = \"{typeof(IndexableAttribute).FullName}\"/>] Members:" +
                            "<br/> --------------------------------------------- \n" +
                            string.Join("\n", formattedLines) +
                            "\n/// <br/> </remarks>";




                        spc.AddSource(
                            GetValidSourceFileNameFromTypeName(t),

                            GenerateIntoType(t, docBlock,

                            string.Empty));

                    }

                }



                getString.AppendLine("\t\tthrow new Exception();");
                getString.AppendLine("\t}");

                setString.AppendLine("\t\tthrow new Exception();");
                setString.AppendLine("\t}");

                getUshort.AppendLine("\t\tthrow new Exception();");
                getUshort.AppendLine("\t}");

                setUshort.AppendLine("\t\tthrow new Exception();");
                setUshort.AppendLine("\t}");


                idxSB.Append(getString);
                idxSB.Append(getUshort);
                idxSB.Append(setString);
                idxSB.Append(setUshort);



                idxSB.AppendLine("}");


                spc.AddSource("IndexableGenerated.g.cs", idxSB.ToString());








                //  ---------------------------------------------- LATE "DYNAMIC" RESOLUTION LOGIC ----------------------------------------------


                static bool IsNumeric(ITypeSymbol t)
                {
                    switch (t.SpecialType)
                    {
                        case SpecialType.System_Byte:
                        case SpecialType.System_SByte:
                        case SpecialType.System_Int16:
                        case SpecialType.System_UInt16:
                        case SpecialType.System_Int32:
                        case SpecialType.System_UInt32:
                        case SpecialType.System_Int64:
                        case SpecialType.System_UInt64:
                        case SpecialType.System_Single:   // float
                        case SpecialType.System_Double:
                        case SpecialType.System_Decimal:
                            return true;
                        default:
                            return false;
                    }
                }



                var dynamicastsb = new StringBuilder();


                dynamicastsb.AppendLine("// <auto-generated />");
                dynamicastsb.AppendLine("namespace Engine.Core;");
                dynamicastsb.AppendLine("using System;");
                dynamicastsb.AppendLine("using System.Runtime.CompilerServices;");
                dynamicastsb.AppendLine();

                dynamicastsb.AppendLine("public static partial class Parsing");
                dynamicastsb.AppendLine("{");

                dynamicastsb.AppendLine("\tpublic static partial TTo UnknownCast<TTo>(object value)");
                dynamicastsb.AppendLine("\t{");
                dynamicastsb.AppendLine("\t\tif (value == null) throw new ArgumentNullException(nameof(value));");
                dynamicastsb.AppendLine("\t\tvar type = value.GetType();");
                dynamicastsb.AppendLine();

                foreach (var src in AllInvolvedDynamicCastTypes)
                {
                    var srcName = src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                    dynamicastsb.AppendLine($"\t\tif (type == typeof({srcName}))");
                    dynamicastsb.AppendLine("\t\t{");

                    foreach (var dst in AllInvolvedDynamicCastTypes)
                    {
                        var dstName = dst.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);


                        var conversionMethods = src.GetMembers()
                            .Concat(dst.GetMembers())
                            .OfType<IMethodSymbol>()
                            .Where(m =>
                                m.MethodKind == MethodKind.Conversion &&
                                m.Parameters.Length == 1 &&
                                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, src) &&
                                SymbolEqualityComparer.Default.Equals(m.ReturnType, dst));



                        if (
                            (DerivesFrom(src, dst) || DerivesFrom(dst, src) || SymbolEqualityComparer.Default.Equals(dst, src))   //inheritance/match
                            || dst.SpecialType == SpecialType.System_Object    //boxing request
                            || (IsNumeric(src) && IsNumeric(dst))              //numeric
                            || conversionMethods.Any())                        //explicit
                        {


                            dynamicastsb.AppendLine($"\t\t\tif (typeof(TTo) == typeof({dstName}))");
                            dynamicastsb.AppendLine("\t\t\t{");
                            dynamicastsb.AppendLine($"\t\t\t\tvar srcVal = ({srcName})value;");
                            dynamicastsb.AppendLine($"\t\t\t\tvar cast = ({dstName})srcVal;");
                            dynamicastsb.AppendLine($"\t\t\t\treturn Unsafe.As<{dstName}, TTo>(ref cast);");
                            dynamicastsb.AppendLine("\t\t\t}");
                        }
                    }

                    dynamicastsb.AppendLine("\t\t}");
                    dynamicastsb.AppendLine();
                }

                dynamicastsb.AppendLine("\t\tthrow new InvalidCastException(\n#if DEBUG\n$\"No conversion from '{value.GetType()}' to '{typeof(TTo)}'\"\n#endif\n);");
                dynamicastsb.AppendLine("\t}");




                dynamicastsb.AppendLine("}");



                spc.AddSource("CastGenerated.g.cs", dynamicastsb.ToString());


            });


    }




}
