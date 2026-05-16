// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.WASI.GFX.Webgpu;
using Xunit;
using Gen = Wacs.WASI.GFX.Webgpu.Webgpu;

namespace Wacs.WASI.GFX.Webgpu.Test
{
    /// <summary>
    /// Direct exercises of the canonical-ABI memory decoders in
    /// <see cref="WitBindings"/>. Each test builds a synthetic
    /// <see cref="MemoryInstance"/>, writes the wire-form bytes
    /// for the decoder's expected layout, and asserts the
    /// resulting C# record. Catches layout-arithmetic regressions
    /// without needing real wgpu-native.
    /// </summary>
    public class CanonicalAbiDecoderTests
    {
        /// <summary>Allocate a one-page memory (64 KiB) — comfortably
        /// larger than any descriptor we decode.</summary>
        private static MemoryInstance NewMem()
            => new MemoryInstance(new MemoryType(1));

        private static void WriteU32(MemoryInstance mem, int off, uint v)
        {
            var span = mem.AsSpan(off, 4);
            span[0] = (byte)v;
            span[1] = (byte)(v >> 8);
            span[2] = (byte)(v >> 16);
            span[3] = (byte)(v >> 24);
        }

        private static void WriteF32(MemoryInstance mem, int off, float v)
            => WriteU32(mem, off,
                unchecked((uint)BitConverter.SingleToInt32Bits(v)));

        // ----- gpu-primitive-state -----

        [Fact]
        public void ReadOptPrimitiveState_Disc0_ReturnsNone()
        {
            var mem = NewMem();
            // disc @0 = 0
            var result = WitBindings.ReadOptPrimitiveState(mem, 0);
            Assert.False(result.HasValue);
        }

        [Fact]
        public void ReadOptPrimitiveState_AllFieldsSet_DecodesEachArm()
        {
            var mem = NewMem();
            // disc @0 = 1; payload @1
            mem.Data[0] = 1;
            // topology opt<enum> @1: disc@1=1, val@2=LineStrip(2)
            mem.Data[1] = 1; mem.Data[2] = (byte)Gen.GpuPrimitiveTopology.LineStrip;
            // strip-index-format @3: disc@3=1, val@4=Uint16(0)
            mem.Data[3] = 1; mem.Data[4] = (byte)Gen.GpuIndexFormat.Uint16;
            // front-face @5: disc@5=1, val@6=Cw(1)
            mem.Data[5] = 1; mem.Data[6] = (byte)Gen.GpuFrontFace.Cw;
            // cull-mode @7: disc@7=1, val@8=Back(2)
            mem.Data[7] = 1; mem.Data[8] = (byte)Gen.GpuCullMode.Back;
            // unclipped-depth @9: disc@9=1, val@10=true
            mem.Data[9] = 1; mem.Data[10] = 1;

            var opt = WitBindings.ReadOptPrimitiveState(mem, 0);
            Assert.True(opt.HasValue);
            var r = opt.Value;
            Assert.Equal(Gen.GpuPrimitiveTopology.LineStrip,
                r.Topology.Value);
            Assert.Equal(Gen.GpuIndexFormat.Uint16,
                r.StripIndexFormat.Value);
            Assert.Equal(Gen.GpuFrontFace.Cw, r.FrontFace.Value);
            Assert.Equal(Gen.GpuCullMode.Back, r.CullMode.Value);
            Assert.True(r.UnclippedDepth.Value);
        }

