﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using Internal.IL;
using Internal.TypeSystem;

using BindingFlags = System.Reflection.BindingFlags;

namespace ILCompiler.Dataflow
{
    class ReflectionMethodBodyScanner : MethodBodyScanner
    {
        private readonly FlowAnnotations _flowAnnotations;
        private readonly Logger _logger;

        public static bool RequiresReflectionMethodBodyScannerForCallSite(FlowAnnotations flowAnnotations, MethodDesc methodDefinition)
        {
            return
                GetIntrinsicIdForMethod(methodDefinition) > IntrinsicId.RequiresReflectionBodyScanner_Sentinel ||
                flowAnnotations.RequiresDataflowAnalysis(methodDefinition) ||
                methodDefinition.HasCustomAttribute("System.Diagnostics.CodeAnalysis", "RequiresUnreferencedCodeAttribute");
        }

        public static bool RequiresReflectionMethodBodyScannerForMethodBody(FlowAnnotations flowAnnotations, MethodDesc methodDefinition)
        {
            return
                GetIntrinsicIdForMethod(methodDefinition) > IntrinsicId.RequiresReflectionBodyScanner_Sentinel ||
                flowAnnotations.RequiresDataflowAnalysis(methodDefinition);
        }

        public static bool RequiresReflectionMethodBodyScannerForAccess(FlowAnnotations flowAnnotations, FieldDesc fieldDefinition)
        {
            return flowAnnotations.RequiresDataflowAnalysis(fieldDefinition);
        }

        bool ShouldEnableReflectionPatternReporting(MethodDesc method)
        {
            return !method.HasCustomAttribute("System.Diagnostics.CodeAnalysis", "RequiresUnreferencedCodeAttribute");
        }

        public ReflectionMethodBodyScanner(FlowAnnotations flowAnnotations, Logger logger)
        {
            _flowAnnotations = flowAnnotations;
            _logger = logger;
        }

        public void ScanAndProcessReturnValue(MethodIL methodBody)
        {
            Scan(methodBody);

            if (!methodBody.OwningMethod.Signature.ReturnType.IsVoid)
            {
                var method = methodBody.OwningMethod;
                var requiredMemberTypes = _flowAnnotations.GetReturnParameterAnnotation(method);
                if (requiredMemberTypes != 0)
                {
                    var targetContext = new MethodOrigin(method);
                    var reflectionContext = new ReflectionPatternContext(_logger, ShouldEnableReflectionPatternReporting(method), method, targetContext);
                    reflectionContext.AnalyzingPattern();
                    RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, MethodReturnValue, targetContext);
                }
            }
        }

#if false
        // TODO: Data flow from custom attributes
        public void ProcessAttributeDataflow(IMemberDefinition source, MethodDefinition method, IList<CustomAttributeArgument> arguments)
        {
            int paramOffset = method.HasImplicitThis() ? 1 : 0;

            for (int i = 0; i < method.Parameters.Count; i++)
            {
                var annotation = _context.Annotations.FlowAnnotations.GetParameterAnnotation(method, i + paramOffset);
                if (annotation != DynamicallyAccessedMemberTypes.None)
                {
                    ValueNode valueNode = GetValueNodeForCustomAttributeArgument(arguments[i]);
                    if (valueNode != null)
                    {
                        var reflectionContext = new ReflectionPatternContext(_context, true, source, method.Parameters[i]);
                        reflectionContext.AnalyzingPattern();
                        RequireDynamicallyAccessedMembers(ref reflectionContext, annotation, valueNode, method);
                    }
                }
            }
        }

        public void ProcessAttributeDataflow(FieldDefinition field, CustomAttributeArgument value)
        {
            var annotation = _context.Annotations.FlowAnnotations.GetFieldAnnotation(field);
            Debug.Assert(annotation != DynamicallyAccessedMemberTypes.None);

            ValueNode valueNode = GetValueNodeForCustomAttributeArgument(value);
            if (valueNode != null)
            {
                var reflectionContext = new ReflectionPatternContext(_context, true, field.DeclaringType.Methods[0], field);
                reflectionContext.AnalyzingPattern();
                RequireDynamicallyAccessedMembers(ref reflectionContext, annotation, valueNode, field);
            }
        }

        static ValueNode GetValueNodeForCustomAttributeArgument(CustomAttributeArgument argument)
        {
            ValueNode valueNode;
            if (argument.Type.Name == "Type")
            {
                TypeDefinition referencedType = ((TypeReference)argument.Value).Resolve();
                valueNode = referencedType == null ? null : new SystemTypeValue(referencedType);
            }
            else if (argument.Type.MetadataType == MetadataType.String)
            {
                valueNode = new KnownStringValue((string)argument.Value);
            }
            else
            {
                // We shouldn't have gotten a non-null annotation for this from GetParameterAnnotation
                throw new InvalidOperationException();
            }

            return valueNode;
        }

        // TODO: data flow between generic things
        public void ProcessGenericArgumentDataFlow(GenericParameter genericParameter, TypeReference genericArgument, IMemberDefinition source)
        {
            var annotation = _context.Annotations.FlowAnnotations.GetGenericParameterAnnotation(genericParameter);
            Debug.Assert(annotation != DynamicallyAccessedMemberTypes.None);

            ValueNode valueNode = GetValueNodeFromGenericArgument(genericArgument);
            bool enableReflectionPatternReporting = !(source is MethodDefinition sourceMethod) || ShouldEnableReflectionPatternReporting(sourceMethod);

            var reflectionContext = new ReflectionPatternContext(_context, enableReflectionPatternReporting, source, genericParameter);
            reflectionContext.AnalyzingPattern();
            RequireDynamicallyAccessedMembers(ref reflectionContext, annotation, valueNode, genericParameter);
        }

        ValueNode GetValueNodeFromGenericArgument(TypeReference genericArgument)
        {
            if (genericArgument is GenericParameter inputGenericParameter)
            {
                // Technically this should be a new value node type as it's not a System.Type instance representation, but just the generic parameter
                // That said we only use it to perform the dynamically accessed members checks and for that purpose treating it as System.Type is perfectly valid.
                return new SystemTypeForGenericParameterValue(inputGenericParameter, _context.Annotations.FlowAnnotations.GetGenericParameterAnnotation(inputGenericParameter));
            }
            else
            {
                TypeDefinition genericArgumentTypeDef = genericArgument.Resolve();
                if (genericArgumentTypeDef != null)
                {
                    return new SystemTypeValue(genericArgumentTypeDef);
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
        }
#endif

        protected override void WarnAboutInvalidILInMethod(MethodIL method, int ilOffset)
        {
            // Serves as a debug helper to make sure valid IL is not considered invalid.
            //
            // The .NET Native compiler used to warn if it detected invalid IL during treeshaking,
            // but the warnings were often triggered in autogenerated dead code of a major game engine
            // and resulted in support calls. No point in warning. If the code gets exercised at runtime,
            // an InvalidProgramException will likely be raised.
            Debug.Fail("Invalid IL or a bug in the scanner");
        }

        protected override ValueNode GetMethodParameterValue(MethodDesc method, int parameterIndex)
        {
            DynamicallyAccessedMemberTypes memberTypes = _flowAnnotations.GetParameterAnnotation(method, parameterIndex);
            return new MethodParameterValue(method, parameterIndex, memberTypes);
        }

        protected override ValueNode GetFieldValue(MethodIL method, FieldDesc field)
        {
            switch (field.Name)
            {
                case "EmptyTypes" when field.OwningType.IsTypeOf("System", "Type"):
                    {
                        return new ArrayValue(new ConstIntValue(0));
                    }

                default:
                    {
                        DynamicallyAccessedMemberTypes memberTypes = _flowAnnotations.GetFieldAnnotation(field);
                        return new LoadFieldValue(field, memberTypes);
                    }
            }
        }

        protected override void HandleStoreField(MethodIL methodBody, int offset, FieldDesc field, ValueNode valueToStore)
        {
            var requiredMemberTypes = _flowAnnotations.GetFieldAnnotation(field);
            if (requiredMemberTypes != 0)
            {
                var origin = new FieldOrigin(field);
                var reflectionContext = new ReflectionPatternContext(_logger, ShouldEnableReflectionPatternReporting(methodBody.OwningMethod), methodBody.OwningMethod, origin, offset);
                reflectionContext.AnalyzingPattern();
                RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, valueToStore, origin);
            }
        }

        protected override void HandleStoreParameter(MethodIL method, int offset, int index, ValueNode valueToStore)
        {
            var requiredMemberTypes = _flowAnnotations.GetParameterAnnotation(method.OwningMethod, index);
            if (requiredMemberTypes != 0)
            {
                Origin parameter = DiagnosticUtilities.GetMethodParameterFromIndex(method.OwningMethod, index);
                var reflectionContext = new ReflectionPatternContext(_logger, ShouldEnableReflectionPatternReporting(method.OwningMethod), method.OwningMethod, parameter, offset);
                reflectionContext.AnalyzingPattern();
                RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, valueToStore, parameter);
            }
        }

        enum IntrinsicId
        {
            None = 0,
            IntrospectionExtensions_GetTypeInfo,
            Type_GetTypeFromHandle,
            Type_get_TypeHandle,
            Object_GetType,
            TypeDelegator_Ctor,
            Array_Empty,

            // Anything above this marker will require the method to be run through
            // the reflection body scanner.
            RequiresReflectionBodyScanner_Sentinel = 1000,
            Type_MakeGenericType,
            Type_GetType,
            Type_GetConstructor,
            Type_GetMethod,
            Type_GetField,
            Type_GetProperty,
            Type_GetEvent,
            Type_GetNestedType,
            Type_get_AssemblyQualifiedName,
            Expression_Call,
            Expression_Field,
            Expression_Property,
            Expression_New,
            Activator_CreateInstance_Type,
            Activator_CreateInstance_AssemblyName_TypeName,
            Activator_CreateInstanceFrom,
            Activator_CreateInstanceOfT,
            AppDomain_CreateInstance,
            AppDomain_CreateInstanceAndUnwrap,
            AppDomain_CreateInstanceFrom,
            AppDomain_CreateInstanceFromAndUnwrap,
            Assembly_CreateInstance,
            RuntimeReflectionExtensions_GetRuntimeEvent,
            RuntimeReflectionExtensions_GetRuntimeField,
            RuntimeReflectionExtensions_GetRuntimeMethod,
            RuntimeReflectionExtensions_GetRuntimeProperty,
            RuntimeHelpers_RunClassConstructor,
            MethodInfo_MakeGenericMethod,
        }

