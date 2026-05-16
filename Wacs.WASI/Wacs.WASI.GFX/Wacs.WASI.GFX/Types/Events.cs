// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.GFX.Types
{
    /// <summary>
    /// <c>wasi:surface.resize-event</c> — surface size has
    /// changed. Fired once per OS resize completion (not per
    /// intermediate drag tick on platforms that distinguish).
    /// </summary>
    public readonly struct ResizeEvent
    {
        public uint Height { get; }
        public uint Width { get; }

        public ResizeEvent(uint width, uint height)
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// <c>wasi:surface.frame-event</c> — the host is ready for
    /// the next frame (equivalent of
    /// <c>requestAnimationFrame</c>). The WIT carries a
    /// placeholder <c>nothing: bool</c> because empty records
    /// aren't representable; the field is ignored.
    /// </summary>
    public readonly struct FrameEvent
    {
        public bool Nothing { get; }

        public FrameEvent(bool nothing = false)
        {
            Nothing = nothing;
        }
    }

    /// <summary>
    /// <c>wasi:surface.pointer-event</c> — mouse / touch /
    /// pencil position update. Carries x/y only; buttons and
    /// pressure aren't part of the current WIT.
    /// </summary>
    public readonly struct PointerEvent
    {
        public double X { get; }
        public double Y { get; }

        public PointerEvent(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// <c>wasi:surface.key-event</c> — keyboard key transition.
    /// Both <see cref="Key"/> (the physical key per the
    /// uievents-code spec) and <see cref="Text"/> (the
    /// character produced, if any) are present; either may be
    /// null. Modifier flags reflect the modifier state at the
    /// moment the event fired.
    /// </summary>
    public readonly struct KeyEvent
    {
        public Key? Key { get; }
        public string? Text { get; }
        public bool AltKey { get; }
        public bool CtrlKey { get; }
        public bool MetaKey { get; }
        public bool ShiftKey { get; }

        public KeyEvent(
            Key? key,
            string? text,
            bool altKey,
            bool ctrlKey,
            bool metaKey,
            bool shiftKey)
        {
            Key = key;
            Text = text;
            AltKey = altKey;
            CtrlKey = ctrlKey;
            MetaKey = metaKey;
            ShiftKey = shiftKey;
        }
    }
}