        [Fact]
        public void ReadOptPrimitiveState_MixedNoneSome_HonorsEachDisc()
        {
            var mem = NewMem();
            mem.Data[0] = 1;
            // Only front-face is Some; others have disc=0 so values
            // at +2/+4/+8/+10 are garbage and must NOT be read.
            mem.Data[2] = 0xFF; mem.Data[4] = 0xFF;
            mem.Data[8] = 0xFF; mem.Data[10] = 0xFF;
            mem.Data[5] = 1; mem.Data[6] = (byte)Gen.GpuFrontFace.Ccw;

            var opt = WitBindings.ReadOptPrimitiveState(mem, 0);
            Assert.True(opt.HasValue);
            var r = opt.Value;
            Assert.False(r.Topology.HasValue);
            Assert.False(r.StripIndexFormat.HasValue);
            Assert.False(r.CullMode.HasValue);
            Assert.False(r.UnclippedDepth.HasValue);
            Assert.True(r.FrontFace.HasValue);
            Assert.Equal(Gen.GpuFrontFace.Ccw, r.FrontFace.Value);
        }

        // ----- gpu-stencil-face-state -----

        [Fact]
        public void ReadOptStencilFaceState_Disc0_ReturnsNone()
        {
            var mem = NewMem();
            Assert.False(WitBindings.ReadOptStencilFaceState(mem, 0).HasValue);
        }

        [Fact]
        public void ReadOptStencilFaceState_AllFieldsSet()
        {
            var mem = NewMem();
            mem.Data[0] = 1; // outer disc
            // compare @1: disc@1=1, val@2=LessEqual(4)
            mem.Data[1] = 1; mem.Data[2] = (byte)Gen.GpuCompareFunction.LessEqual;
            // fail-op @3: disc@3=1, val@4=Replace(2)
            mem.Data[3] = 1; mem.Data[4] = (byte)Gen.GpuStencilOperation.Replace;
            // depth-fail-op @5
            mem.Data[5] = 1; mem.Data[6] = (byte)Gen.GpuStencilOperation.Invert;
            // pass-op @7
            mem.Data[7] = 1; mem.Data[8] = (byte)Gen.GpuStencilOperation.IncrementClamp;

            var r = WitBindings.ReadOptStencilFaceState(mem, 0).Value;
            Assert.Equal(Gen.GpuCompareFunction.LessEqual, r.Compare.Value);
            Assert.Equal(Gen.GpuStencilOperation.Replace, r.FailOp.Value);
            Assert.Equal(Gen.GpuStencilOperation.Invert, r.DepthFailOp.Value);
            Assert.Equal(Gen.GpuStencilOperation.IncrementClamp,
                r.PassOp.Value);
        }

        // ----- gpu-depth-stencil-state -----

        [Fact]
        public void ReadOptDepthStencilState_Disc0_ReturnsNone()
        {
            var mem = NewMem();
            Assert.False(WitBindings.ReadOptDepthStencilState(mem, 0).HasValue);
        }

        [Fact]
        public void ReadOptDepthStencilState_AllFieldsSet()
        {
            var mem = NewMem();
            // Outer disc @0=1; payload starts @4 (align 4).
            mem.Data[0] = 1;
            // format u8 @4 = Depth24plusStencil8 (42 per the WIT
            // enum order); use whatever happens to be defined.
            mem.Data[4] = (byte)Gen.GpuTextureFormat.Depth24plusStencil8;
            // depth-write-enabled @5: disc=1 val=1
            mem.Data[5] = 1; mem.Data[6] = 1;
            // depth-compare @7: disc=1 val=Always(7)
            mem.Data[7] = 1; mem.Data[8] = (byte)Gen.GpuCompareFunction.Always;
            // stencil-front @9 (9 bytes): disc=1, sff payload @10 (all None)
            mem.Data[9] = 1;
            // stencil-back @18: disc=1, sff payload @19 (all None)
            mem.Data[18] = 1;
            // stencil-read-mask @28: disc=1, val(u32) @32
            mem.Data[28] = 1; WriteU32(mem, 32, 0xABCD0123);
            // stencil-write-mask @36: disc=1, val(u32) @40
            mem.Data[36] = 1; WriteU32(mem, 40, 0xFEDCBA98);
            // depth-bias @44 (s32): disc=1, val @48
            mem.Data[44] = 1; WriteU32(mem, 48, unchecked((uint)(-7)));
            // depth-bias-slope-scale @52 (f32): disc=1, val @56
            mem.Data[52] = 1; WriteF32(mem, 56, 1.5f);
            // depth-bias-clamp @60 (f32): disc=1, val @64
            mem.Data[60] = 1; WriteF32(mem, 64, -0.25f);

            var r = WitBindings.ReadOptDepthStencilState(mem, 0).Value;
            Assert.Equal(Gen.GpuTextureFormat.Depth24plusStencil8, r.Format);
            Assert.True(r.DepthWriteEnabled.Value);
            Assert.Equal(Gen.GpuCompareFunction.Always,
                r.DepthCompare.Value);
            Assert.True(r.StencilFront.HasValue);
            Assert.True(r.StencilBack.HasValue);
            Assert.Equal(0xABCD0123u, r.StencilReadMask.Value);
            Assert.Equal(0xFEDCBA98u, r.StencilWriteMask.Value);
            Assert.Equal(-7, r.DepthBias.Value);
            Assert.Equal(1.5f, r.DepthBiasSlopeScale.Value);
            Assert.Equal(-0.25f, r.DepthBiasClamp.Value);
        }

