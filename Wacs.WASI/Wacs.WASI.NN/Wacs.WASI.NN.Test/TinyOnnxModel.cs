// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Text;

namespace Wacs.WASI.NN.Test
{
    /// <summary>
    /// Hand-encoded minimal ONNX models for end-to-end inference
    /// tests. Doing the protobuf encoding inline avoids pulling
    /// the (large) Microsoft.ML.OnnxConverter NuGet at test
    /// time and lets the test fixture live entirely in source
    /// — no committed `.onnx` binary blobs to regenerate.
    ///
    /// <para>The encoder is the smallest viable subset of
    /// proto3 wire format: varint, length-delimited string /
    /// submessage, and the field-tag composition. Every byte
    /// in the output is determined by the field layout in
    /// <c>onnx.proto</c>; matching that schema is what makes
    /// the resulting blob a valid ONNX model.</para>
    /// </summary>
    internal static class TinyOnnxModel
    {
        /// <summary>
        /// ONNX model: <c>Identity(X: float[1]) → Y</c>.
        /// Canonical sanity-check graph — output equals input,
        /// so the test can assert byte-fidelity round-trip
        /// without any numerical-tolerance fudge factor.
        /// Around 100-150 bytes.
        /// </summary>
        public static byte[] IdentityFloat1()
        {
            // Schema field numbers from onnx.proto:
            //   ModelProto.ir_version       = 1 (int64)
            //   ModelProto.graph            = 7 (GraphProto)
            //   ModelProto.opset_import     = 8 (repeated)
            //   GraphProto.node             = 1 (repeated)
            //   GraphProto.name             = 2
            //   GraphProto.input            = 11 (repeated ValueInfoProto)
            //   GraphProto.output           = 12 (repeated ValueInfoProto)
            //   NodeProto.input             = 1 (repeated string)
            //   NodeProto.output            = 2 (repeated string)
            //   NodeProto.op_type           = 4
            //   ValueInfoProto.name         = 1
            //   ValueInfoProto.type         = 2 (TypeProto)
            //   TypeProto.tensor_type       = 1 (Tensor)
            //   Tensor.elem_type            = 1 (int32; FLOAT = 1)
            //   Tensor.shape                = 2 (TensorShapeProto)
            //   TensorShapeProto.dim        = 1 (repeated Dimension)
            //   Dimension.dim_value         = 1 (int64)
            //   OperatorSetIdProto.version  = 2 (int64)

            // Dim { dim_value: 1 }
            var dim = Build(ms => WriteVarintField(ms, 1, 1));
            // TensorShape { dim: [Dim] }
            var shape = Build(ms => WriteSubmessage(ms, 1, dim));
            // TensorType { elem_type: 1 (FLOAT), shape: TensorShape }
            var tensorType = Build(ms =>
            {
                WriteVarintField(ms, 1, 1);          // elem_type FLOAT
                WriteSubmessage(ms, 2, shape);        // shape
            });
            // TypeProto { tensor_type: TensorType }
            var typeProto = Build(ms => WriteSubmessage(ms, 1, tensorType));
            // ValueInfoProto { name, type }
            byte[] ValueInfo(string name) => Build(ms =>
            {
                WriteString(ms, 1, name);
                WriteSubmessage(ms, 2, typeProto);
            });
            var inputVI = ValueInfo("X");
            var outputVI = ValueInfo("Y");
            // NodeProto { input: ["X"], output: ["Y"], op_type: "Identity" }
            var node = Build(ms =>
            {
                WriteString(ms, 1, "X");
                WriteString(ms, 2, "Y");
                WriteString(ms, 4, "Identity");
            });
            // GraphProto { node, name, input, output }
            var graph = Build(ms =>
            {
                WriteSubmessage(ms, 1, node);
                WriteString(ms, 2, "id");
                WriteSubmessage(ms, 11, inputVI);
                WriteSubmessage(ms, 12, outputVI);
            });
            // OperatorSetIdProto { version: 13 }
            var opset = Build(ms => WriteVarintField(ms, 2, 13));
            // ModelProto { ir_version: 7, graph, opset_import }
            return Build(ms =>
            {
                WriteVarintField(ms, 1, 7);
                WriteSubmessage(ms, 7, graph);
                WriteSubmessage(ms, 8, opset);
            });
        }

        // ---- protobuf wire-format primitives ----

        private static byte[] Build(Action<MemoryStream> writer)
        {
            using var ms = new MemoryStream();
            writer(ms);
            return ms.ToArray();
        }

        private static void WriteVarint(MemoryStream ms, ulong value)
        {
            while (value >= 0x80)
            {
                ms.WriteByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            ms.WriteByte((byte)value);
        }

        private static void WriteTag(MemoryStream ms, int field, int wireType)
            => WriteVarint(ms, (ulong)((field << 3) | wireType));

        private static void WriteVarintField(MemoryStream ms, int field, long value)
        {
            WriteTag(ms, field, 0);
            WriteVarint(ms, (ulong)value);
        }

        private static void WriteString(MemoryStream ms, int field, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            WriteTag(ms, field, 2);
            WriteVarint(ms, (ulong)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }

        private static void WriteSubmessage(MemoryStream ms, int field, byte[] data)
        {
            WriteTag(ms, field, 2);
            WriteVarint(ms, (ulong)data.Length);
            ms.Write(data, 0, data.Length);
        }
    }
}