        static IntrinsicId GetIntrinsicIdForMethod(MethodDesc calledMethod)
        {
            return calledMethod.Name switch
            {
                // static System.Reflection.IntrospectionExtensions.GetTypeInfo (Type type)
                "GetTypeInfo" when calledMethod.IsDeclaredOnType("System.Reflection", "IntrospectionExtensions") => IntrinsicId.IntrospectionExtensions_GetTypeInfo,

                // System.Type.GetTypeInfo (Type type)
                "GetTypeFromHandle" when calledMethod.IsDeclaredOnType("System", "Type") => IntrinsicId.Type_GetTypeFromHandle,

                // System.Type.GetTypeHandle (Type type)
                "get_TypeHandle" when calledMethod.IsDeclaredOnType("System", "Type") => IntrinsicId.Type_get_TypeHandle,

                // static System.Type.MakeGenericType (Type [] typeArguments)
                "MakeGenericType" when calledMethod.IsDeclaredOnType("System", "Type") => IntrinsicId.Type_MakeGenericType,

                // static System.Reflection.RuntimeReflectionExtensions.GetRuntimeEvent (this Type type, string name)
                "GetRuntimeEvent" when calledMethod.IsDeclaredOnType("System.Reflection", "RuntimeReflectionExtensions")
                    && calledMethod.HasParameterOfType(0, "System", "Type")
                    && calledMethod.HasParameterOfType(1, "System", "String")
                    => IntrinsicId.RuntimeReflectionExtensions_GetRuntimeEvent,

                // static System.Reflection.RuntimeReflectionExtensions.GetRuntimeField (this Type type, string name)
                "GetRuntimeField" when calledMethod.IsDeclaredOnType("System.Reflection", "RuntimeReflectionExtensions")
                    && calledMethod.HasParameterOfType(0, "System", "Type")
                    && calledMethod.HasParameterOfType(1, "System", "String")
                    => IntrinsicId.RuntimeReflectionExtensions_GetRuntimeField,

                // static System.Reflection.RuntimeReflectionExtensions.GetRuntimeMethod (this Type type, string name, Type[] parameters)
                "GetRuntimeMethod" when calledMethod.IsDeclaredOnType("System.Reflection", "RuntimeReflectionExtensions")
                    && calledMethod.HasParameterOfType(0, "System", "Type")
                    && calledMethod.HasParameterOfType(1, "System", "String")
                    => IntrinsicId.RuntimeReflectionExtensions_GetRuntimeMethod,

                // static System.Reflection.RuntimeReflectionExtensions.GetRuntimeProperty (this Type type, string name)
                "GetRuntimeProperty" when calledMethod.IsDeclaredOnType("System.Reflection", "RuntimeReflectionExtensions")
                    && calledMethod.HasParameterOfType(0, "System", "Type")
                    && calledMethod.HasParameterOfType(1, "System", "String")
                    => IntrinsicId.RuntimeReflectionExtensions_GetRuntimeProperty,

                // static System.Linq.Expressions.Expression.Call (Type, String, Type[], Expression[])
                "Call" when calledMethod.IsDeclaredOnType("System.Linq.Expressions", "Expression")
                    && calledMethod.HasParameterOfType(0, "System", "Type")
                    && calledMethod.Signature.Length == 4
                    => IntrinsicId.Expression_Call,

                // static System.Linq.Expressions.Expression.Field (Expression, Type, String)
                "Field" when calledMethod.IsDeclaredOnType("System.Linq.Expressions", "Expression")
                    && calledMethod.HasParameterOfType(1, "System", "Type")
                    && calledMethod.Signature.Length == 3
                    => IntrinsicId.Expression_Field,

                // static System.Linq.Expressions.Expression.Property (Expression, Type, String)
                "Property" when calledMethod.IsDeclaredOnType("System.Linq.Expressions", "Expression")
                    && calledMethod.HasParameterOfType(1, "System", "Type")
                    && calledMethod.Signature.Length == 3
                    => IntrinsicId.Expression_Property,

                // static System.Linq.Expressions.Expression.New (Type)
                "New" when calledMethod.IsDeclaredOnType("System.Linq.Expressions", "Expression")
                    && calledMethod.HasParameterOfType(0, "System", "Type")
                    && calledMethod.Signature.Length == 1
                    => IntrinsicId.Expression_New,

                // static System.Type.GetType (string)
                // static System.Type.GetType (string, Boolean)
                // static System.Type.GetType (string, Boolean, Boolean)
                // static System.Type.GetType (string, Func<AssemblyName, Assembly>, Func<Assembly, String, Boolean, Type>)
                // static System.Type.GetType (string, Func<AssemblyName, Assembly>, Func<Assembly, String, Boolean, Type>, Boolean)
                // static System.Type.GetType (string, Func<AssemblyName, Assembly>, Func<Assembly, String, Boolean, Type>, Boolean, Boolean)
                "GetType" when calledMethod.IsDeclaredOnType("System", "Type")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    => IntrinsicId.Type_GetType,

                // System.Type.GetConstructor (Type[])
                // System.Type.GetConstructor (BindingFlags, Binder, Type[], ParameterModifier [])
                // System.Type.GetConstructor (BindingFlags, Binder, CallingConventions, Type[], ParameterModifier [])
                "GetConstructor" when calledMethod.IsDeclaredOnType("System", "Type")
                    && !calledMethod.Signature.IsStatic
                    => IntrinsicId.Type_GetConstructor,

                // System.Type.GetMethod (string)
                // System.Type.GetMethod (string, BindingFlags)
                // System.Type.GetMethod (string, Type[])
                // System.Type.GetMethod (string, Type[], ParameterModifier[])
                // System.Type.GetMethod (string, BindingFlags, Binder, Type[], ParameterModifier[]) 6
                // System.Type.GetMethod (string, BindingFlags, Binder, CallingConventions, Type[], ParameterModifier[]) 7
                // System.Type.GetMethod (string, int, Type[])
                // System.Type.GetMethod (string, int, Type[], ParameterModifier[]?)
                // System.Type.GetMethod (string, int, BindingFlags, Binder?, Type[], ParameterModifier[]?)
                // System.Type.GetMethod (string, int, BindingFlags, Binder?, CallingConventions, Type[], ParameterModifier[]?)
                "GetMethod" when calledMethod.IsDeclaredOnType("System", "Type")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && !calledMethod.Signature.IsStatic
                    => IntrinsicId.Type_GetMethod,

                // System.Type.GetField (string)
                // System.Type.GetField (string, BindingFlags)
                "GetField" when calledMethod.IsDeclaredOnType("System", "Type")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && !calledMethod.Signature.IsStatic
                    => IntrinsicId.Type_GetField,

                // System.Type.GetEvent (string)
                // System.Type.GetEvent (string, BindingFlags)
                "GetEvent" when calledMethod.IsDeclaredOnType("System", "Type")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && !calledMethod.Signature.IsStatic
                    => IntrinsicId.Type_GetEvent,

                // System.Type.GetNestedType (string)
                // System.Type.GetNestedType (string, BindingFlags)
                "GetNestedType" when calledMethod.IsDeclaredOnType("System", "Type")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && !calledMethod.Signature.IsStatic
                    => IntrinsicId.Type_GetNestedType,

                // System.Type.AssemblyQualifiedName
                "get_AssemblyQualifiedName" when calledMethod.IsDeclaredOnType("System", "Type")
                    && calledMethod.Signature.Length == 0
                    && !calledMethod.Signature.IsStatic
                    => IntrinsicId.Type_get_AssemblyQualifiedName,

                // System.Type.GetProperty (string)
                // System.Type.GetProperty (string, BindingFlags)
                // System.Type.GetProperty (string, Type)
                // System.Type.GetProperty (string, Type[])
                // System.Type.GetProperty (string, Type, Type[])
                // System.Type.GetProperty (string, Type, Type[], ParameterModifier[])
                // System.Type.GetProperty (string, BindingFlags, Binder, Type, Type[], ParameterModifier[])
                "GetProperty" when calledMethod.IsDeclaredOnType("System", "Type")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && !calledMethod.Signature.IsStatic
                    => IntrinsicId.Type_GetProperty,

                // static System.Object.GetType ()
                "GetType" when calledMethod.IsDeclaredOnType("System", "Object")
                    => IntrinsicId.Object_GetType,

                ".ctor" when calledMethod.IsDeclaredOnType("System.Reflection", "TypeDelegator")
                    && calledMethod.HasParameterOfType(0, "System", "Type")
                    => IntrinsicId.TypeDelegator_Ctor,

                "Empty" when calledMethod.IsDeclaredOnType("System", "Array")
                    => IntrinsicId.Array_Empty,

                // static System.Activator.CreateInstance (System.Type type)
                // static System.Activator.CreateInstance (System.Type type, bool nonPublic)
                // static System.Activator.CreateInstance (System.Type type, params object?[]? args)
                // static System.Activator.CreateInstance (System.Type type, object?[]? args, object?[]? activationAttributes)
                // static System.Activator.CreateInstance (System.Type type, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object?[]? args, System.Globalization.CultureInfo? culture)
                // static System.Activator.CreateInstance (System.Type type, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object?[]? args, System.Globalization.CultureInfo? culture, object?[]? activationAttributes) { throw null; }
                "CreateInstance" when calledMethod.IsDeclaredOnType("System", "Activator")
                    && !calledMethod.HasInstantiation
                    && calledMethod.HasParameterOfType(0, "System", "Type")
                    => IntrinsicId.Activator_CreateInstance_Type,

                // static System.Activator.CreateInstance (string assemblyName, string typeName)
                // static System.Activator.CreateInstance (string assemblyName, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object?[]? args, System.Globalization.CultureInfo? culture, object?[]? activationAttributes)
                // static System.Activator.CreateInstance (string assemblyName, string typeName, object?[]? activationAttributes)
                "CreateInstance" when calledMethod.IsDeclaredOnType("System", "Activator")
                    && !calledMethod.HasInstantiation
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && calledMethod.HasParameterOfType(1, "System", "String")
                    => IntrinsicId.Activator_CreateInstance_AssemblyName_TypeName,

                // static System.Activator.CreateInstanceFrom (string assemblyFile, string typeName)
                // static System.Activator.CreateInstanceFrom (string assemblyFile, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
                // static System.Activator.CreateInstanceFrom (string assemblyFile, string typeName, object? []? activationAttributes)
                "CreateInstanceFrom" when calledMethod.IsDeclaredOnType("System", "Activator")
                    && !calledMethod.HasInstantiation
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && calledMethod.HasParameterOfType(1, "System", "String")
                    => IntrinsicId.Activator_CreateInstanceFrom,

                // static T System.Activator.CreateInstance<T> ()
                "CreateInstance" when calledMethod.IsDeclaredOnType("System", "Activator")
                    && calledMethod.HasInstantiation
                    && calledMethod.Instantiation.Length == 1
                    && calledMethod.Signature.Length == 0
                    => IntrinsicId.Activator_CreateInstanceOfT,

                // System.AppDomain.CreateInstance (string assemblyName, string typeName)
                // System.AppDomain.CreateInstance (string assemblyName, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
                // System.AppDomain.CreateInstance (string assemblyName, string typeName, object? []? activationAttributes)
                "CreateInstance" when calledMethod.IsDeclaredOnType("System", "AppDomain")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && calledMethod.HasParameterOfType(1, "System", "String")
                    => IntrinsicId.AppDomain_CreateInstance,

                // System.AppDomain.CreateInstanceAndUnwrap (string assemblyName, string typeName)
                // System.AppDomain.CreateInstanceAndUnwrap (string assemblyName, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
                // System.AppDomain.CreateInstanceAndUnwrap (string assemblyName, string typeName, object? []? activationAttributes)
                "CreateInstanceAndUnwrap" when calledMethod.IsDeclaredOnType("System", "AppDomain")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && calledMethod.HasParameterOfType(1, "System", "String")
                    => IntrinsicId.AppDomain_CreateInstanceAndUnwrap,

                // System.AppDomain.CreateInstanceFrom (string assemblyFile, string typeName)
                // System.AppDomain.CreateInstanceFrom (string assemblyFile, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
                // System.AppDomain.CreateInstanceFrom (string assemblyFile, string typeName, object? []? activationAttributes)
                "CreateInstanceFrom" when calledMethod.IsDeclaredOnType("System", "AppDomain")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && calledMethod.HasParameterOfType(1, "System", "String")
                    => IntrinsicId.AppDomain_CreateInstanceFrom,

                // System.AppDomain.CreateInstanceFromAndUnwrap (string assemblyFile, string typeName)
                // System.AppDomain.CreateInstanceFromAndUnwrap (string assemblyFile, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
                // System.AppDomain.CreateInstanceFromAndUnwrap (string assemblyFile, string typeName, object? []? activationAttributes)
                "CreateInstanceFromAndUnwrap" when calledMethod.IsDeclaredOnType("System", "AppDomain")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    && calledMethod.HasParameterOfType(1, "System", "String")
                    => IntrinsicId.AppDomain_CreateInstanceFromAndUnwrap,

                // System.Reflection.Assembly.CreateInstance (string typeName)
                // System.Reflection.Assembly.CreateInstance (string typeName, bool ignoreCase)
                // System.Reflection.Assembly.CreateInstance (string typeName, bool ignoreCase, BindingFlags bindingAttr, Binder? binder, object []? args, CultureInfo? culture, object []? activationAttributes)
                "CreateInstance" when calledMethod.IsDeclaredOnType("System.Reflection", "Assembly")
                    && calledMethod.HasParameterOfType(0, "System", "String")
                    => IntrinsicId.Assembly_CreateInstance,

                // System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor (RuntimeTypeHandle type)
                "RunClassConstructor" when calledMethod.IsDeclaredOnType("System.Runtime.CompilerServices", "RuntimeHelpers")
                    && calledMethod.HasParameterOfType(0, "System", "RuntimeTypeHandle")
                    => IntrinsicId.RuntimeHelpers_RunClassConstructor,

                // System.Reflection.MethodInfo.MakeGenericMethod (Type[] typeArguments)
                "MakeGenericMethod" when calledMethod.IsDeclaredOnType("System.Reflection", "MethodInfo")
                    && !calledMethod.Signature.IsStatic
                    && calledMethod.Signature.Length == 1
                    => IntrinsicId.MethodInfo_MakeGenericMethod,

                _ => IntrinsicId.None,
            };
        }