        // ----- gpu-multisample-state -----

        [Fact]
        public void ReadOptMultisampleState_Disc0_ReturnsNone()
        {
            var mem = NewMem();
            Assert.False(WitBindings.ReadOptMultisampleState(mem, 0).HasValue);
        }

        [Fact]
        public void ReadOptMultisampleState_AllFieldsSet()
        {
            var mem = NewMem();
            mem.Data[0] = 1;
            // count @4: disc=1, val(u32) @8 = 4
            mem.Data[4] = 1; WriteU32(mem, 8, 4);
            // mask @12: disc=1, val(u32) @16 = 0xDEADBEEF
            mem.Data[12] = 1; WriteU32(mem, 16, 0xDEADBEEFu);
            // alpha-to-coverage-enabled @20: disc=1 val@21=true
            mem.Data[20] = 1; mem.Data[21] = 1;

            var r = WitBindings.ReadOptMultisampleState(mem, 0).Value;
            Assert.Equal(4u, r.Count.Value);
            Assert.Equal(0xDEADBEEFu, r.Mask.Value);
            Assert.True(r.AlphaToCoverageEnabled.Value);
        }

        // ----- gpu-render-pass-depth-stencil-attachment -----

        [Fact]
        public void ReadOptDepthStencilAttachment_Disc0_ReturnsNone()
        {
            var mem = NewMem();
            Assert.False(WitBindings
                .ReadOptDepthStencilAttachment(mem, 0,
                    _ => throw new InvalidOperationException(
                        "lookup must not be called for None"))
                .HasValue);
        }

