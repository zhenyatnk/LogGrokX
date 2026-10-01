using System;

namespace LogGrokX.Data;

[Flags]
public enum Base64Content
{
    None = 0,
    Pem = 1,
    Base64 = 2,
    All = Pem | Base64
}
