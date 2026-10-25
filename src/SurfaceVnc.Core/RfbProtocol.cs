using System;

namespace SurfaceVnc.Core
{
    /// <summary>
    /// RFB (RFC 6143) protocol constants: message types, encodings, security types and
    /// the protocol version strings we speak.
    /// </summary>
    public static class RfbProtocol
    {
        public const string VersionString33 = "RFB 003.003\n";
        public const string VersionString38 = "RFB 003.008\n";

        public const int ProtocolVersionLength = 12;

        // ---- Client -> Server message types -------------------------------------------
        public const byte MsgSetPixelFormat = 0;
        public const byte MsgSetEncodings = 2;
        public const byte MsgFramebufferUpdateRequest = 3;
        public const byte MsgKeyEvent = 4;
        public const byte MsgPointerEvent = 5;
        public const byte MsgClientCutText = 6;

        // ---- Server -> Client message types -------------------------------------------
        public const byte MsgFramebufferUpdate = 0;
        public const byte MsgSetColourMapEntries = 1;
        public const byte MsgBell = 2;
        public const byte MsgServerCutText = 3;

        // ---- Security types -----------------------------------------------------------
        public const byte SecurityInvalid = 0;
        public const byte SecurityNone = 1;
        public const byte SecurityVncAuthentication = 2;

        public const int VncAuthenticationChallengeLength = 16;

        // ---- Pointer button masks (RFB "button-mask") ---------------------------------
        public const byte ButtonLeft = 1;
        public const byte ButtonMiddle = 2;
        public const byte ButtonRight = 4;
        public const byte ButtonWheelUp = 8;
        public const byte ButtonWheelDown = 16;
        public const byte ButtonWheelLeft = 32;
        public const byte ButtonWheelRight = 64;

        /// <summary>Encodings understood by this client, in order of preference.</summary>
        public static readonly RfbEncoding[] PreferredEncodings = new RfbEncoding[]
        {
            RfbEncoding.CopyRect,
            RfbEncoding.Hextile,
            RfbEncoding.CoRre,
            RfbEncoding.Rre,
            RfbEncoding.Raw
        };
    }

    /// <summary>RFB encoding numbers (a.k.a. "encoding-type" in RFC 6143).</summary>
    public enum RfbEncoding
    {
        Raw = 0,
        CopyRect = 1,
        Rre = 2,
        CoRre = 4,
        Hextile = 5,
        Zrle = 6,
        Tight = 7,
        Trle = 15,
        ZrleEncoding = 16,
        LastRect = unchecked((int)0xFFFFFF20),
        DesktopSize = unchecked((int)0xFFFFFF21),
    }
}