        public override bool HandleCall(MethodIL callingMethodBody, MethodDesc calledMethod, ILOpcode operation, int offset, ValueNodeList methodParams, out ValueNode methodReturnValue)
        {
            methodReturnValue = null;

            var callingMethodDefinition = callingMethodBody.OwningMethod;
            bool shouldEnableReflectionWarnings = ShouldEnableReflectionPatternReporting(callingMethodDefinition);
            var reflectionContext = new ReflectionPatternContext(_logger, shouldEnableReflectionWarnings, callingMethodDefinition, new MethodOrigin(calledMethod), offset);

            DynamicallyAccessedMemberTypes returnValueDynamicallyAccessedMemberTypes = 0;

            try
            {

                bool requiresDataFlowAnalysis = _flowAnnotations.RequiresDataflowAnalysis(calledMethod);
                returnValueDynamicallyAccessedMemberTypes = requiresDataFlowAnalysis ?
                    _flowAnnotations.GetReturnParameterAnnotation(calledMethod) : 0;

                switch (GetIntrinsicIdForMethod(calledMethod))
                {
                    case IntrinsicId.IntrospectionExtensions_GetTypeInfo:
                        {
                            // typeof(Foo).GetTypeInfo()... will be commonly present in code targeting
                            // the dead-end reflection refactoring. The call doesn't do anything and we
                            // don't want to lose the annotation.
                            methodReturnValue = methodParams[0];
                        }
                        break;

                    case IntrinsicId.TypeDelegator_Ctor:
                        {
                            // This is an identity function for analysis purposes
                            if (operation == ILOpcode.newobj)
                                methodReturnValue = methodParams[1];
                        }
                        break;

                    case IntrinsicId.Array_Empty:
                        {
                            methodReturnValue = new ArrayValue(new ConstIntValue(0));
                        }
                        break;

                    case IntrinsicId.Type_GetTypeFromHandle:
                        {
                            // Infrastructure piece to support "typeof(Foo)"
                            if (methodParams[0] is RuntimeTypeHandleValue typeHandle)
                                methodReturnValue = new SystemTypeValue(typeHandle.TypeRepresented);
                            else if (methodParams[0] is RuntimeTypeHandleForGenericParameterValue typeHandleForGenericParameter)
                            {
                                methodReturnValue = new SystemTypeForGenericParameterValue(
                                    typeHandleForGenericParameter.GenericParameter,
                                    _flowAnnotations.GetGenericParameterAnnotation(typeHandleForGenericParameter.GenericParameter));
                            }
                        }
                        break;

                    case IntrinsicId.Type_get_TypeHandle:
                        {
                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is SystemTypeValue typeValue)
                                    methodReturnValue = MergePointValue.MergeValues(methodReturnValue, new RuntimeTypeHandleValue(typeValue.TypeRepresented));
                                else if (value == NullValue.Instance)
                                    methodReturnValue = MergePointValue.MergeValues(methodReturnValue, value);
                                else
                                    methodReturnValue = MergePointValue.MergeValues(methodReturnValue, UnknownValue.Instance);
                            }
                        }
                        break;

                    case IntrinsicId.Type_MakeGenericType:
                        {
                            reflectionContext.AnalyzingPattern();
                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is SystemTypeValue typeValue)
                                {
                                    foreach (GenericParameterDesc genericParameter in typeValue.TypeRepresented.GetTypeDefinition().Instantiation)
                                    {
                                        if (_flowAnnotations.GetGenericParameterAnnotation(genericParameter) != DynamicallyAccessedMemberTypes.None)
                                        {
                                            // There is a generic parameter which has some requirements on the input types.
                                            // For now we don't support tracking actual array elements, so we can't validate that the requirements are fulfilled.
                                            reflectionContext.RecordUnrecognizedPattern(
                                                2055,
                                                $"Call to '{calledMethod.GetDisplayName()}' can not be statically analyzed. " +
                                                $"It's not possible to guarantee the availability of requirements of the generic type.");
                                        }
                                    }

                                    // We haven't found any generic parameters with annotations, so there's nothing to validate.
                                    reflectionContext.RecordHandledPattern();
                                }
                                else if (value == NullValue.Instance)
                                    reflectionContext.RecordHandledPattern();
                                else
                                {
                                    // We have no way to "include more" to fix this if we don't know, so we have to warn
                                    reflectionContext.RecordUnrecognizedPattern(
                                        2055,
                                        $"Call to '{calledMethod.GetDisplayName()}' can not be statically analyzed. " +
                                        $"It's not possible to guarantee the availability of requirements of the generic type.");
                                }
                            }

