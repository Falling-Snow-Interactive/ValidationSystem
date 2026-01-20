using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace Fsi.Validation
{
    public static class BuiltInValidators
    {
        private static readonly Lazy<TypeIndex> CachedTypeIndex = new(BuildTypeIndex);

        [ValidationMethod]
        public static ValidatorResult CheckForUnusedUsings()
        {
            string[] guids = AssetDatabase.FindAssets("t:Script", new[] { "Assets/Scripts", "Assets/Tests" });
            int warnings = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (monoScript == null)
                {
                    continue;
                }

                string contents;
                try
                {
                    contents = File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to read {path}: {ex.Message}", monoScript);
                    continue;
                }

                IReadOnlyList<string> usings = ParseUsingDirectives(contents);
                if (usings.Count == 0)
                {
                    continue;
                }

                HashSet<string> identifiers = ExtractIdentifiers(contents);
                TypeIndex typeIndex = CachedTypeIndex.Value;

                foreach (string ns in usings)
                {
                    if (!typeIndex.NamespaceTypes.TryGetValue(ns, out HashSet<string> typeNames))
                    {
                        continue;
                    }

                    typeIndex.AttributeShortNames.TryGetValue(ns, out HashSet<string> attributeShortNames);

                    if (typeIndex.ExtensionNamespaces.Contains(ns))
                    {
                        continue;
                    }

                    bool used = typeNames.Any(typeName => identifiers.Contains(typeName));
                    if (!used && attributeShortNames != null)
                    {
                        used = attributeShortNames.Any(attributeName => identifiers.Contains(attributeName));
                    }
                    if (used)
                    {
                        continue;
                    }

                    warnings++;
                    Debug.LogWarning($"Unused using '{ns}' in {path}", monoScript);
                }
            }

            return warnings == 0
                ? ValidatorResult.Pass()
                : ValidatorResult.Fail($"Found {warnings} unused using directive(s). " +
                                       "This validator uses a conservative identifier scan when Roslyn is unavailable, " +
                                       "so it may miss some unused usings while avoiding false positives.");
        }

        private static IReadOnlyList<string> ParseUsingDirectives(string contents)
        {
            List<string> usings = new List<string>();
            bool inBlockComment = false;

            using (StringReader reader = new StringReader(contents))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();

                    if (inBlockComment)
                    {
                        int endIndex = trimmed.IndexOf("*/", StringComparison.Ordinal);
                        if (endIndex < 0)
                        {
                            continue;
                        }

                        trimmed = trimmed.Substring(endIndex + 2).Trim();
                        inBlockComment = false;
                        if (trimmed.Length == 0)
                        {
                            continue;
                        }
                    }

                    if (trimmed.StartsWith("/*", StringComparison.Ordinal))
                    {
                        int endIndex = trimmed.IndexOf("*/", StringComparison.Ordinal);
                        if (endIndex < 0)
                        {
                            inBlockComment = true;
                            continue;
                        }

                        trimmed = trimmed.Substring(endIndex + 2).Trim();
                        if (trimmed.Length == 0)
                        {
                            continue;
                        }
                    }

                    if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (trimmed.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!trimmed.StartsWith("using ", StringComparison.Ordinal))
                    {
                        break;
                    }

                    if (trimmed.StartsWith("using static ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int semicolonIndex = trimmed.IndexOf(';');
                    if (semicolonIndex < 0)
                    {
                        break;
                    }

                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex >= 0 && equalsIndex < semicolonIndex)
                    {
                        continue;
                    }

                    string ns = trimmed.Substring("using ".Length, semicolonIndex - "using ".Length).Trim();
                    if (ns.StartsWith("global::", StringComparison.Ordinal))
                    {
                        ns = ns.Substring("global::".Length);
                    }

                    if (!string.IsNullOrEmpty(ns))
                    {
                        usings.Add(ns);
                    }
                }
            }

            return usings;
        }

        private static HashSet<string> ExtractIdentifiers(string contents)
        {
            HashSet<string> identifiers = new HashSet<string>(StringComparer.Ordinal);
            int length = contents.Length;
            bool inLineComment = false;
            bool inBlockComment = false;
            bool inString = false;
            bool inVerbatimString = false;
            bool inChar = false;

            for (int i = 0; i < length; i++)
            {
                char c = contents[i];
                char next = i + 1 < length ? contents[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\n')
                    {
                        inLineComment = false;
                    }

                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }

                    continue;
                }

                if (inString)
                {
                    if (c == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (inVerbatimString)
                {
                    if (c == '"' && next == '"')
                    {
                        i++;
                        continue;
                    }

                    if (c == '"')
                    {
                        inVerbatimString = false;
                    }

                    continue;
                }

                if (inChar)
                {
                    if (c == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (c == '\'')
                    {
                        inChar = false;
                    }

                    continue;
                }

                if (c == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (c == '@' && next == '"')
                {
                    inVerbatimString = true;
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '\'')
                {
                    inChar = true;
                    continue;
                }

                if (c == '@' && (char.IsLetter(next) || next == '_'))
                {
                    int start = i + 1;
                    i = start;
                    while (i < length && (char.IsLetterOrDigit(contents[i]) || contents[i] == '_'))
                    {
                        i++;
                    }

                    identifiers.Add(contents.Substring(start, i - start));
                    i--;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    i++;
                    while (i < length && (char.IsLetterOrDigit(contents[i]) || contents[i] == '_'))
                    {
                        i++;
                    }

                    identifiers.Add(contents.Substring(start, i - start));
                    i--;
                }
            }

            return identifiers;
        }

        private static TypeIndex BuildTypeIndex()
        {
            Dictionary<string, HashSet<string>> namespaceTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> attributeShortNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            HashSet<string> extensionNamespaces = new HashSet<string>(StringComparer.Ordinal);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(type => type != null).ToArray();
                }

                foreach (Type type in types)
                {
                    if (type == null || string.IsNullOrEmpty(type.Namespace))
                    {
                        continue;
                    }

                    string ns = type.Namespace;
                    string typeName = type.Name;
                    int tickIndex = typeName.IndexOf('`');
                    if (tickIndex >= 0)
                    {
                        typeName = typeName.Substring(0, tickIndex);
                    }

                    if (!namespaceTypes.TryGetValue(ns, out HashSet<string> typeNames))
                    {
                        typeNames = new HashSet<string>(StringComparer.Ordinal);
                        namespaceTypes[ns] = typeNames;
                    }

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        typeNames.Add(typeName);
                    }

                    if (typeof(Attribute).IsAssignableFrom(type))
                    {
                        string attributeShortName = GetAttributeShortName(typeName);
                        if (!string.IsNullOrEmpty(attributeShortName))
                        {
                            if (!attributeShortNames.TryGetValue(ns, out HashSet<string> names))
                            {
                                names = new HashSet<string>(StringComparer.Ordinal);
                                attributeShortNames[ns] = names;
                            }

                            names.Add(attributeShortName);
                        }
                    }

                    if (!extensionNamespaces.Contains(ns) && IsExtensionContainer(type))
                    {
                        extensionNamespaces.Add(ns);
                    }
                }
            }

            return new TypeIndex(namespaceTypes, attributeShortNames, extensionNamespaces);
        }

        private static string GetAttributeShortName(string typeName)
        {
            const string suffix = "Attribute";
            if (typeName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return typeName.Substring(0, typeName.Length - suffix.Length);
            }

            return null;
        }

        private static bool IsExtensionContainer(Type type)
        {
            if (!type.IsSealed || !type.IsAbstract || !type.IsClass)
            {
                return false;
            }

            return type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(method => method.IsDefined(typeof(ExtensionAttribute), false));
        }

        private readonly struct TypeIndex
        {
            public TypeIndex(
                Dictionary<string, HashSet<string>> namespaceTypes,
                Dictionary<string, HashSet<string>> attributeShortNames,
                HashSet<string> extensionNamespaces)
            {
                NamespaceTypes = namespaceTypes;
                AttributeShortNames = attributeShortNames;
                ExtensionNamespaces = extensionNamespaces;
            }

            public Dictionary<string, HashSet<string>> NamespaceTypes { get; }
            public Dictionary<string, HashSet<string>> AttributeShortNames { get; }
            public HashSet<string> ExtensionNamespaces { get; }
        }
    }
}
