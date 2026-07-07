namespace Cera.Compiler.Analyzer;

public enum IntrinsicId : byte
{
    // --- Array & List Memory Operations ---
    Get = 0x00,
    ArrLength = 0x01,
    Build = 0x02,
    ArrBuild = 0x03,
    Concat = 0x04,
    ArrConcat = 0x05,
    ArrToList = 0x06,
    ListToArr = 0x07,

    // --- I/O Operations ---
    Out = 0x10,
    In = 0x11,
    Read = 0x12,
    Write = 0x13,

    // --- Type Conversions ---
    IntToFloat = 0x20,
    FloatToInt = 0x21,
    CharToInt = 0x22,
    IntToChar = 0x23,
    IntToChars = 0x24,
    FloatToChars = 0x25,
    BoolToChars = 0x26,
    CharsToInt = 0x27,
    CharsToFloat = 0x28
}