                            // We don't want to lose track of the type
                            // in case this is e.g. Activator.CreateInstance(typeof(Foo<>).MakeGenericType(...));
                            methodReturnValue = methodParams[0];
                        }
                        break;

                    //
                    // System.Reflection.RuntimeReflectionExtensions
                    //
                    // static GetRuntimeEvent (this Type type, string name)
                    // static GetRuntimeField (this Type type, string name)
                    // static GetRuntimeMethod (this Type type, string name, Type[] parameters)
                    // static GetRuntimeProperty (this Type type, string name)
                    //
                    case var getRuntimeMember when getRuntimeMember == IntrinsicId.RuntimeReflectionExtensions_GetRuntimeEvent
                        || getRuntimeMember == IntrinsicId.RuntimeReflectionExtensions_GetRuntimeField
                        || getRuntimeMember == IntrinsicId.RuntimeReflectionExtensions_GetRuntimeMethod
                        || getRuntimeMember == IntrinsicId.RuntimeReflectionExtensions_GetRuntimeProperty:
                        {

                            reflectionContext.AnalyzingPattern();
                            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
                            DynamicallyAccessedMemberTypes requiredMemberTypes = getRuntimeMember switch
                            {
                                IntrinsicId.RuntimeReflectionExtensions_GetRuntimeEvent => DynamicallyAccessedMemberTypes.PublicEvents,
                                IntrinsicId.RuntimeReflectionExtensions_GetRuntimeField => DynamicallyAccessedMemberTypes.PublicFields,
                                IntrinsicId.RuntimeReflectionExtensions_GetRuntimeMethod => DynamicallyAccessedMemberTypes.PublicMethods,
                                IntrinsicId.RuntimeReflectionExtensions_GetRuntimeProperty => DynamicallyAccessedMemberTypes.PublicProperties,
                                _ => throw new Exception($"Reflection call '{calledMethod.GetDisplayName()}' inside '{callingMethodDefinition.GetDisplayName()}' is of unexpected member type."),
                            };

                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is SystemTypeValue systemTypeValue)
                                {
                                    foreach (var stringParam in methodParams[1].UniqueValues())
                                    {
                                        if (stringParam is KnownStringValue stringValue)
                                        {
                                            switch (getRuntimeMember)
                                            {
                                                case IntrinsicId.RuntimeReflectionExtensions_GetRuntimeEvent:
                                                    MarkEventsOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, e => e.Name == stringValue.Contents, bindingFlags);
                                                    reflectionContext.RecordHandledPattern();
                                                    break;
                                                case IntrinsicId.RuntimeReflectionExtensions_GetRuntimeField:
                                                    MarkFieldsOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, f => f.Name == stringValue.Contents, bindingFlags);
                                                    reflectionContext.RecordHandledPattern();
                                                    break;
                                                case IntrinsicId.RuntimeReflectionExtensions_GetRuntimeMethod:
                                                    MarkMethodsOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, m => m.Name == stringValue.Contents, bindingFlags);
                                                    reflectionContext.RecordHandledPattern();
                                                    break;
                                                case IntrinsicId.RuntimeReflectionExtensions_GetRuntimeProperty:
                                                    MarkPropertiesOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, p => p.Name == stringValue.Contents, bindingFlags);
                                                    reflectionContext.RecordHandledPattern();
                                                    break;
                                                default:
                                                    throw new Exception($"Error processing reflection call '{calledMethod.GetDisplayName()}' inside {callingMethodDefinition.GetDisplayName()}. Unexpected member kind.");
                                            }
                                        }
                                        else
                                        {
                                            RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, value, new ParameterOrigin(calledMethod, 0));
                                        }
                                    }
                                }
                                else
                                {
                                    RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, value, new ParameterOrigin(calledMethod, 0));
                                }
                            }
                        }
                        break;
                    //
                    // System.Linq.Expressions.Expression
                    // 
                    // static Call (Type, String, Type[], Expression[])
                    //
                    case IntrinsicId.Expression_Call:
                        {
                            reflectionContext.AnalyzingPattern();
                            BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is SystemTypeValue systemTypeValue)
                                {
                                    foreach (var stringParam in methodParams[1].UniqueValues())
                                    {
                                        // TODO: Change this as needed after deciding whether or not we are to keep
                                        // all methods on a type that was accessed via reflection.
                                        if (stringParam is KnownStringValue stringValue)
                                        {
                                            MarkMethodsOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, m => m.Name == stringValue.Contents, bindingFlags);
                                            reflectionContext.RecordHandledPattern();
                                        }
                                        else
                                        {
                                            RequireDynamicallyAccessedMembers(
                                                ref reflectionContext,
                                                GetDynamicallyAccessedMemberTypesFromBindingFlagsForMethods(bindingFlags),
                                                value,
                                                new ParameterOrigin(calledMethod, 0));
                                        }
                                    }
                                }
                                else
                                {
                                    RequireDynamicallyAccessedMembers(
                                        ref reflectionContext,
                                        GetDynamicallyAccessedMemberTypesFromBindingFlagsForMethods(bindingFlags),
                                        value,
                                        new ParameterOrigin(calledMethod, 0));
                                }
                            }
                        }
                        break;

                    //
                    // System.Linq.Expressions.Expression
                    // 
                    // static Field (Expression, Type, String)
                    // static Property (Expression, Type, String)
                    //
                    case var fieldOrPropertyInstrinsic when fieldOrPropertyInstrinsic == IntrinsicId.Expression_Field || fieldOrPropertyInstrinsic == IntrinsicId.Expression_Property:
                        {
                            reflectionContext.AnalyzingPattern();
                            DynamicallyAccessedMemberTypes memberTypes = fieldOrPropertyInstrinsic == IntrinsicId.Expression_Property
                                ? DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties
                                : DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields;

                            foreach (var value in methodParams[1].UniqueValues())
                            {
                                if (value is SystemTypeValue systemTypeValue)
                                {
                                    foreach (var stringParam in methodParams[2].UniqueValues())
                                    {
                                        if (stringParam is KnownStringValue stringValue)
                                        {
                                            BindingFlags bindingFlags = methodParams[0].Kind == ValueNodeKind.Null ? BindingFlags.Static : BindingFlags.Default;
                                            // TODO: Change this as needed after deciding if we are to keep all fields/properties on a type
                                            // that is accessed via reflection. For now, let's only keep the field/property that is retrieved.
                                            if (fieldOrPropertyInstrinsic == IntrinsicId.Expression_Property)
                                            {
                                                MarkPropertiesOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, filter: p => p.Name == stringValue.Contents, bindingFlags);
                                            }
                                            else
                                            {
                                                MarkFieldsOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, filter: f => f.Name == stringValue.Contents, bindingFlags);
                                            }

                                            reflectionContext.RecordHandledPattern();
                                        }
                                        else
                                        {
                                            RequireDynamicallyAccessedMembers(ref reflectionContext, memberTypes, value, new ParameterOrigin(calledMethod, 2));
                                        }
                                    }
                                }
                                else
                                {
                                    RequireDynamicallyAccessedMembers(ref reflectionContext, memberTypes, value, new ParameterOrigin(calledMethod, 1));
                                }
                            }
                        }
                        break;

                    //
                    // System.Linq.Expressions.Expression
                    // 
                    // static New (Type)
                    //
                    case IntrinsicId.Expression_New:
                        {
                            reflectionContext.AnalyzingPattern();

                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is SystemTypeValue systemTypeValue)
                                {
                                    MarkConstructorsOnType(ref reflectionContext, systemTypeValue.TypeRepresented, null, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    reflectionContext.RecordHandledPattern();
                                }
                                else
                                {
                                    RequireDynamicallyAccessedMembers(ref reflectionContext, DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, value, new ParameterOrigin(calledMethod, 0));
                                }
                            }
                        }
                        break;

                    //
                    // System.Object
                    // 
                    // GetType()
                    //
                    case IntrinsicId.Object_GetType:
                        {
                            // We could do better here if we start tracking the static types of values within the method body.
                            // Right now, this can only analyze a couple cases for which we have static information for.
                            TypeDesc staticType = null;
                            if (methodParams[0] is MethodParameterValue methodParam)
                            {
                                if (!callingMethodDefinition.Signature.IsStatic)
                                {
                                    if (methodParam.ParameterIndex == 0)
                                    {
                                        staticType = callingMethodDefinition.OwningType;
                                    }
                                    else
                                    {
                                        staticType = callingMethodDefinition.Signature[methodParam.ParameterIndex - 1];
                                    }
                                }
                                else
                                {
                                    staticType = callingMethodDefinition.Signature[methodParam.ParameterIndex];
                                }
                            }
                            else if (methodParams[0] is LoadFieldValue loadedField)
                            {
                                staticType = loadedField.Field.FieldType;
                            }

                            if (staticType != null)
                            {
                                // We can only analyze the Object.GetType call with the precise type if the type is sealed.
                                // The type could be a descendant of the type in question, making us miss reflection.
                                bool canUse = staticType is MetadataType mdType && mdType.IsSealed;

                                if (!canUse)
                                {
                                    // We can allow Object.GetType to be modeled as System.Delegate because we keep all methods
                                    // on delegates anyway so reflection on something this approximation would miss is actually safe.
                                    canUse = staticType.IsTypeOf("System", "Delegate");
                                }

                                if (canUse)
                                {
                                    methodReturnValue = new SystemTypeValue(staticType);
                                }
                            }
                        }
                        break;

                    //
                    // System.Type
                    //
                    // GetType (string)
                    // GetType (string, Boolean)
                    // GetType (string, Boolean, Boolean)
                    // GetType (string, Func<AssemblyName, Assembly>, Func<Assembly, String, Boolean, Type>)
                    // GetType (string, Func<AssemblyName, Assembly>, Func<Assembly, String, Boolean, Type>, Boolean)
                    // GetType (string, Func<AssemblyName, Assembly>, Func<Assembly, String, Boolean, Type>, Boolean, Boolean)
                    //
                    case IntrinsicId.Type_GetType:
                        {
                            reflectionContext.AnalyzingPattern();

                            var parameters = calledMethod.Signature;
                            if ((parameters.Length == 3 && (methodParams[2].Kind == ValueNodeKind.MethodReturn || methodParams[2].Kind == ValueNodeKind.ConstInt || methodParams[2].Kind == ValueNodeKind.LoadField)
                                && (methodParams[2].AsConstInt() == null || methodParams[2].AsConstInt() != 0)) ||
                                (parameters.Length == 5 && (methodParams[4].AsConstInt() == null || methodParams[4].AsConstInt() != 0)))
                            {
                                reflectionContext.RecordUnrecognizedPattern(2096, $"Call to '{calledMethod.GetDisplayName()}' can perform case insensitive lookup of the type, currently ILLink can not guarantee presence of all the matching types");
                                break;
                            }
                            foreach (var typeNameValue in methodParams[0].UniqueValues())
                            {
                                if (typeNameValue is KnownStringValue knownStringValue)
                                {
                                    bool found = ILCompiler.DependencyAnalysis.ReflectionMethodBodyScanner.ResolveType(knownStringValue.Contents, ((MetadataType)callingMethodDefinition.OwningType).Module,
                                        callingMethodDefinition.Context,
                                        out TypeDesc foundType, out _);
                                    if (!found)
                                    {
                                        // Intentionally ignore - it's not wrong for code to call Type.GetType on non-existing name, the code might expect null/exception back.
                                        reflectionContext.RecordHandledPattern();
                                    }
                                    else
                                    {
                                        reflectionContext.RecordRecognizedPattern(() => _logger.Writer.WriteLine($"Marking {foundType.GetDisplayName()}"));
                                        methodReturnValue = MergePointValue.MergeValues(methodReturnValue, new SystemTypeValue(foundType));
                                    }
                                }
                                else if (typeNameValue == NullValue.Instance)
                                {
                                    reflectionContext.RecordHandledPattern();
                                }
                                else if (typeNameValue is LeafValueWithDynamicallyAccessedMemberNode valueWithDynamicallyAccessedMember && valueWithDynamicallyAccessedMember.DynamicallyAccessedMemberTypes != 0)
                                {
                                    // Propagate the annotation from the type name to the return value. Annotation on a string value will be fullfilled whenever a value is assigned to the string with annotation.
                                    // So while we don't know which type it is, we can guarantee that it will fulfill the annotation.
                                    reflectionContext.RecordHandledPattern();
                                    methodReturnValue = MergePointValue.MergeValues(methodReturnValue, new MethodReturnValue(calledMethod, valueWithDynamicallyAccessedMember.DynamicallyAccessedMemberTypes));
                                }
                                else
                                {
                                    reflectionContext.RecordUnrecognizedPattern(2057, $"Unrecognized value passed to the parameter 'typeName' of method '{calledMethod.GetDisplayName()}'. It's not possible to guarantee the availability of the target type.");
                                }
                            }

                        }
                        break;

                    //
                    // GetConstructor (Type[])
                    // GetConstructor (BindingFlags, Binder, Type[], ParameterModifier [])
                    // GetConstructor (BindingFlags, Binder, CallingConventions, Type[], ParameterModifier [])
                    //
                    case IntrinsicId.Type_GetConstructor:
                        {
                            reflectionContext.AnalyzingPattern();

                            var parameters = calledMethod.Signature;
                            BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Instance;
                            if (parameters.Length > 1)
                            {
                                if (methodParams[1].AsConstInt() != null)
                                    bindingFlags |= (BindingFlags)methodParams[1].AsConstInt();
                            }
                            int? ctorParameterCount = parameters.Length switch
                            {
                                1 => (methodParams[1] as ArrayValue)?.Size.AsConstInt(),
                                2 => (methodParams[3] as ArrayValue)?.Size.AsConstInt(),
                                5 => (methodParams[4] as ArrayValue)?.Size.AsConstInt(),
                                _ => null,
                            };

                            // Go over all types we've seen
                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is SystemTypeValue systemTypeValue)
                                {
                                    MarkConstructorsOnType(ref reflectionContext, systemTypeValue.TypeRepresented, null, bindingFlags);
                                    reflectionContext.RecordHandledPattern();
                                }
                                else
                                {
                                    // Otherwise fall back to the bitfield requirements
                                    var requiredMemberTypes = bindingFlags.HasFlag(BindingFlags.Public) ? DynamicallyAccessedMemberTypes.PublicConstructors : DynamicallyAccessedMemberTypes.None;
                                    requiredMemberTypes |= bindingFlags.HasFlag(BindingFlags.NonPublic) ? DynamicallyAccessedMemberTypes.NonPublicConstructors : DynamicallyAccessedMemberTypes.None;
                                    // We can scope down the public constructors requirement if we know the number of parameters is 0
                                    if (requiredMemberTypes == DynamicallyAccessedMemberTypes.PublicConstructors && ctorParameterCount == 0)
                                        requiredMemberTypes = DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;
                                    RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, value, new MethodOrigin(calledMethod));
                                }
                            }
                        }
                        break;

                    //
                    // GetMethod (string)
                    // GetMethod (string, BindingFlags)
                    // GetMethod (string, Type[])
                    // GetMethod (string, Type[], ParameterModifier[])
                    // GetMethod (string, BindingFlags, Binder, Type[], ParameterModifier[]) 6
                    // GetMethod (string, BindingFlags, Binder, CallingConventions, Type[], ParameterModifier[]) 7
                    // GetMethod (string, int, Type[])
                    // GetMethod (string, int, Type[], ParameterModifier[]?)
                    // GetMethod (string, int, BindingFlags, Binder?, Type[], ParameterModifier[]?)
                    // GetMethod (string, int, BindingFlags, Binder?, CallingConventions, Type[], ParameterModifier[]?)
                    //
                    case IntrinsicId.Type_GetMethod:
                        {
                            reflectionContext.AnalyzingPattern();

                            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
                            if (calledMethod.Signature.Length > 1 && calledMethod.Signature[1].IsTypeOf("System.Reflection", "BindingFlags") && methodParams[2].AsConstInt() != null)
                            {
                                bindingFlags = (BindingFlags)methodParams[2].AsConstInt();
                            }
                            else if (calledMethod.Signature.Length > 2 && calledMethod.Signature[2].IsTypeOf("System.Reflection", "BindingFlags") && methodParams[3].AsConstInt() != null)
                            {
                                bindingFlags = (BindingFlags)methodParams[3].AsConstInt();
                            }

                            var requiredMemberTypes = GetDynamicallyAccessedMemberTypesFromBindingFlagsForMethods(bindingFlags);
                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is SystemTypeValue systemTypeValue)
                                {
                                    foreach (var stringParam in methodParams[1].UniqueValues())
                                    {
                                        if (stringParam is KnownStringValue stringValue)
                                        {
                                            if (BindingFlagsAreSupported(bindingFlags))
                                                RequireDynamicallyAccessedMembers(ref reflectionContext, DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods, value, new MethodOrigin(calledMethod));
                                            else
                                                MarkMethodsOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, m => m.Name == stringValue.Contents, bindingFlags);
                                            reflectionContext.RecordHandledPattern();
                                        }
                                        else
                                        {
                                            // Otherwise fall back to the bitfield requirements
                                            RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, value, new MethodOrigin(calledMethod));
                                        }
                                    }
                                }
                                else
                                {
                                    // Otherwise fall back to the bitfield requirements
                                    RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, value, new MethodOrigin(calledMethod));
                                }
                            }
                        }
                        break;

                    //
                    // GetNestedType (string)
                    // GetNestedType (string, BindingFlags)
                    //
                    case IntrinsicId.Type_GetNestedType:
                        {
                            reflectionContext.AnalyzingPattern();

                            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
                            if (calledMethod.Signature.Length > 1 && calledMethod.Signature[1].IsTypeOf("System.Reflection", "BindingFlags") && methodParams[2].AsConstInt() != null)
                            {
                                bindingFlags = (BindingFlags)methodParams[2].AsConstInt();
                            }

                            var requiredMemberTypes = GetDynamicallyAccessedMemberTypesFromBindingFlagsForNestedTypes(bindingFlags);
                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is SystemTypeValue systemTypeValue)
                                {
                                    foreach (var stringParam in methodParams[1].UniqueValues())
                                    {
                                        if (stringParam is KnownStringValue stringValue)
                                        {
                                            if (BindingFlagsAreSupported(bindingFlags))
                                                // We have chosen not to populate the methodReturnValue for now
                                                RequireDynamicallyAccessedMembers(ref reflectionContext, DynamicallyAccessedMemberTypes.PublicNestedTypes | DynamicallyAccessedMemberTypes.NonPublicNestedTypes, value, new MethodOrigin(calledMethod));
                                            else
                                            {
                                                MetadataType[] matchingNestedTypes = MarkNestedTypesOnType(ref reflectionContext, systemTypeValue.TypeRepresented, m => m.Name == stringValue.Contents, bindingFlags);

                                                if (matchingNestedTypes != null)
                                                {
                                                    for (int i = 0; i < matchingNestedTypes.Length; i++)
                                                        methodReturnValue = MergePointValue.MergeValues(methodReturnValue, new SystemTypeValue(matchingNestedTypes[i]));
                                                }
                                            }
                                            reflectionContext.RecordHandledPattern();
                                        }
                                        else
                                        {
                                            // Otherwise fall back to the bitfield requirements
                                            RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, value, new MethodOrigin(calledMethod));
                                        }
                                    }
                                }
                                else
                                {
                                    // Otherwise fall back to the bitfield requirements
                                    RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, value, new MethodOrigin(calledMethod));
                                }
                            }
                        }
                        break;

                    //
                    // AssemblyQualifiedName
                    //
                    case IntrinsicId.Type_get_AssemblyQualifiedName:
                        {

                            ValueNode transformedResult = null;
                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is LeafValueWithDynamicallyAccessedMemberNode dynamicallyAccessedThing)
                                {
                                    var annotatedString = new AnnotatedStringValue(dynamicallyAccessedThing.SourceContext, dynamicallyAccessedThing.DynamicallyAccessedMemberTypes);
                                    transformedResult = MergePointValue.MergeValues(transformedResult, annotatedString);
                                }
                                else
                                {
                                    transformedResult = null;
                                    break;
                                }
                            }

                            if (transformedResult != null)
                            {
                                methodReturnValue = transformedResult;
                            }
                        }
                        break;

                    //
                    // GetField (string)
                    // GetField (string, BindingFlags)
                    // GetEvent (string)
                    // GetEvent (string, BindingFlags)
                    // GetProperty (string)
                    // GetProperty (string, BindingFlags)
                    // GetProperty (string, Type)
                    // GetProperty (string, Type[])
                    // GetProperty (string, Type, Type[])
                    // GetProperty (string, Type, Type[], ParameterModifier[])
                    // GetProperty (string, BindingFlags, Binder, Type, Type[], ParameterModifier[])
                    //
                    case var fieldPropertyOrEvent when (fieldPropertyOrEvent == IntrinsicId.Type_GetField || fieldPropertyOrEvent == IntrinsicId.Type_GetProperty || fieldPropertyOrEvent == IntrinsicId.Type_GetEvent)
                        && calledMethod.IsDeclaredOnType("System", "Type")
                        && !calledMethod.Signature.IsStatic
                        && calledMethod.Signature[0].IsString:
                        {

                            reflectionContext.AnalyzingPattern();
                            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
                            if (calledMethod.Signature.Length > 1 && calledMethod.Signature[1].IsTypeOf("System.Reflection", "BindingFlags") && methodParams[2].AsConstInt() != null)
                            {
                                bindingFlags = (BindingFlags)methodParams[2].AsConstInt();
                            }

                            DynamicallyAccessedMemberTypes memberTypes = fieldPropertyOrEvent switch
                            {
                                IntrinsicId.Type_GetEvent => GetDynamicallyAccessedMemberTypesFromBindingFlagsForEvents(bindingFlags),
                                IntrinsicId.Type_GetField => GetDynamicallyAccessedMemberTypesFromBindingFlagsForFields(bindingFlags),
                                IntrinsicId.Type_GetProperty => GetDynamicallyAccessedMemberTypesFromBindingFlagsForProperties(bindingFlags),
                                _ => throw new ArgumentException($"Reflection call '{calledMethod.GetDisplayName()}' inside '{callingMethodDefinition.GetDisplayName()}' is of unexpected member type."),
                            };

                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is SystemTypeValue systemTypeValue)
                                {
                                    foreach (var stringParam in methodParams[1].UniqueValues())
                                    {
                                        if (stringParam is KnownStringValue stringValue)
                                        {
                                            switch (fieldPropertyOrEvent)
                                            {
                                                case IntrinsicId.Type_GetEvent:
                                                    if (BindingFlagsAreSupported(bindingFlags))
                                                        RequireDynamicallyAccessedMembers(ref reflectionContext, DynamicallyAccessedMemberTypes.PublicEvents | DynamicallyAccessedMemberTypes.NonPublicEvents, value, new MethodOrigin(calledMethod));
                                                    else
                                                        MarkEventsOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, filter: e => e.Name == stringValue.Contents, bindingFlags);
                                                    break;
                                                case IntrinsicId.Type_GetField:
                                                    if (BindingFlagsAreSupported(bindingFlags))
                                                        RequireDynamicallyAccessedMembers(ref reflectionContext, DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields, value, new MethodOrigin(calledMethod));
                                                    else
                                                        MarkFieldsOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, filter: f => f.Name == stringValue.Contents, bindingFlags);
                                                    break;
                                                case IntrinsicId.Type_GetProperty:
                                                    if (BindingFlagsAreSupported(bindingFlags))
                                                        RequireDynamicallyAccessedMembers(ref reflectionContext, DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties, value, new MethodOrigin(calledMethod));
                                                    else
                                                        MarkPropertiesOnTypeHierarchy(ref reflectionContext, systemTypeValue.TypeRepresented, filter: p => p.Name == stringValue.Contents, bindingFlags);
                                                    break;
                                                default:
                                                    Debug.Fail("Unreachable.");
                                                    break;
                                            }
                                            reflectionContext.RecordHandledPattern();
                                        }
                                        else
                                        {
                                            RequireDynamicallyAccessedMembers(ref reflectionContext, memberTypes, value, new MethodOrigin(calledMethod));
                                        }
                                    }
                                }
                                else
                                {
                                    RequireDynamicallyAccessedMembers(ref reflectionContext, memberTypes, value, new MethodOrigin(calledMethod));
                                }
                            }
                        }
                        break;

                    //
                    // System.Activator
                    // 
                    // static CreateInstance (System.Type type)
                    // static CreateInstance (System.Type type, bool nonPublic)
                    // static CreateInstance (System.Type type, params object?[]? args)
                    // static CreateInstance (System.Type type, object?[]? args, object?[]? activationAttributes)
                    // static CreateInstance (System.Type type, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object?[]? args, System.Globalization.CultureInfo? culture)
                    // static CreateInstance (System.Type type, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object?[]? args, System.Globalization.CultureInfo? culture, object?[]? activationAttributes) { throw null; }
                    //
                    case IntrinsicId.Activator_CreateInstance_Type:
                        {
                            var parameters = calledMethod.Signature;

                            reflectionContext.AnalyzingPattern();

                            int? ctorParameterCount = null;
                            BindingFlags bindingFlags = BindingFlags.Instance;
                            if (parameters.Length > 1)
                            {
                                if (parameters[1].IsWellKnownType(WellKnownType.Boolean))
                                {
                                    // The overload that takes a "nonPublic" bool
                                    bool nonPublic = true;
                                    if (methodParams[1] is ConstIntValue constInt)
                                    {
                                        nonPublic = constInt.Value != 0;
                                    }

                                    if (nonPublic)
                                        bindingFlags |= BindingFlags.NonPublic | BindingFlags.Public;
                                    else
                                        bindingFlags |= BindingFlags.Public;
                                    ctorParameterCount = 0;
                                }
                                else
                                {
                                    // Overload that has the parameters as the second or fourth argument
                                    int argsParam = parameters.Length == 2 || parameters.Length == 3 ? 1 : 3;

                                    if (methodParams.Count > argsParam &&
                                        methodParams[argsParam] is ArrayValue arrayValue &&
                                        arrayValue.Size.AsConstInt() != null)
                                    {
                                        ctorParameterCount = arrayValue.Size.AsConstInt();
                                    }

                                    if (parameters.Length > 3)
                                    {
                                        if (methodParams[1].AsConstInt() != null)
                                            bindingFlags |= (BindingFlags)methodParams[1].AsConstInt();
                                        else
                                            bindingFlags |= BindingFlags.NonPublic | BindingFlags.Public;
                                    }
                                    else
                                    {
                                        bindingFlags |= BindingFlags.Public;
                                    }
                                }
                            }
                            else
                            {
                                // The overload with a single System.Type argument
                                ctorParameterCount = 0;
                                bindingFlags |= BindingFlags.Public;
                            }

                            // Go over all types we've seen
                            foreach (var value in methodParams[0].UniqueValues())
                            {
                                if (value is SystemTypeValue systemTypeValue)
                                {
                                    // Special case known type values as we can do better by applying exact binding flags and parameter count.
                                    MarkConstructorsOnType(ref reflectionContext, systemTypeValue.TypeRepresented,
                                        ctorParameterCount == null ? (Func<MethodDesc, bool>)null : m => m.Signature.Length == ctorParameterCount, bindingFlags);
                                    reflectionContext.RecordHandledPattern();
                                }
                                else
                                {
                                    // Otherwise fall back to the bitfield requirements
                                    var requiredMemberTypes = ctorParameterCount == 0
                                        ? DynamicallyAccessedMemberTypes.PublicParameterlessConstructor
                                        : GetDynamicallyAccessedMemberTypesFromBindingFlagsForConstructors(bindingFlags);
                                    RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, value, new ParameterOrigin(calledMethod, 0));
                                }
                            }
                        }
                        break;

