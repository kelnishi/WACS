// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Silk.NET.SDL;
using Wacs.WASI.GFX.Types;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// SDL_Scancode → <see cref="Key"/> mapping. The
    /// wasi-gfx <c>key</c> enum follows the W3C
    /// uievents-code spec — keys are identified by physical
    /// position (KeyA = the key that's "A" on a US QWERTY
    /// layout, regardless of the actual layout). SDL's
    /// scancodes have the same semantics, so this is a
    /// near-1:1 mapping.
    ///
    /// <para>v0 covers the writing-system keys (digits +
    /// letters), the common control / arrow / numpad / F-key
    /// blocks, and the basic modifiers + audio/media subset.
    /// Unmapped scancodes return null — the WIT uses
    /// <c>option&lt;key&gt;</c> for that case.</para>
    /// </summary>
    internal static class SdlKeyMapping
    {
        public static Key? FromScancode(Scancode sc)
        {
            switch (sc)
            {
                // Writing system — digits
                case Scancode.Scancode1: return Key.Digit1;
                case Scancode.Scancode2: return Key.Digit2;
                case Scancode.Scancode3: return Key.Digit3;
                case Scancode.Scancode4: return Key.Digit4;
                case Scancode.Scancode5: return Key.Digit5;
                case Scancode.Scancode6: return Key.Digit6;
                case Scancode.Scancode7: return Key.Digit7;
                case Scancode.Scancode8: return Key.Digit8;
                case Scancode.Scancode9: return Key.Digit9;
                case Scancode.Scancode0: return Key.Digit0;

                // Writing system — letters
                case Scancode.ScancodeA: return Key.KeyA;
                case Scancode.ScancodeB: return Key.KeyB;
                case Scancode.ScancodeC: return Key.KeyC;
                case Scancode.ScancodeD: return Key.KeyD;
                case Scancode.ScancodeE: return Key.KeyE;
                case Scancode.ScancodeF: return Key.KeyF;
                case Scancode.ScancodeG: return Key.KeyG;
                case Scancode.ScancodeH: return Key.KeyH;
                case Scancode.ScancodeI: return Key.KeyI;
                case Scancode.ScancodeJ: return Key.KeyJ;
                case Scancode.ScancodeK: return Key.KeyK;
                case Scancode.ScancodeL: return Key.KeyL;
                case Scancode.ScancodeM: return Key.KeyM;
                case Scancode.ScancodeN: return Key.KeyN;
                case Scancode.ScancodeO: return Key.KeyO;
                case Scancode.ScancodeP: return Key.KeyP;
                case Scancode.ScancodeQ: return Key.KeyQ;
                case Scancode.ScancodeR: return Key.KeyR;
                case Scancode.ScancodeS: return Key.KeyS;
                case Scancode.ScancodeT: return Key.KeyT;
                case Scancode.ScancodeU: return Key.KeyU;
                case Scancode.ScancodeV: return Key.KeyV;
                case Scancode.ScancodeW: return Key.KeyW;
                case Scancode.ScancodeX: return Key.KeyX;
                case Scancode.ScancodeY: return Key.KeyY;
                case Scancode.ScancodeZ: return Key.KeyZ;

                // Punctuation / writing system
                case Scancode.ScancodeMinus:        return Key.Minus;
                case Scancode.ScancodeEquals:       return Key.Equal;
                case Scancode.ScancodeLeftbracket:  return Key.BracketLeft;
                case Scancode.ScancodeRightbracket: return Key.BracketRight;
                case Scancode.ScancodeBackslash:    return Key.Backslash;
                case Scancode.ScancodeSemicolon:    return Key.Semicolon;
                case Scancode.ScancodeApostrophe:   return Key.Quote;
                case Scancode.ScancodeGrave:        return Key.Backquote;
                case Scancode.ScancodeComma:        return Key.Comma;
                case Scancode.ScancodePeriod:       return Key.Period;
                case Scancode.ScancodeSlash:        return Key.Slash;

                // Functional
                case Scancode.ScancodeReturn:    return Key.Enter;
                case Scancode.ScancodeEscape:    return Key.Escape;
                case Scancode.ScancodeBackspace: return Key.Backspace;
                case Scancode.ScancodeTab:       return Key.Tab;
                case Scancode.ScancodeSpace:     return Key.Space;
                case Scancode.ScancodeCapslock:  return Key.CapsLock;

                // Function keys
                case Scancode.ScancodeF1:  return Key.F1;
                case Scancode.ScancodeF2:  return Key.F2;
                case Scancode.ScancodeF3:  return Key.F3;
                case Scancode.ScancodeF4:  return Key.F4;
                case Scancode.ScancodeF5:  return Key.F5;
                case Scancode.ScancodeF6:  return Key.F6;
                case Scancode.ScancodeF7:  return Key.F7;
                case Scancode.ScancodeF8:  return Key.F8;
                case Scancode.ScancodeF9:  return Key.F9;
                case Scancode.ScancodeF10: return Key.F10;
                case Scancode.ScancodeF11: return Key.F11;
                case Scancode.ScancodeF12: return Key.F12;

                // Control pad / arrows
                case Scancode.ScancodeInsert:   return Key.Insert;
                case Scancode.ScancodeHome:     return Key.Home;
                case Scancode.ScancodePageup:   return Key.PageUp;
                case Scancode.ScancodeDelete:   return Key.Delete;
                case Scancode.ScancodeEnd:      return Key.End;
                case Scancode.ScancodePagedown: return Key.PageDown;
                case Scancode.ScancodeRight:    return Key.ArrowRight;
                case Scancode.ScancodeLeft:     return Key.ArrowLeft;
                case Scancode.ScancodeDown:     return Key.ArrowDown;
                case Scancode.ScancodeUp:       return Key.ArrowUp;

                // Modifiers
                case Scancode.ScancodeLctrl:  return Key.ControlLeft;
                case Scancode.ScancodeLshift: return Key.ShiftLeft;
                case Scancode.ScancodeLalt:   return Key.AltLeft;
                case Scancode.ScancodeLgui:   return Key.MetaLeft;
                case Scancode.ScancodeRctrl:  return Key.ControlRight;
                case Scancode.ScancodeRshift: return Key.ShiftRight;
                case Scancode.ScancodeRalt:   return Key.AltRight;
                case Scancode.ScancodeRgui:   return Key.MetaRight;

                default: return null;
            }
        }

        public static (bool alt, bool ctrl, bool meta, bool shift) Modifiers(Keymod mod)
        {
            bool alt   = (mod & (Keymod.Lalt | Keymod.Ralt)) != 0;
            bool ctrl  = (mod & (Keymod.Lctrl | Keymod.Rctrl)) != 0;
            bool meta  = (mod & (Keymod.Lgui | Keymod.Rgui)) != 0;
            bool shift = (mod & (Keymod.Lshift | Keymod.Rshift)) != 0;
            return (alt, ctrl, meta, shift);
        }
    }
}
