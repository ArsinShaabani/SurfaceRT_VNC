using System;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// X11 keysyms, which is what RFB KeyEvent messages carry, plus the mapping from Windows
    /// virtual key codes (what a Surface keyboard / on-screen keyboard reports) and from
    /// Unicode characters to keysyms.
    /// </summary>
    public static class RemoteKeys
    {
        // Special keys (X11 keysym values, "function" range 0xFF00 - 0xFFFF).
        public const uint BackSpace = 0xFF08;
        public const uint Tab = 0xFF09;
        public const uint Linefeed = 0xFF0A;
        public const uint Return = 0xFF0D;
        public const uint Pause = 0xFF13;
        public const uint ScrollLock = 0xFF14;
        public const uint SysReq = 0xFF15;
        public const uint Escape = 0xFF1B;
        public const uint Home = 0xFF50;
        public const uint Left = 0xFF51;
        public const uint Up = 0xFF52;
        public const uint Right = 0xFF53;
        public const uint Down = 0xFF54;
        public const uint PageUp = 0xFF55;
        public const uint PageDown = 0xFF56;
        public const uint End = 0xFF57;
        public const uint Insert = 0xFF63;
        public const uint Menu = 0xFF67;
        public const uint NumLock = 0xFF7F;
        public const uint KPEnter = 0xFF8D;
        public const uint F1 = 0xFFBE;
        public const uint F2 = 0xFFBF;
        public const uint F3 = 0xFFC0;
        public const uint F4 = 0xFFC1;
        public const uint F5 = 0xFFC2;
        public const uint F6 = 0xFFC3;
        public const uint F7 = 0xFFC4;
        public const uint F8 = 0xFFC5;
        public const uint F9 = 0xFFC6;
        public const uint F10 = 0xFFC7;
        public const uint F11 = 0xFFC8;
        public const uint F12 = 0xFFC9;
        public const uint ShiftLeft = 0xFFE1;
        public const uint ShiftRight = 0xFFE2;
        public const uint ControlLeft = 0xFFE3;
        public const uint ControlRight = 0xFFE4;
        public const uint CapsLock = 0xFFE5;
        public const uint AltLeft = 0xFFE9;
        public const uint AltRight = 0xFFEA;
        public const uint MetaLeft = 0xFFEB;
        public const uint MetaRight = 0xFFEC;
        public const uint SuperLeft = 0xFFEB;
        public const uint SuperRight = 0xFFEC;
        public const uint Delete = 0xFFFF;


        public const uint Space = 0x0020;

        // Windows virtual key codes we map (VK_*).
        private const int VkBack = 0x08;
        private const int VkTab = 0x09;
        private const int VkReturn = 0x0D;
        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkMenu = 0x12;
        private const int VkPause = 0x13;
        private const int VkCapital = 0x14;
        private const int VkEscape = 0x1B;
        private const int VkSpace = 0x20;
        private const int VkPrior = 0x21;
        private const int VkNext = 0x22;
        private const int VkEnd = 0x23;
        private const int VkHome = 0x24;
        private const int VkLeft = 0x25;
        private const int VkUp = 0x26;
        private const int VkRight = 0x27;
        private const int VkDown = 0x28;
        private const int VkInsert = 0x2D;
        private const int VkDelete = 0x2E;
        private const int VkLWin = 0x5B;
        private const int VkRWin = 0x5C;
        private const int VkApps = 0x5D;
        private const int VkNumLock = 0x90;
        private const int VkScroll = 0x91;
        private const int VkF1 = 0x70;

        /// <summary>
        /// Maps a Windows virtual key code to an X11 keysym, or 0 when the key has no keysym
        /// (letters and digits are handled through <see cref="FromUnicode"/> instead, because
        /// only the character tells us about shift state and keyboard layout).
        /// </summary>
        public static uint FromVirtualKey(int virtualKey)
        {
            switch (virtualKey)
            {
                case VkBack: return BackSpace;
                case VkTab: return Tab;
                case VkReturn: return Return;
                case VkShift: return ShiftLeft;
                case VkControl: return ControlLeft;
                case VkMenu: return AltLeft;
                case VkPause: return Pause;
                case VkCapital: return CapsLock;
                case VkEscape: return Escape;
                case VkSpace: return Space;
                case VkPrior: return PageUp;
                case VkNext: return PageDown;
                case VkEnd: return End;
                case VkHome: return Home;
                case VkLeft: return Left;
                case VkUp: return Up;
                case VkRight: return Right;
                case VkDown: return Down;
                case VkInsert: return Insert;
                case VkDelete: return Delete;
                case VkLWin: return SuperLeft;
                case VkRWin: return SuperRight;
                case VkApps: return Menu;
                case VkNumLock: return NumLock;
                case VkScroll: return ScrollLock;
            }

            if (virtualKey >= VkF1 && virtualKey <= VkF1 + 11)
                return F1 + (uint)(virtualKey - VkF1);

            return 0;
        }

        /// <summary>
        /// Maps a Unicode character to a keysym.  ASCII maps to itself; everything else uses the
        /// RFB convention for Unicode characters (0x01000000 | code point), which lets the
        /// on-screen keyboard type Persian/Arabic text into the remote session.
        /// </summary>
        public static uint FromUnicode(int codePoint)
        {
            if (codePoint < 0) return 0;
            if (codePoint <= 0xFF) return (uint)codePoint;   // ASCII and Latin-1 keysyms match Unicode
            return 0x01000000u | (uint)codePoint;
        }

        /// <summary>Keysym for a character, or 0 when it cannot be typed.</summary>
        public static uint FromChar(char character)
        {
            return FromUnicode(character);
        }
    }
}