#if false
                    // TODO: niche APIs that we probably shouldn't even have added
                    //
                    // System.Activator
                    // 
                    // static CreateInstance (string assemblyName, string typeName)
                    // static CreateInstance (string assemblyName, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object?[]? args, System.Globalization.CultureInfo? culture, object?[]? activationAttributes)
                    // static CreateInstance (string assemblyName, string typeName, object?[]? activationAttributes)
                    //
                    case IntrinsicId.Activator_CreateInstance_AssemblyName_TypeName:
                        ProcessCreateInstanceByName(ref reflectionContext, calledMethod, methodParams);
                        break;

                    //
                    // System.Activator
                    // 
                    // static CreateInstanceFrom (string assemblyFile, string typeName)
                    // static CreateInstanceFrom (string assemblyFile, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
                    // static CreateInstanceFrom (string assemblyFile, string typeName, object? []? activationAttributes)
                    //
                    case IntrinsicId.Activator_CreateInstanceFrom:
                        ProcessCreateInstanceByName(ref reflectionContext, calledMethod, methodParams);
                        break;
#endif

#if false
                    // We probably don't need this because there's other places within the compiler that ensure this works.
                    //
                    // System.Activator
                    // 
                    // static T CreateInstance<T> ()
                    //
                    // Note: If the when condition returns false it would be an overload which we don't recognize, so just fall through to the default case
                    case IntrinsicId.Activator_CreateInstanceOfT when
                        calledMethod.Instantiation.Length == 1:
                        {
                            reflectionContext.AnalyzingPattern();

                            if (genericCalledMethod.GenericArguments[0] is GenericParameter genericParameter &&
                                genericParameter.HasDefaultConstructorConstraint)
                            {
                                // This is safe, the linker would have marked the default .ctor already
                                reflectionContext.RecordHandledPattern();
                                break;
                            }

                            RequireDynamicallyAccessedMembers(
                                ref reflectionContext,
                                DynamicallyAccessedMemberTypes.PublicParameterlessConstructor,
                                GetValueNodeFromGenericArgument(genericCalledMethod.GenericArguments[0]),
                                calledMethodDefinition.GenericParameters[0]);
                        }
                        break;
