using System;

namespace SurfaceVnc.Core
{
    /// <summary>Raised for protocol level errors (unexpected message, unsupported encoding, ...).</summary>
    public class RfbException : Exception
    {
        public RfbException(string message) : base(message) { }
        public RfbException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Raised when the server rejects our credentials or refuses the connection.</summary>
    public class RfbAuthenticationException : RfbException
    {
        public RfbAuthenticationException(string message) : base(message) { }
    }

    /// <summary>Raised when the server closes the connection (or the socket dies).</summary>
    public class RfbConnectionClosedException : RfbException
    {
        public RfbConnectionClosedException(string message) : base(message) { }
    }
}
