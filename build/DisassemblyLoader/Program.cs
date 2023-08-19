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

using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CompilerExplorer
{
    namespace DisassemblyLoader
    {
        class Program
        {
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
                if (type.TypeInitializer is ConstructorInfo initializer)
                    RuntimeHelpers.PrepareMethod(initializer.MethodHandle);

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

            static void PrepareMethod(MethodInfo method)
            {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
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
