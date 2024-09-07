// Copyright (c) 2024, Compiler Explorer Authors
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

using System.Reflection;
using System;
using System.IO;
using Iced.Intel;
using static CompilerExplorer.DisassemblyLoader.MonoInterop;
using System.Text;
using System.Linq;
using Decoder = Iced.Intel.Decoder;
using System.Runtime.InteropServices;

namespace CompilerExplorer
{
    namespace DisassemblyLoader
    {
        internal static unsafe class MonoJitInspector
        {
            public static unsafe bool TryGetJitCode(RuntimeMethodHandle handle, out void* code, out int size)
            {
                var fp = (void*)handle.GetFunctionPointer();
                var ji = mono_jit_info_table_find(mono_domain_get(), fp);

                if (ji == null)
                {
                    code = null;
                    size = 0;
                    return false;
                }

                code = mono_jit_info_get_code_start(ji);
                size = mono_jit_info_get_code_size(ji);
                return true;
            }

            private static void WriteComment(TextWriter writer, string comment)
            {
                writer.Write("; ");
                writer.WriteLine(comment);
            }

            public static unsafe void WriteDisassembly(MethodBase method, byte* code, int size, Formatter formatter, TextWriter writer)
            {
                var reader = new UnmanagedCodeReader(code, size);
                var decoder = Decoder.Create(8 * IntPtr.Size, reader);
                var output = new StringOutput();
                decoder.IP = (ulong)code;
                ulong tail = (ulong)(code + size);
                var methodName = FormatMethodName(method);

                WriteComment(writer, $"Assembly listing for method {methodName}");

                while (decoder.IP < tail)
                {
                    var instr = decoder.Decode();
                    formatter.Format(instr, output);
                    writer.Write("       ");
                    writer.WriteLine(output.ToStringAndReset());
                }

                WriteComment(writer, $"Total bytes of code {size} for method {methodName}");
                WriteComment(writer, "============================================================");
                writer.WriteLine();
            }

            private static string FormatMethodName(MethodBase method)
            {
                var sb = new StringBuilder();
                if (method.DeclaringType is Type type)
                {
                    sb.Append(type);
                    sb.Append(':');
                }
                sb.Append(method.Name);
                sb.Append('(');
                sb.Append(string.Join(',', method.GetParameters().Select(p => p.ParameterType)));
                sb.Append(')');
                if (method is MethodInfo { ReturnType: var retType } && retType != typeof(void))
                {
                    sb.Append(':');
                    sb.Append(retType);
                }
                if (method.CallingConvention.HasFlag(CallingConventions.HasThis)
                    && !method.CallingConvention.HasFlag(CallingConventions.ExplicitThis))
                {
                    sb.Append(":this");
                }

                return sb.ToString();
            }
        }

        internal sealed class UnmanagedCodeReader : CodeReader
        {
            public int Length { get; }

            public int Offset { get; private set; }

            public unsafe byte* Pointer { get; }

            public unsafe UnmanagedCodeReader(byte* pointer, int length)
            {
                Pointer = pointer;
                Length = length;
            }

            public override unsafe int ReadByte()
            {
                if (Offset >= Length)
                    return -1;

                return Pointer[Offset++];
            }
        }

        internal sealed unsafe class MonoSymbolResolver : ISymbolResolver
        {
            public bool TryGetSymbol(in Instruction instruction, int operand, int instructionOperand, ulong address, int addressSize, out SymbolResult symbol)
            {
                var name = mono_pmip((void*)address);
                if (name == null)
                {
                    symbol = default;
                    return false;
                }

                symbol = new SymbolResult(address, Marshal.PtrToStringAnsi((nint)name)?.Trim() ?? "unknown");
                return true;
            }
        }
    }
}