        [Fact]
        public void ReadOptDepthStencilAttachment_AllFieldsSet()
        {
            var mem = NewMem();
            var view = new StubGpuTextureView();
            mem.Data[0] = 1;  // outer disc
            // view u32 @4 = 42
            WriteU32(mem, 4, 42);
            // depth-clear-value @8 (f32): disc=1, val @12
            mem.Data[8] = 1; WriteF32(mem, 12, 0.75f);
            // depth-load-op @16: disc=1 val@17=Clear(0)
            mem.Data[16] = 1; mem.Data[17] = (byte)Gen.GpuLoadOp.Clear;
            // depth-store-op @18: disc=1 val@19=Store(0)
            mem.Data[18] = 1; mem.Data[19] = (byte)Gen.GpuStoreOp.Store;
            // depth-read-only @20: disc=1 val@21=false
            mem.Data[20] = 1; mem.Data[21] = 0;
            // stencil-clear-value @24 (u32): disc=1 val@28=0xCAFEBABE
            mem.Data[24] = 1; WriteU32(mem, 28, 0xCAFEBABEu);
            // stencil-load-op @32: disc=1 val@33=Clear(0)
            mem.Data[32] = 1; mem.Data[33] = (byte)Gen.GpuLoadOp.Clear;
            // stencil-store-op @34: disc=1 val@35=Discard(1)
            mem.Data[34] = 1; mem.Data[35] = (byte)Gen.GpuStoreOp.Discard;
            // stencil-read-only @36: disc=1 val@37=true
            mem.Data[36] = 1; mem.Data[37] = 1;

            Gen.IGpuTextureView? captured = null;
            var r = WitBindings.ReadOptDepthStencilAttachment(mem, 0,
                handle =>
                {
                    Assert.Equal(42, handle);
                    return view;
                }).Value;
            captured = r.View;
            Assert.Same(view, captured);
            Assert.Equal(0.75f, r.DepthClearValue.Value);
            Assert.Equal(Gen.GpuLoadOp.Clear, r.DepthLoadOp.Value);
            Assert.Equal(Gen.GpuStoreOp.Store, r.DepthStoreOp.Value);
            Assert.False(r.DepthReadOnly.Value);
            Assert.Equal(0xCAFEBABEu, r.StencilClearValue.Value);
            Assert.Equal(Gen.GpuLoadOp.Clear, r.StencilLoadOp.Value);
            Assert.Equal(Gen.GpuStoreOp.Discard, r.StencilStoreOp.Value);
            Assert.True(r.StencilReadOnly.Value);
        }

        // ----- gpu-render-pass-timestamp-writes -----

        [Fact]
        public void ReadOptRenderPassTimestampWrites_Disc0_ReturnsNone()
        {
            var mem = NewMem();
            Assert.False(WitBindings
                .ReadOptRenderPassTimestampWrites(mem, 0,
                    _ => throw new InvalidOperationException(
                        "lookup must not be called for None"))
                .HasValue);
        }

        [Fact]
        public void ReadOptRenderPassTimestampWrites_AllFieldsSet()
        {
            var mem = NewMem();
            var qs = new StubQuerySet();
            mem.Data[0] = 1;
            // query-set u32 @4 = 7
            WriteU32(mem, 4, 7);
            // beginning-of-pass @8 (u32): disc=1 val@12=0
            mem.Data[8] = 1; WriteU32(mem, 12, 0);
            // end-of-pass @16 (u32): disc=1 val@20=1
            mem.Data[16] = 1; WriteU32(mem, 20, 1);

            var r = WitBindings.ReadOptRenderPassTimestampWrites(mem, 0,
                handle =>
                {
                    Assert.Equal(7, handle);
                    return qs;
                }).Value;
            Assert.Same(qs, r.QuerySet);
            Assert.Equal(0u, r.BeginningOfPassWriteIndex.Value);
            Assert.Equal(1u, r.EndOfPassWriteIndex.Value);
        }

        [Fact]
        public void ReadOptRenderPassTimestampWrites_PartialNone()
        {
            var mem = NewMem();
            var qs = new StubQuerySet();
            mem.Data[0] = 1;
            WriteU32(mem, 4, 7);
            // both indices None — values at +12/+20 are garbage and
            // must NOT be returned through .Value.
            WriteU32(mem, 12, 0xFFFFFFFFu);
            WriteU32(mem, 20, 0xFFFFFFFFu);

            var r = WitBindings.ReadOptRenderPassTimestampWrites(mem, 0,
                _ => qs).Value;
            Assert.Same(qs, r.QuerySet);
            Assert.False(r.BeginningOfPassWriteIndex.HasValue);
            Assert.False(r.EndOfPassWriteIndex.HasValue);
        }

        // ----- minimal IGpuQuerySet stub (no class in StubGpuResources) -----

        private sealed class StubQuerySet : Gen.IGpuQuerySet
        {
            private string _label = string.Empty;
            public void Destroy() { }
            public Gen.GpuQueryType Type() => Gen.GpuQueryType.Occlusion;
            public uint Count() => 0;
            public string Label() => _label;
            public void SetLabel(string label)
                => _label = label ?? string.Empty;
        }
    }
}
