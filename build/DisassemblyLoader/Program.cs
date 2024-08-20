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
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CompilerExplorer
{
    namespace DisassemblyLoader
    {
        class Program
        {
            private static readonly bool _isMonoRuntime = Type.GetType("Mono.RuntimeStructs") != null;
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
            });

            static void Main(string[] args)
            {
                var assembly = Assembly.LoadFile(args[0]);

                foreach (var type in assembly.GetTypes())
                {
                    ProcessType(type);
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
                            PrepareType(type.MakeGenericType(attr.GenericArguments));
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
                }
            }

            static void PrepareType(Type type)
            {
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
                            PrepareMethod(method.MakeGenericMethod(attr.GenericArguments));
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
                }
            }

            static void PrepareMethod(MethodBase methodBase)
            {
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
                    Console.WriteLine($"; Failed to generate code for '{methodBase}':");
                    var diagInfo = ex.ToString();
                    foreach (var line in diagInfo.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        Console.WriteLine($"; {line}");
                    }
                    Console.WriteLine("============================================================");
                    throw;
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
}
