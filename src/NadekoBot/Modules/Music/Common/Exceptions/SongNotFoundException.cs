﻿using System;

namespace Mitternacht.Modules.Music.Common.Exceptions
{
    public class SongNotFoundException : Exception
    {
        public SongNotFoundException(string message) : base(message)
        {
        }
        public SongNotFoundException() : base("Song is not found.") { }
    }
}
