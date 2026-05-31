// Copyright (c) 2023, Compiler Explorer Authors
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.

using Iced.Intel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CompilerExplorer
{
    namespace DisassemblyLoader
    {
        class Program
        {
            private static readonly bool _isMonoRuntime = Type.GetType("Mono.RuntimeStructs") != null;
            private static readonly HashSet<MethodBase> _preparedMethods = new();
            private static readonly HashSet<Type> _preparedTypes = new();
            private static readonly Formatter _defaultFormatter = new IntelFormatter(new FormatterOptions
            {
                HexSuffix = "h",
                OctalSuffix = "o",
                BinarySuffix = "b",
                FirstOperandCharIndex = 9,
                ShowSymbolAddress = true,
                SpaceAfterOperandSeparator = true,
                RipRelativeAddresses = true,
                SignedImmediateOperands = true,
                BranchLeadingZeros = false,
            }, new MonoSymbolResolver());

            private static DisassemblerOptionsAttribute _options = default!;

            static void Main(string[] args)
            {
                var assembly = Assembly.LoadFile(args[0]);
                _options = assembly.GetCustomAttribute<DisassemblerOptionsAttribute>() ?? new DisassemblerOptionsAttribute();

                foreach (var type in assembly.GetTypes())
                {
                    ProcessType(type);
                }

                foreach (var attr in assembly.GetCustomAttributes<MethodInstantiationAttribute>())
                {
                    ProcessInstantiation(assembly, containingType: null, attr);
                }
            }

            static void ProcessType(Type type)
            {
                if (type.IsGenericTypeDefinition)
                {
                    foreach (var attr in type.GetCustomAttributes<GenericArgumentsAttribute>())
                    {
                        try
                        {
                            var genericType = type.MakeGenericType(attr.GenericArguments);
                            PrepareType(genericType);
                            ProcessTypeInstantiations(genericType);
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
                else
                {
                    PrepareType(type);
                    ProcessTypeInstantiations(type);
                }
            }

            static void PrepareType(Type type)
            {
                if (!_preparedTypes.Add(type))
                {
                    return;
                }

                if (_options.RunClassConstructor)
                {
                    try
                    {
                        RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"; Failed to run class constructor for type {type}");
                        foreach (var line in ex.ToString().AsSpan().EnumerateLines())
                        {
                            Console.WriteLine($"; {line}");
                        }
                        Console.WriteLine("; ============================================================");
                        Console.WriteLine();
                    }
                }

                foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    PrepareMethod(constructor);
                }

                foreach (var method in type.GetRuntimeMethods())
                {
                    ProcessMethod(method);
                }
            }

            static void ProcessMethod(MethodInfo method)
            {
                if (method.IsGenericMethodDefinition)
                {
                    foreach (var attr in method.GetCustomAttributes<GenericArgumentsAttribute>())
                    {
                        try
                        {
                            var genericMethod = method.MakeGenericMethod(attr.GenericArguments);
                            PrepareMethod(genericMethod);
                            PrepareStateMachineType(genericMethod);
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
                else
                {
                    PrepareMethod(method);
                    PrepareStateMachineType(method);
                }
            }

            static void ProcessTypeInstantiations(Type type)
            {
                var definition = type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;
                foreach (var attr in definition.GetCustomAttributes<MethodInstantiationAttribute>())
                {
                    ProcessInstantiation(type.Assembly, type, attr);
                }
            }

            static void ProcessInstantiation(Assembly assembly, Type? containingType, MethodInstantiationAttribute attr)
            {
                var type = attr.TypeName == null ? containingType : assembly.GetType(attr.TypeName);
                if (type == null)
                {
                    return;
                }

                if (type.ContainsGenericParameters)
                {
                    try
                    {
                        type = type.GetGenericTypeDefinition().MakeGenericType(attr.GenericTypeArguments);
                    }
                    catch
                    {
                        return;
                    }
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name == attr.MethodName))
                {
                    try
                    {
                        if (method.IsGenericMethodDefinition)
                        {
                            if (method.GetGenericArguments().Length != attr.GenericMethodArguments.Length)
                            {
                                continue;
                            }

                            var genericMethod = method.MakeGenericMethod(attr.GenericMethodArguments);
                            PrepareMethod(genericMethod);
                            PrepareStateMachineType(genericMethod);
                        }
                        else if (attr.GenericMethodArguments.Length == 0)
                        {
                            PrepareMethod(method);
                            PrepareStateMachineType(method);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            static void PrepareStateMachineType(MethodInfo method)
            {
                var stateMachineType = method.GetCustomAttribute<StateMachineAttribute>()?.StateMachineType;
                if (stateMachineType == null)
                {
                    return;
                }

                if (stateMachineType.ContainsGenericParameters)
                {
                    var genericArguments = (method.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes)
                        .Concat(method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes)
                        .ToArray();
                    if (genericArguments.Any(argument => argument.ContainsGenericParameters))
                    {
                        return;
                    }

                    stateMachineType = stateMachineType.GetGenericTypeDefinition().MakeGenericType(genericArguments);
                }

                PrepareType(stateMachineType);
            }

            static void PrepareMethod(MethodBase methodBase)
            {
                if (!_preparedMethods.Add(methodBase))
                {
                    return;
                }

                try
                {
                    RuntimeHelpers.PrepareMethod(methodBase.MethodHandle);

                    // mono runtime doesn't support JitDisasm, so we need to print the assembly manually
                    if (_isMonoRuntime)
                    {
                        unsafe
                        {
                            if (MonoJitInspector.TryGetJitCode(methodBase.MethodHandle, out var code, out var size))
                            {
                                MonoJitInspector.WriteDisassembly(methodBase, (byte*)code, size, _defaultFormatter, Console.Out);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"; Failed to generate code for method {methodBase}");
                    foreach (var line in ex.ToString().AsSpan().EnumerateLines())
                    {
                        Console.WriteLine($"; {line}");
                    }
                    Console.WriteLine("; ============================================================");
                    Console.WriteLine();
                }
            }
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    public sealed class GenericArgumentsAttribute : Attribute
    {
        public GenericArgumentsAttribute(params Type[] genericArguments)
        {
            GenericArguments = genericArguments;
        }

        public Type[] GenericArguments { get; }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    public sealed class MethodInstantiationAttribute : Attribute
    {
        public MethodInstantiationAttribute(string typeName, string methodName, Type[]? genericTypeArguments, Type[]? genericMethodArguments)
        {
            TypeName = typeName;
            MethodName = methodName;
            GenericTypeArguments = genericTypeArguments ?? Type.EmptyTypes;
            GenericMethodArguments = genericMethodArguments ?? Type.EmptyTypes;
        }

        public MethodInstantiationAttribute(string typeName, string methodName, string[]? genericTypeArguments, string[]? genericMethodArguments)
        {
            TypeName = typeName;
            MethodName = methodName;
            GenericTypeArguments = (genericTypeArguments ?? Array.Empty<string>())
                .Select(name => Type.GetType(name)).Where(t => t != null).ToArray()!;
            GenericMethodArguments = (genericMethodArguments ?? Array.Empty<string>())
                .Select(name => Type.GetType(name)).Where(t => t != null).ToArray()!;
        }

        public MethodInstantiationAttribute(string methodName, Type[]? genericMethodArguments)
        {
            MethodName = methodName;
            GenericMethodArguments = genericMethodArguments ?? Type.EmptyTypes;
        }

        public MethodInstantiationAttribute(string methodName, string[]? genericMethodArguments)
        {
            MethodName = methodName;
            GenericMethodArguments = (genericMethodArguments ?? Array.Empty<string>())
                .Select(name => Type.GetType(name)).Where(t => t != null).ToArray()!;
        }

        public string? TypeName { get; }
        public string MethodName { get; }
        public Type[] GenericTypeArguments { get; } = Type.EmptyTypes;
        public Type[] GenericMethodArguments { get; } = Type.EmptyTypes;
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
    public sealed class DisassemblerOptionsAttribute : Attribute
    {
        public bool RunClassConstructor { get; set; } = true;
    }
}
