// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.GFX.Types
{
    /// <summary>
    /// Physical key identifier. Mirrors the
    /// <c>wasi:surface.key</c> WIT enum verbatim, which itself
    /// follows the W3C uievents-code spec
    /// (https://w3c.github.io/uievents-code/#code-value-tables).
    ///
    /// <para>"Unidentified" is intentionally omitted — wasi-gfx
    /// uses <c>option&lt;key&gt;</c> for that case (modeled as
    /// nullable <see cref="Key"/>? at the SPI level).</para>
    /// </summary>
    public enum Key
    {
        // Writing system keys
        Backquote,
        Backslash,
        BracketLeft,
        BracketRight,
        Comma,
        Digit0,
        Digit1,
        Digit2,
        Digit3,
        Digit4,
        Digit5,
        Digit6,
        Digit7,
        Digit8,
        Digit9,
        Equal,
        IntlBackslash,
        IntlRo,
        IntlYen,
        KeyA,
        KeyB,
        KeyC,
        KeyD,
        KeyE,
        KeyF,
        KeyG,
        KeyH,
        KeyI,
        KeyJ,
        KeyK,
        KeyL,
        KeyM,
        KeyN,
        KeyO,
        KeyP,
        KeyQ,
        KeyR,
        KeyS,
        KeyT,
        KeyU,
        KeyV,
        KeyW,
        KeyX,
        KeyY,
        KeyZ,
        Minus,
        Period,
        Quote,
        Semicolon,
        Slash,

        // Functional keys
        AltLeft,
        AltRight,
        Backspace,
        CapsLock,
        ContextMenu,
        ControlLeft,
        ControlRight,
        Enter,
        MetaLeft,
        MetaRight,
        ShiftLeft,
        ShiftRight,
        Space,
        Tab,
        Convert,
        KanaMode,
        Lang1,
        Lang2,
        Lang3,
        Lang4,
        Lang5,
        NonConvert,

        // Control pad
        Delete,
        End,
        Help,
        Home,
        Insert,
        PageDown,
        PageUp,

        // Arrow pad
        ArrowDown,
        ArrowLeft,
        ArrowRight,
        ArrowUp,

        // Numpad
        NumLock,
        Numpad0,
        Numpad1,
        Numpad2,
        Numpad3,
        Numpad4,
        Numpad5,
        Numpad6,
        Numpad7,
        Numpad8,
        Numpad9,
        NumpadAdd,
        NumpadBackspace,
        NumpadClear,
        NumpadClearEntry,
        NumpadComma,
        NumpadDecimal,
        NumpadDivide,
        NumpadEnter,
        NumpadEqual,
        NumpadHash,
        NumpadMemoryAdd,
        NumpadMemoryClear,
        NumpadMemoryRecall,
        NumpadMemoryStore,
        NumpadMemorySubtract,
        NumpadMultiply,
        NumpadParenLeft,
        NumpadParenRight,
        NumpadStar,
        NumpadSubtract,

        // Function keys
        Escape,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
        Fn,
        FnLock,
        PrintScreen,
        ScrollLock,
        Pause,

        // Media + browser keys (subset of the upstream WIT enum;
        // the full set isn't enumerated here yet).
        BrowserBack,
        BrowserFavorites,
        BrowserForward,
        BrowserHome,
        BrowserRefresh,
        BrowserSearch,
        BrowserStop,
        Eject,
        LaunchApp1,
        LaunchApp2,
        LaunchMail,
        MediaPlayPause,
        MediaSelect,
        MediaStop,
        MediaTrackNext,
        MediaTrackPrevious,
        Power,
        Sleep,
        AudioVolumeDown,
        AudioVolumeMute,
        AudioVolumeUp,
        WakeUp,
    }
}
