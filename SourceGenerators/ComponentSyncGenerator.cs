using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ShootingGame.SourceGen
{
    /// <summary>
    /// Source Generator for [SyncComponent] partial structs.
    ///
    /// For each struct marked with [SyncComponent], generates:
    /// - ComponentTypeId constant
    /// - Shadow fields for dirty detection
    /// - WriteDelta/ReadDelta (P-frame, only changed fields)
    /// - WriteFull/ReadFull (I-frame, all fields)
    /// - MarkClean() method
    ///
    /// Supported field types: byte, int, uint, float, bool, long,
    ///                        Vec2, Vec3, Quat (from ShootingGame.Shared.Math)
    /// </summary>
    [Generator]
    public class ComponentSyncGenerator : ISourceGenerator
    {
        private const string SyncComponentAttrName = "SyncComponentAttribute";
        private const string SyncVarAttrName = "SyncVarAttribute";

        // Field type → (writerMethod, readerMethod, fieldSizeHint)
        private static readonly Dictionary<string, (string write, string read, int size)> _typeMap =
            new Dictionary<string, (string, string, int)>
            {
                { "byte",   ("WriteByte",   "ReadByte",   1) },
                { "int",    ("WriteInt32",  "ReadInt32",  4) },
                { "uint",   ("WriteUInt32", "ReadUInt32", 4) },
                { "float",  ("WriteFloat",  "ReadFloat",  4) },
                { "bool",   ("WriteBool",   "ReadBool",   1) },
                { "long",   ("WriteInt64",  "ReadInt64",  8) },
                { "Vec2",   ("WriteVec2",   "ReadVec2",   8) },
                { "Vec3",   ("WriteVec3",   "ReadVec3",  12) },
                { "Quat",   ("WriteQuat",   "ReadQuat",  16) },
            };

        public void Initialize(GeneratorInitializationContext context)
        {
            // Register a syntax receiver to find candidate structs
            context.RegisterForSyntaxNotifications(() => new SyncComponentReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not SyncComponentReceiver receiver)
                return;

            // Filter structs that actually have [SyncComponent] attribute
            var syncComponents = new List<(StructDeclarationSyntax syntax, INamedTypeSymbol symbol)>();
            foreach (var structDecl in receiver.CandidateStructs)
            {
                var model = context.Compilation.GetSemanticModel(structDecl.SyntaxTree);
                var symbol = model.GetDeclaredSymbol(structDecl) as INamedTypeSymbol;
                if (symbol == null) continue;

                if (HasAttribute(symbol, SyncComponentAttrName))
                    syncComponents.Add((structDecl, symbol));
            }

            if (syncComponents.Count == 0) return;

            // Assign ComponentTypeIds: use explicit override if set, otherwise auto-assign by name order
            AssignComponentTypeIds(syncComponents, context);

            // Generate code for each struct
            foreach (var (syntax, symbol) in syncComponents)
            {
                var source = GenerateComponentSyncCode(symbol, context);
                if (source != null)
                {
                    var hintName = $"{symbol.ContainingNamespace}_{symbol.Name}.g.cs";
                    context.AddSource(hintName, source);
                }
            }
        }

        // ==================== ID Assignment ====================

        private void AssignComponentTypeIds(
            List<(StructDeclarationSyntax syntax, INamedTypeSymbol symbol)> components,
            GeneratorExecutionContext context)
        {
            // First pass: collect explicit IDs
            var explicitIds = new HashSet<byte>();
            var pending = new List<(INamedTypeSymbol symbol, byte? id)>();

            foreach (var (syntax, symbol) in components)
            {
                var attr = symbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == SyncComponentAttrName);
                byte? explicitId = null;

                if (attr?.NamedArguments != null)
                {
                    foreach (var arg in attr.NamedArguments)
                    {
                        if (arg.Key == "ComponentTypeId" && arg.Value.Value is byte b && b > 0)
                        {
                            explicitId = b;
                            explicitIds.Add(b);
                            break;
                        }
                    }
                }

                pending.Add((symbol, explicitId));
            }

            // Sort: explicit IDs first, then by full name for auto-assign
            pending.Sort((a, b) =>
            {
                if (a.id.HasValue != b.id.HasValue)
                    return a.id.HasValue ? -1 : 1;
                return string.Compare(
                    a.symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    b.symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    StringComparison.Ordinal);
            });

            // Second pass: assign IDs
            byte nextId = 1;
            foreach (var (symbol, explicitId) in pending)
            {
                if (explicitId.HasValue)
                    continue; // Already has ID

                // Skip IDs that are explicitly taken
                while (explicitIds.Contains(nextId) && nextId < 255)
                    nextId++;

                if (nextId >= 255)
                {
                    // Report diagnostic: too many components
                    continue;
                }

                // Store the auto-assigned ID as a named argument on the attribute
                // We'll use a hack: store it in our own tracking dict
                _autoAssignedIds[symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = nextId;
                nextId++;
            }
        }

        private static readonly Dictionary<string, byte> _autoAssignedIds = new Dictionary<string, byte>();

        private static byte GetAssignedId(INamedTypeSymbol symbol)
        {
            // Check explicit override
            var attr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == SyncComponentAttrName);
            if (attr?.NamedArguments != null)
            {
                foreach (var arg in attr.NamedArguments)
                {
                    if (arg.Key == "ComponentTypeId" && arg.Value.Value is byte b && b > 0)
                        return b;
                }
            }

            // Check auto-assigned
            var key = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return _autoAssignedIds.TryGetValue(key, out var id) ? id : (byte)0;
        }

        // ==================== Code Generation ====================

        private static string? GenerateComponentSyncCode(INamedTypeSymbol symbol, GeneratorExecutionContext context)
        {
            byte componentTypeId = GetAssignedId(symbol);
            if (componentTypeId == 0)
            {
                // Report diagnostic
                return null;
            }

            var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? "Global";
            var structName = symbol.Name;
            var accessibility = symbol.DeclaredAccessibility.ToString().ToLower();

            // Find SyncVar fields
            var syncFields = new List<(IFieldSymbol field, string hookMethod)>();
            foreach (var member in symbol.GetMembers())
            {
                if (member is IFieldSymbol field && HasAttribute(field, SyncVarAttrName))
                {
                    string hookMethod = "";
                    var attr = field.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == SyncVarAttrName);
                    if (attr?.NamedArguments != null)
                    {
                        foreach (var arg in attr.NamedArguments)
                        {
                            if (arg.Key == "HookMethodName" && arg.Value.Value is string hook)
                                hookMethod = hook;
                        }
                    }
                    syncFields.Add((field, hookMethod));
                }
            }

            if (syncFields.Count == 0)
                return null;

            // Collect needed using statements
            var neededNamespaces = new HashSet<string>();
            foreach (var (field, _) in syncFields)
            {
                var typeName = field.Type.Name;
                if (typeName is "Vec2" or "Vec3" or "Quat")
                    neededNamespaces.Add("ShootingGame.Shared.Math");
            }
            neededNamespaces.Add("ShootingGame.Shared.Protocol");

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("using System;");

            foreach (var ns in neededNamespaces.OrderBy(n => n))
                sb.AppendLine($"using {ns};");

            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    {accessibility} partial struct {structName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        // ComponentTypeId (auto-assigned by Source Generator)");
            sb.AppendLine($"        public const byte ComponentTypeId = {componentTypeId};");
            sb.AppendLine();

            // Shadow fields for dirty detection
            sb.AppendLine("        // --- Shadow fields for dirty detection ---");
            foreach (var (field, _) in syncFields)
            {
                sb.AppendLine($"        private {field.Type.Name} __last_{field.Name};");
            }
            sb.AppendLine();

            // HasDelta properties
            sb.AppendLine("        // --- Per-field dirty flags ---");
            foreach (var (field, _) in syncFields)
            {
                sb.AppendLine($"        public bool HasDelta_{field.Name} => {field.Name} != __last_{field.Name};");
            }
            sb.AppendLine();

            // HasAnyDelta
            sb.AppendLine("        // --- Aggregate dirty flag ---");
            sb.Append("        public bool HasAnyDelta => ");
            for (int i = 0; i < syncFields.Count; i++)
            {
                if (i > 0) sb.Append(" || ");
                sb.Append($"HasDelta_{syncFields[i].field.Name}");
            }
            sb.AppendLine(";");
            sb.AppendLine();

            // WriteDelta
            sb.AppendLine("        // --- WriteDelta: only dirty fields (P帧) ---");
            sb.AppendLine("        public void WriteDelta(PacketWriter w)");
            sb.AppendLine("        {");
            sb.AppendLine($"            w.WriteByte(ComponentTypeId);");
            sb.AppendLine($"            w.WriteByte((byte){syncFields.Count}); // field count (unused but reserved)");
            sb.AppendLine($"            byte fieldMask = 0;");
            sb.AppendLine($"            int maskPos = w.Position;");
            sb.AppendLine($"            w.WriteByte(0); // placeholder for field mask");
            sb.AppendLine();
            for (int i = 0; i < syncFields.Count; i++)
            {
                var (field, _) = syncFields[i];
                var typeName = field.Type.Name;
                if (!_typeMap.TryGetValue(typeName, out var methods))
                    continue;

                sb.AppendLine($"            if (HasDelta_{field.Name})");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                fieldMask |= (byte)(1 << {i});");
                sb.AppendLine($"                w.{methods.write}({field.Name});");
                sb.AppendLine($"            }}");
                sb.AppendLine();
            }
            sb.AppendLine($"            // Write back field mask");
            sb.AppendLine($"            // (simplified: write mask at end for streaming simplicity)");
            sb.AppendLine($"            w.WriteByte(fieldMask);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // ReadDelta
            sb.AppendLine("        // --- ReadDelta: apply P帧 changes ---");
            sb.AppendLine("        public void ReadDelta(PacketReader r)");
            sb.AppendLine("        {");
            sb.AppendLine($"            // ComponentTypeId header already consumed by dispatcher");
            sb.AppendLine($"            r.ReadByte(); // reserved field count");
            sb.AppendLine($"            byte fieldMask = r.ReadByte();");
            sb.AppendLine();
            for (int i = 0; i < syncFields.Count; i++)
            {
                var (field, hook) = syncFields[i];
                var typeName = field.Type.Name;
                if (!_typeMap.TryGetValue(typeName, out var methods))
                    continue;

                sb.AppendLine($"            if ((fieldMask & (1 << {i})) != 0)");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                var old = {field.Name};");
                sb.AppendLine($"                {field.Name} = r.{methods.read}();");
                if (!string.IsNullOrEmpty(hook))
                    sb.AppendLine($"                // Hook: {hook}(old, {field.Name}) — call via reflection or generated dispatch");
                sb.AppendLine($"            }}");
                sb.AppendLine();
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            // WriteFull
            sb.AppendLine("        // --- WriteFull: all fields (I帧) ---");
            sb.AppendLine("        public void WriteFull(PacketWriter w)");
            sb.AppendLine("        {");
            sb.AppendLine($"            w.WriteByte(ComponentTypeId);");
            foreach (var (field, _) in syncFields)
            {
                var typeName = field.Type.Name;
                if (_typeMap.TryGetValue(typeName, out var methods))
                    sb.AppendLine($"            w.{methods.write}({field.Name});");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            // ReadFull
            sb.AppendLine("        // --- ReadFull: apply I帧 state ---");
            sb.AppendLine("        public void ReadFull(PacketReader r)");
            sb.AppendLine("        {");
            sb.AppendLine($"            // ComponentTypeId header already consumed by dispatcher");
            foreach (var (field, _) in syncFields)
            {
                var typeName = field.Type.Name;
                if (_typeMap.TryGetValue(typeName, out var methods))
                    sb.AppendLine($"            {field.Name} = r.{methods.read}();");
            }
            sb.AppendLine("            MarkClean();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // MarkClean
            sb.AppendLine("        // --- MarkClean: reset all dirty flags ---");
            sb.AppendLine("        public void MarkClean()");
            sb.AppendLine("        {");
            foreach (var (field, _) in syncFields)
            {
                sb.AppendLine($"            __last_{field.Name} = {field.Name};");
            }
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        // ==================== Helpers ====================

        private static bool HasAttribute(ISymbol symbol, string attrName)
        {
            return symbol.GetAttributes()
                .Any(a => a.AttributeClass?.Name == attrName ||
                          a.AttributeClass?.Name == attrName.Replace("Attribute", ""));
        }

        /// <summary>
        /// Syntax receiver: collects all struct declarations.
        /// Attribute filtering happens in Execute with semantic model.
        /// </summary>
        private class SyncComponentReceiver : ISyntaxReceiver
        {
            public readonly List<StructDeclarationSyntax> CandidateStructs = new List<StructDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is StructDeclarationSyntax structDecl &&
                    structDecl.AttributeLists.Count > 0)
                {
                    CandidateStructs.Add(structDecl);
                }
            }
        }
    }
}