#endif

#if false
                    // TODO: niche APIs that we probably shouldn't even have added
                    //
                    // System.AppDomain
                    //
                    // CreateInstance (string assemblyName, string typeName)
                    // CreateInstance (string assemblyName, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
                    // CreateInstance (string assemblyName, string typeName, object? []? activationAttributes)
                    //
                    // CreateInstanceAndUnwrap (string assemblyName, string typeName)
                    // CreateInstanceAndUnwrap (string assemblyName, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
                    // CreateInstanceAndUnwrap (string assemblyName, string typeName, object? []? activationAttributes)
                    //
                    // CreateInstanceFrom (string assemblyFile, string typeName)
                    // CreateInstanceFrom (string assemblyFile, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
                    // CreateInstanceFrom (string assemblyFile, string typeName, object? []? activationAttributes)
                    //
                    // CreateInstanceFromAndUnwrap (string assemblyFile, string typeName)
                    // CreateInstanceFromAndUnwrap (string assemblyFile, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
                    // CreateInstanceFromAndUnwrap (string assemblyFile, string typeName, object? []? activationAttributes)
                    //
                    case var appDomainCreateInstance when appDomainCreateInstance == IntrinsicId.AppDomain_CreateInstance
                        || appDomainCreateInstance == IntrinsicId.AppDomain_CreateInstanceAndUnwrap
                        || appDomainCreateInstance == IntrinsicId.AppDomain_CreateInstanceFrom
                        || appDomainCreateInstance == IntrinsicId.AppDomain_CreateInstanceFromAndUnwrap:
                        ProcessCreateInstanceByName(ref reflectionContext, calledMethod, methodParams);
                        break;
#endif

                    //
                    // System.Reflection.Assembly
                    //
                    // CreateInstance (string typeName)
                    // CreateInstance (string typeName, bool ignoreCase)
                    // CreateInstance (string typeName, bool ignoreCase, BindingFlags bindingAttr, Binder? binder, object []? args, CultureInfo? culture, object []? activationAttributes)
                    //
                    case IntrinsicId.Assembly_CreateInstance:
                        //
                        // TODO: This could be supported for "this" only calls
                        //
                        reflectionContext.AnalyzingPattern();
                        reflectionContext.RecordUnrecognizedPattern(2058, $"Parameters passed to method '{calledMethod.GetDisplayName()}' cannot be analyzed. Consider using methods 'System.Type.GetType' and `System.Activator.CreateInstance` instead.");
                        break;

                    //
                    // System.Runtime.CompilerServices.RuntimeHelpers
                    //
                    // RunClassConstructor (RuntimeTypeHandle type)
                    //
                    case IntrinsicId.RuntimeHelpers_RunClassConstructor:
                        {
                            reflectionContext.AnalyzingPattern();
                            foreach (var typeHandleValue in methodParams[0].UniqueValues())
                            {
                                if (typeHandleValue is RuntimeTypeHandleValue runtimeTypeHandleValue)
                                {
                                    _logger.Writer.WriteLine($"Marking static constructor of {runtimeTypeHandleValue.TypeRepresented.GetDisplayName()}");
                                    reflectionContext.RecordHandledPattern();
                                }
                                else if (typeHandleValue == NullValue.Instance)
                                    reflectionContext.RecordHandledPattern();
                                else
                                {
                                    reflectionContext.RecordUnrecognizedPattern(2059, $"Unrecognized value passed to the parameter 'type' of method '{calledMethod.GetDisplayName()}'. It's not possible to guarantee the availability of the target static constructor.");
                                }
                            }
                        }
                        break;

                    //
                    // System.Reflection.MethodInfo
                    //
                    // MakeGenericMethod (Type[] typeArguments)
                    //
                    case IntrinsicId.MethodInfo_MakeGenericMethod:
                        {
                            reflectionContext.AnalyzingPattern();

                            // We don't track MethodInfo values, so we can't determine if the MakeGenericMethod is problematic or not.
                            // Since some of the generic parameters may have annotations, all calls are potentially dangerous.
                            reflectionContext.RecordUnrecognizedPattern(2060, $"Call to `{calledMethod.GetDisplayName()}` can not be statically analyzed. It's not possible to guarantee the availability of requirements of the generic method.");
                        }
                        break;

                    default:
                        if (requiresDataFlowAnalysis)
                        {
                            reflectionContext.AnalyzingPattern();
                            for (int parameterIndex = 0; parameterIndex < methodParams.Count; parameterIndex++)
                            {
                                var requiredMemberTypes = _flowAnnotations.GetParameterAnnotation(calledMethod, parameterIndex);
                                if (requiredMemberTypes != 0)
                                {
                                    Origin targetContext = DiagnosticUtilities.GetMethodParameterFromIndex(calledMethod, parameterIndex);
                                    RequireDynamicallyAccessedMembers(ref reflectionContext, requiredMemberTypes, methodParams[parameterIndex], targetContext);
                                }
                            }

                            reflectionContext.RecordHandledPattern();
                        }

                        if (shouldEnableReflectionWarnings &&
                            calledMethod.HasCustomAttribute("System.Diagnostics.CodeAnalysis", "RequiresUnreferencedCodeAttribute"))
                        {
                            string message =
                                $"Calling '{calledMethod.GetDisplayName()}' which has `RequiresUnreferencedCodeAttribute` can break functionality when trimming application code.";// +
                                // TODO
                                //$"{requiresUnreferencedCode.Message}.";

                            //if (requiresUnreferencedCode.Url != null)
                            //{
                            //    message += " " + requiresUnreferencedCode.Url;
                            //}

                            _logger.LogWarning(message, 2026, callingMethodDefinition, offset, MessageSubCategory.TrimAnalysis);
                        }

                        // To get good reporting of errors we need to track the origin of the value for all method calls
                        // but except Newobj as those are special.
                        if (!calledMethod.Signature.ReturnType.IsVoid)
                        {
                            methodReturnValue = new MethodReturnValue(calledMethod, returnValueDynamicallyAccessedMemberTypes);

                            return true;
                        }

                        return false;
                }
            }
            finally
            {
                reflectionContext.Dispose();
            }

            // If we get here, we handled this as an intrinsic.  As a convenience, if the code above
            // didn't set the return value (and the method has a return value), we will set it to be an
            // unknown value with the return type of the method.
            if (methodReturnValue == null)
            {
                if (!calledMethod.Signature.ReturnType.IsVoid)
                {
                    methodReturnValue = new MethodReturnValue(calledMethod, returnValueDynamicallyAccessedMemberTypes);
                }
            }

            // Validate that the return value has the correct annotations as per the method return value annotations
            if (returnValueDynamicallyAccessedMemberTypes != 0 && methodReturnValue != null)
            {
                if (methodReturnValue is LeafValueWithDynamicallyAccessedMemberNode methodReturnValueWithMemberTypes)
                {
                    if (!methodReturnValueWithMemberTypes.DynamicallyAccessedMemberTypes.HasFlag(returnValueDynamicallyAccessedMemberTypes))
                        throw new InvalidOperationException($"Internal linker error: processing of call from {callingMethodDefinition.GetDisplayName()} to {calledMethod.GetDisplayName()} returned value which is not correctly annotated with the expected dynamic member access kinds.");
                }
                else if (methodReturnValue is SystemTypeValue)
                {
                    // SystemTypeValue can fullfill any requirement, so it's always valid
                    // The requirements will be applied at the point where it's consumed (passed as a method parameter, set as field value, returned from the method)
                }
                else
                {
                    throw new InvalidOperationException($"Internal linker error: processing of call from {callingMethodDefinition.GetDisplayName()} to {calledMethod.GetDisplayName()} returned value which is not correctly annotated with the expected dynamic member access kinds.");
                }
            }

            return true;
        }

