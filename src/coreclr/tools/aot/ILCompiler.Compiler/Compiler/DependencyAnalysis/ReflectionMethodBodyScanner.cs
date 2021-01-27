// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

using Internal.TypeSystem;

using AssemblyName = System.Reflection.AssemblyName;
using StringBuilder = System.Text.StringBuilder;

namespace ILCompiler.DependencyAnalysis
{
    internal static class ReflectionMethodBodyScanner
    {
        public static bool ResolveType(string name, ModuleDesc callingModule, TypeSystemContext context, out TypeDesc type, out ModuleDesc referenceModule)
        {
            // This can do enough resolution to resolve "Foo" or "Foo, Assembly, PublicKeyToken=...".
            // The reflection resolution rules are complicated. This is only needed for a heuristic,
            // not for correctness, so this shortcut is okay.

            type = null;
            referenceModule = null;

            int i = 0;

            // Consume type name part
            StringBuilder typeName = new StringBuilder();
            StringBuilder typeNamespace = new StringBuilder();
            while (i < name.Length && (char.IsLetterOrDigit(name[i]) || name[i] == '.' || name[i] == '`'))
            {
                if (name[i] == '.')
                {
                    if (typeNamespace.Length > 0)
                        typeNamespace.Append('.');
                    typeNamespace.Append(typeName);
                    typeName.Clear();
                }
                else
                {
                    typeName.Append(name[i]);
                }
                i++;
            }

            // Consume any comma or white space
            while (i < name.Length && (name[i] == ' ' || name[i] == ','))
            {
                i++;
            }

            // Consume assembly name
            StringBuilder assemblyName = new StringBuilder();
            while (i < name.Length && (char.IsLetterOrDigit(name[i]) || name[i] == '.'))
            {
                assemblyName.Append(name[i]);
                i++;
            }

            // If the name was assembly-qualified, resolve the assembly
            // If it wasn't qualified, we resolve in the calling assembly

            referenceModule = callingModule;
            if (assemblyName.Length > 0)
            {
                referenceModule = context.ResolveAssembly(new AssemblyName(assemblyName.ToString()), false);    
            }

            if (referenceModule == null)
                return false;

            // Resolve type in the assembly
            type = referenceModule.GetType(typeNamespace.ToString(), typeName.ToString(), false);
            
            // If it didn't resolve and wasn't assembly-qualified, we also try core library
            if (type == null && assemblyName.Length == 0)
            {
                referenceModule = context.SystemModule;
                type = referenceModule.GetType(typeNamespace.ToString(), typeName.ToString(), false);
            }
            
            return type != null;
        }
    }
}
