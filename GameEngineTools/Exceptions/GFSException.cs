// GFSException.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Exceptions
{
    using System;

    internal class GFSArgumentNullException : ArgumentNullException
    {
        public GFSArgumentNullException(string message) : base(message)
        {
            Message = message;
        }

        public new string Message { get; private set; }

        public override string ToString()
        {
            return $"Message: {Message}";
        }
    }

    internal class GFSException : Exception
    {
        public GFSException(string message) : base(message)
        {
            Message = message;
        }

        public new string Message { get; private set; }

        public override string ToString()
        {
            return $"Message: {Message}";
        }
    }
}