#if false
        void ProcessCreateInstanceByName(ref ReflectionPatternContext reflectionContext, MethodDesc calledMethod, ValueNodeList methodParams)
        {
            reflectionContext.AnalyzingPattern();

            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            bool parameterlessConstructor = true;
            if (calledMethod.Signature.Length == 8 && calledMethod.Signature[2].IsWellKnownType(WellKnownType.Boolean) &&
                methodParams[3].AsConstInt() != null)
            {
                parameterlessConstructor = false;
                bindingFlags = BindingFlags.Instance | (BindingFlags)methodParams[3].AsConstInt();
            }

            int methodParamsOffset = !calledMethod.Signature.IsStatic ? 1 : 0;

            foreach (var assemblyNameValue in methodParams[methodParamsOffset].UniqueValues())
            {
                if (assemblyNameValue is KnownStringValue assemblyNameStringValue)
                {
                    foreach (var typeNameValue in methodParams[methodParamsOffset + 1].UniqueValues())
                    {
                        if (typeNameValue is KnownStringValue typeNameStringValue)
                        {
                            var resolvedAssembly = _context.GetLoadedAssembly(assemblyNameStringValue.Contents);
                            if (resolvedAssembly == null)
                            {
                                reflectionContext.RecordUnrecognizedPattern(2061, $"The assembly name '{assemblyNameStringValue.Contents}' passed to method '{calledMethod.GetDisplayName()}' references assembly which is not available.");
                                continue;
                            }

                            var resolvedType = _context.TypeNameResolver.ResolveTypeName(resolvedAssembly, typeNameStringValue.Contents)?.Resolve();
                            if (resolvedType == null)
                            {
                                // It's not wrong to have a reference to non-existing type - the code may well expect to get an exception in this case
                                // Note that we did find the assembly, so it's not a linker config problem, it's either intentional, or wrong versions of assemblies
                                // but linker can't know that.
                                reflectionContext.RecordHandledPattern();
                                continue;
                            }

                            MarkConstructorsOnType(ref reflectionContext, resolvedType, parameterlessConstructor ? m => m.Parameters.Count == 0 : (Func<MethodDesc, bool>)null, bindingFlags);
                        }
                        else
                        {
                            reflectionContext.RecordUnrecognizedPattern(2032, $"Unrecognized value passed to the parameter '{calledMethod.Parameters[1].Name}' of method '{calledMethod.GetDisplayName()}'. It's not possible to guarantee the availability of the target type.");
                        }
                    }
                }
                else
                {
                    reflectionContext.RecordUnrecognizedPattern(2032, $"Unrecognized value passed to the parameter '{calledMethod.Parameters[0].Name}' of method '{calledMethod.GetDisplayName()}'. It's not possible to guarantee the availability of the target type.");
                }
            }
        }
#endif

        void RequireDynamicallyAccessedMembers(ref ReflectionPatternContext reflectionContext, DynamicallyAccessedMemberTypes requiredMemberTypes, ValueNode value, Origin targetContext)
        {
            foreach (var uniqueValue in value.UniqueValues())
            {
                if (requiredMemberTypes == DynamicallyAccessedMemberTypes.PublicParameterlessConstructor
                    && uniqueValue is SystemTypeForGenericParameterValue genericParam
                    && genericParam.GenericParameter.HasDefaultConstructorConstraint)
                {
                    // We allow a new() constraint on a generic parameter to satisfy DynamicallyAccessedMemberTypes.PublicParameterlessConstructor
                    reflectionContext.RecordHandledPattern();
                }
                else if (uniqueValue is LeafValueWithDynamicallyAccessedMemberNode valueWithDynamicallyAccessedMember)
                {
                    if (!valueWithDynamicallyAccessedMember.DynamicallyAccessedMemberTypes.HasFlag(requiredMemberTypes))
                    {
                        switch ((valueWithDynamicallyAccessedMember.SourceContext, targetContext))
                        {
                            case (ParameterOrigin sourceParameter, ParameterOrigin targetParameter):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2067,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(sourceParameter)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(sourceParameter.Method)}' " +
                                    $"don't match those on the parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(targetParameter)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(targetParameter.Method)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (ParameterOrigin sourceParameter, MethodReturnOrigin targetMethodReturnType):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2068,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(sourceParameter)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(sourceParameter.Method)}' " +
                                    $"don't match those on the return value of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(targetMethodReturnType.Method)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (ParameterOrigin sourceParameter, FieldOrigin targetField):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2069,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(sourceParameter)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(sourceParameter.Method)}' " +
                                    $"don't match those on the field '{targetField.GetDisplayName()}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (ParameterOrigin sourceParameter, MethodOrigin targetMethod):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2070,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(sourceParameter)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(sourceParameter.Method)}' " +
                                    $"don't match those on the implicit 'this' parameter of method '{targetMethod.GetDisplayName()}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (ParameterOrigin sourceParameter, GenericParameterOrigin targetGenericParameter):
                                // Currently this is never generated, once ILLink supports full analysis of MakeGenericType/MakeGenericMethod this will be used
                                reflectionContext.RecordUnrecognizedPattern(
                                    2071,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(sourceParameter)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(sourceParameter.Method)}' " +
                                    $"don't match those on the generic parameter '{targetGenericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(targetGenericParameter)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;

                            case (MethodReturnOrigin sourceMethodReturnType, ParameterOrigin targetParameter):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2072,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the return value of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(sourceMethodReturnType.Method)}' " +
                                    $"don't match those on the parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(targetParameter)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(targetParameter.Method)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (MethodReturnOrigin sourceMethodReturnType, MethodReturnOrigin targetMethodReturnType):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2073,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the return value of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(sourceMethodReturnType.Method)}' " +
                                    $"don't match those on the return value of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(targetMethodReturnType.Method)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (MethodReturnOrigin sourceMethodReturnType, FieldOrigin targetField):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2074,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the return value of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(sourceMethodReturnType.Method)}' " +
                                    $"don't match those on the field '{targetField.GetDisplayName()}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (MethodReturnOrigin sourceMethodReturnType, MethodOrigin targetMethod):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2075,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the return value of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(sourceMethodReturnType.Method)}' " +
                                    $"don't match those on the implicit 'this' parameter of method '{targetMethod.GetDisplayName()}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (MethodReturnOrigin sourceMethodReturnType, GenericParameterOrigin targetGenericParameter):
                                // Currently this is never generated, once ILLink supports full analysis of MakeGenericType/MakeGenericMethod this will be used
                                reflectionContext.RecordUnrecognizedPattern(
                                    2076,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the return value of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(sourceMethodReturnType.Method)}' " +
                                    $"don't match those on the generic parameter '{targetGenericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(targetGenericParameter)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;

                            case (FieldOrigin sourceField, ParameterOrigin targetParameter):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2077,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the field '{sourceField.GetDisplayName()}' " +
                                    $"don't match those on the parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(targetParameter)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(targetParameter.Method)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (FieldOrigin sourceField, MethodReturnOrigin targetMethodReturnType):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2078,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the field '{sourceField.GetDisplayName()}' " +
                                    $"don't match those on the return value of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(targetMethodReturnType.Method)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (FieldOrigin sourceField, FieldOrigin targetField):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2079,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the field '{sourceField.GetDisplayName()}' " +
                                    $"don't match those on the field '{targetField.GetDisplayName()}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (FieldOrigin sourceField, MethodOrigin targetMethod):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2080,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the field '{sourceField.GetDisplayName()}' " +
                                    $"don't match those on the implicit 'this' parameter of method '{targetMethod.GetDisplayName()}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (FieldOrigin sourceField, GenericParameterOrigin targetGenericParameter):
                                // Currently this is never generated, once ILLink supports full analysis of MakeGenericType/MakeGenericMethod this will be used
                                reflectionContext.RecordUnrecognizedPattern(
                                    2081,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the field '{sourceField.GetDisplayName()}' " +
                                    $"don't match those on the generic parameter '{targetGenericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(targetGenericParameter)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;

                            case (MethodOrigin sourceMethod, ParameterOrigin targetParameter):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2082,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the implicit 'this' parameter of method '{sourceMethod.GetDisplayName()}' " +
                                    $"don't match those on the parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(targetParameter)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(targetParameter.Method)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (MethodOrigin sourceMethod, MethodReturnOrigin targetMethodReturnType):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2083,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the implicit 'this' parameter of method '{sourceMethod.GetDisplayName()}' " +
                                    $"don't match those on the return value of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(targetMethodReturnType.Method)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (MethodOrigin sourceMethod, FieldOrigin targetField):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2084,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the implicit 'this' parameter of method '{sourceMethod.GetDisplayName()}' " +
                                    $"don't match those on the field '{targetField.GetDisplayName()}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (MethodOrigin sourceMethod, MethodOrigin targetMethod):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2085,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the implicit 'this' parameter of method '{sourceMethod.GetDisplayName()}' " +
                                    $"don't match those on the implicit 'this' parameter of method '{targetMethod.GetDisplayName()}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (MethodOrigin sourceMethod, GenericParameterOrigin targetGenericParameter):
                                // Currently this is never generated, once ILLink supports full analysis of MakeGenericType/MakeGenericMethod this will be used
                                reflectionContext.RecordUnrecognizedPattern(
                                    2086,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the implicit 'this' parameter of method '{sourceMethod.GetDisplayName()}' " +
                                    $"don't match those on the generic parameter '{targetGenericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(targetGenericParameter)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;

                            case (GenericParameterOrigin sourceGenericParameter, ParameterOrigin targetParameter):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2087,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the generic parameter '{sourceGenericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(sourceGenericParameter)}' " +
                                    $"don't match those on the parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(targetParameter)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(targetParameter.Method)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (GenericParameterOrigin sourceGenericParameter, MethodReturnOrigin targetMethodReturnType):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2088,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the generic parameter '{sourceGenericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(sourceGenericParameter)}' " +
                                    $"don't match those on the return value of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(targetMethodReturnType.Method)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (GenericParameterOrigin sourceGenericParameter, FieldOrigin targetField):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2089,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the generic parameter '{sourceGenericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(sourceGenericParameter)}' " +
                                    $"don't match those on the field '{targetField.GetDisplayName()}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (GenericParameterOrigin sourceGenericParameter, MethodOrigin targetMethod):
                                // Currently this is never generated, it might be possible one day if we try to validate annotations on results of reflection
                                // For example code like this should ideally one day generate the warning
                                // void TestMethod<T>()
                                // {
                                //    // This passes the T as the "this" parameter to Type.GetMethods()
                                //    typeof(Type).GetMethod("GetMethods").Invoke(typeof(T), new object[] {});
                                // }
                                reflectionContext.RecordUnrecognizedPattern(
                                    2090,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the generic parameter '{sourceGenericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(sourceGenericParameter)}' " +
                                    $"don't match those on the implicit 'this' parameter of method '{targetMethod.GetDisplayName()}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;
                            case (GenericParameterOrigin sourceGenericParameter, GenericParameterOrigin targetGenericParameter):
                                reflectionContext.RecordUnrecognizedPattern(
                                    2091,
                                    $"The requirements declared via the 'DynamicallyAccessedMembersAttribute' on the generic parameter '{sourceGenericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(sourceGenericParameter)}' " +
                                    $"don't match those on the generic parameter '{targetGenericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(targetGenericParameter)}'. " +
                                    $"The source value must declare at least the same requirements as those declared on the target location it's assigned to");
                                break;

                            default:
                                throw new NotImplementedException($"unsupported source context {valueWithDynamicallyAccessedMember.SourceContext} or target context {targetContext}");
                        };
                    }
                    else
                    {
                        reflectionContext.RecordHandledPattern();
                    }
                }
                else if (uniqueValue is SystemTypeValue systemTypeValue)
                {
                    MarkTypeForDynamicallyAccessedMembers(ref reflectionContext, systemTypeValue.TypeRepresented, requiredMemberTypes);
                }
                else if (uniqueValue is KnownStringValue knownStringValue)
                {
                    ModuleDesc callingModule = ((reflectionContext.Source as MethodDesc)?.OwningType as MetadataType)?.Module;

                    if (!ILCompiler.DependencyAnalysis.ReflectionMethodBodyScanner.ResolveType(knownStringValue.Contents, callingModule, reflectionContext.Source.Context, out TypeDesc foundType, out _))
                    {
                        // Intentionally ignore - it's not wrong for code to call Type.GetType on non-existing name, the code might expect null/exception back.
                        reflectionContext.RecordHandledPattern();
                    }
                    else
                    {
                        MarkTypeForDynamicallyAccessedMembers(ref reflectionContext, foundType, requiredMemberTypes);
                    }
                }
                else if (uniqueValue == NullValue.Instance)
                {
                    // Ignore - probably unreachable path as it would fail at runtime anyway.
                }
                else
                {
                    switch (targetContext)
                    {
                        case ParameterOrigin parameterDefinition:
                            reflectionContext.RecordUnrecognizedPattern(
                                2062,
                                $"Value passed to parameter '{DiagnosticUtilities.GetParameterNameForErrorMessage(parameterDefinition)}' of method '{DiagnosticUtilities.GetMethodSignatureDisplayName(parameterDefinition.Method)}' can not be statically determined and may not meet 'DynamicallyAccessedMembersAttribute' requirements.");
                            break;
                        case MethodReturnOrigin methodReturnType:
                            reflectionContext.RecordUnrecognizedPattern(
                                2063,
                                $"Value returned from method '{DiagnosticUtilities.GetMethodSignatureDisplayName(methodReturnType.Method)}' can not be statically determined and may not meet 'DynamicallyAccessedMembersAttribute' requirements.");
                            break;
                        case FieldOrigin fieldDefinition:
                            reflectionContext.RecordUnrecognizedPattern(
                                2064,
                                $"Value assigned to {fieldDefinition.GetDisplayName()} can not be statically determined and may not meet 'DynamicallyAccessedMembersAttribute' requirements.");
                            break;
                        case MethodOrigin methodDefinition:
                            reflectionContext.RecordUnrecognizedPattern(
                                2065,
                                $"Value passed to implicit 'this' parameter of method '{methodDefinition.GetDisplayName()}' can not be statically determined and may not meet 'DynamicallyAccessedMembersAttribute' requirements.");
                            break;
                        case GenericParameterOrigin genericParameter:
                            // Note: this is currently unreachable as there's no IL way to pass unknown value to a generic parameter without using reflection.
                            // Once we support analysis of MakeGenericType/MakeGenericMethod arguments this would become reachable though.
                            reflectionContext.RecordUnrecognizedPattern(
                                2066,
                                $"Type passed to generic parameter '{genericParameter.Name}' of '{DiagnosticUtilities.GetGenericParameterDeclaringMemberDisplayName(genericParameter)}' can not be statically determined and may not meet 'DynamicallyAccessedMembersAttribute' requirements.");
                            break;
                        default: throw new NotImplementedException($"unsupported target context {targetContext.GetType()}");
                    };
                }
            }

            reflectionContext.RecordHandledPattern();
        }

        static bool BindingFlagsAreSupported(BindingFlags bindingFlags)
        {
            return (bindingFlags & BindingFlags.IgnoreCase) == BindingFlags.IgnoreCase || (int)bindingFlags > 255;
        }

        void MarkTypeForDynamicallyAccessedMembers(ref ReflectionPatternContext reflectionContext, TypeDesc typeDefinition, DynamicallyAccessedMemberTypes requiredMemberTypes)
        {
            foreach (var member in typeDefinition.GetDynamicallyAccessedMembers(requiredMemberTypes))
            {
                switch (member)
                {
                    case MethodDesc method:
                        MarkMethod(ref reflectionContext, method);
                        break;
                    case FieldDesc field:
                        MarkField(ref reflectionContext, field);
                        break;
                    case MetadataType nestedType:
                        MarkNestedType(ref reflectionContext, nestedType);
                        break;
                    case PropertyPseudoDesc property:
                        MarkProperty(ref reflectionContext, property);
                        break;
                    case EventPseudoDesc @event:
                        MarkEvent(ref reflectionContext, @event);
                        break;
                    default:
                        Debug.Fail(member.GetType().ToString());
                        break;
                }
            }
        }

        void MarkMethod(ref ReflectionPatternContext reflectionContext, MethodDesc method)
        {
            var source = reflectionContext.Source;
            reflectionContext.RecordRecognizedPattern(() => _logger.Writer.WriteLine($"Marking {method.GetDisplayName()}"));
        }

        void MarkNestedType(ref ReflectionPatternContext reflectionContext, MetadataType nestedType)
        {
            var source = reflectionContext.Source;
            reflectionContext.RecordRecognizedPattern(() => _logger.Writer.WriteLine($"Marking {nestedType.GetDisplayName()}"));
        }

        void MarkField(ref ReflectionPatternContext reflectionContext, FieldDesc field)
        {
            var source = reflectionContext.Source;
            reflectionContext.RecordRecognizedPattern(() => _logger.Writer.WriteLine($"Marking {field.GetDisplayName()}"));
        }

        void MarkProperty(ref ReflectionPatternContext reflectionContext, PropertyPseudoDesc property)
        {
            reflectionContext.RecordRecognizedPattern(() => {
                // Marking the property itself actually doesn't keep it (it only marks its attributes and records the dependency), we have to mark the methods on it
                _logger.Writer.WriteLine($"Marking {property.GetDisplayName()}");
            });
        }

        void MarkEvent(ref ReflectionPatternContext reflectionContext, EventPseudoDesc @event)
        {
            reflectionContext.RecordRecognizedPattern(() => {
                // MarkEvent actually marks the add/remove/invoke methods as well, so no need to mark those explicitly
                _logger.Writer.WriteLine($"Marking an event");
            });
        }

        void MarkConstructorsOnType(ref ReflectionPatternContext reflectionContext, TypeDesc type, Func<MethodDesc, bool> filter, BindingFlags? bindingFlags = null)
        {
            foreach (var ctor in type.GetConstructorsOnType(filter, bindingFlags))
                MarkMethod(ref reflectionContext, ctor);
        }

        void MarkMethodsOnTypeHierarchy(ref ReflectionPatternContext reflectionContext, TypeDesc type, Func<MethodDesc, bool> filter, BindingFlags? bindingFlags = null)
        {
            foreach (var method in type.GetMethodsOnTypeHierarchy(filter, bindingFlags))
                MarkMethod(ref reflectionContext, method);
        }

        void MarkFieldsOnTypeHierarchy(ref ReflectionPatternContext reflectionContext, TypeDesc type, Func<FieldDesc, bool> filter, BindingFlags bindingFlags = BindingFlags.Default)
        {
            foreach (var field in type.GetFieldsOnTypeHierarchy(filter, bindingFlags))
                MarkField(ref reflectionContext, field);
        }

        MetadataType[] MarkNestedTypesOnType(ref ReflectionPatternContext reflectionContext, TypeDesc type, Func<MetadataType, bool> filter, BindingFlags bindingFlags = BindingFlags.Default)
        {
            var result = new ArrayBuilder<MetadataType>();

            foreach (var nestedType in type.GetNestedTypesOnType(filter, bindingFlags))
            {
                result.Add(nestedType);
                MarkNestedType(ref reflectionContext, nestedType);
            }

            return result.ToArray();
        }

        void MarkPropertiesOnTypeHierarchy(ref ReflectionPatternContext reflectionContext, TypeDesc type, Func<PropertyPseudoDesc, bool> filter, BindingFlags bindingFlags = BindingFlags.Default)
        {
            foreach (var property in type.GetPropertiesOnTypeHierarchy(filter, bindingFlags))
                MarkProperty(ref reflectionContext, property);
        }

        void MarkEventsOnTypeHierarchy(ref ReflectionPatternContext reflectionContext, TypeDesc type, Func<EventPseudoDesc, bool> filter, BindingFlags bindingFlags = BindingFlags.Default)
        {
            foreach (var @event in type.GetEventsOnTypeHierarchy(filter, bindingFlags))
                MarkEvent(ref reflectionContext, @event);
        }

        static DynamicallyAccessedMemberTypes GetDynamicallyAccessedMemberTypesFromBindingFlagsForNestedTypes(BindingFlags bindingFlags) =>
            (bindingFlags.HasFlag(BindingFlags.Public) ? DynamicallyAccessedMemberTypes.PublicNestedTypes : DynamicallyAccessedMemberTypes.None) |
            (bindingFlags.HasFlag(BindingFlags.NonPublic) ? DynamicallyAccessedMemberTypes.NonPublicNestedTypes : DynamicallyAccessedMemberTypes.None);

        static DynamicallyAccessedMemberTypes GetDynamicallyAccessedMemberTypesFromBindingFlagsForConstructors(BindingFlags bindingFlags) =>
            (bindingFlags.HasFlag(BindingFlags.Public) ? DynamicallyAccessedMemberTypes.PublicConstructors : DynamicallyAccessedMemberTypes.None) |
            (bindingFlags.HasFlag(BindingFlags.NonPublic) ? DynamicallyAccessedMemberTypes.NonPublicConstructors : DynamicallyAccessedMemberTypes.None);

        static DynamicallyAccessedMemberTypes GetDynamicallyAccessedMemberTypesFromBindingFlagsForMethods(BindingFlags bindingFlags) =>
            (bindingFlags.HasFlag(BindingFlags.Public) ? DynamicallyAccessedMemberTypes.PublicMethods : DynamicallyAccessedMemberTypes.None) |
            (bindingFlags.HasFlag(BindingFlags.NonPublic) ? DynamicallyAccessedMemberTypes.NonPublicMethods : DynamicallyAccessedMemberTypes.None);

        static DynamicallyAccessedMemberTypes GetDynamicallyAccessedMemberTypesFromBindingFlagsForFields(BindingFlags bindingFlags) =>
            (bindingFlags.HasFlag(BindingFlags.Public) ? DynamicallyAccessedMemberTypes.PublicFields : DynamicallyAccessedMemberTypes.None) |
            (bindingFlags.HasFlag(BindingFlags.NonPublic) ? DynamicallyAccessedMemberTypes.NonPublicFields : DynamicallyAccessedMemberTypes.None);

        static DynamicallyAccessedMemberTypes GetDynamicallyAccessedMemberTypesFromBindingFlagsForProperties(BindingFlags bindingFlags) =>
            (bindingFlags.HasFlag(BindingFlags.Public) ? DynamicallyAccessedMemberTypes.PublicProperties : DynamicallyAccessedMemberTypes.None) |
            (bindingFlags.HasFlag(BindingFlags.NonPublic) ? DynamicallyAccessedMemberTypes.NonPublicProperties : DynamicallyAccessedMemberTypes.None);

        static DynamicallyAccessedMemberTypes GetDynamicallyAccessedMemberTypesFromBindingFlagsForEvents(BindingFlags bindingFlags) =>
            (bindingFlags.HasFlag(BindingFlags.Public) ? DynamicallyAccessedMemberTypes.PublicEvents : DynamicallyAccessedMemberTypes.None) |
            (bindingFlags.HasFlag(BindingFlags.NonPublic) ? DynamicallyAccessedMemberTypes.NonPublicEvents : DynamicallyAccessedMemberTypes.None);
    }
}
