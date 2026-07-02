namespace Cera.Compiler.Backend;

public enum OpCode : byte
{
    // Stack & Memory (0x00 - 0x0F)
    LOAD_CONST = 0x01,
    PUSH_INT = 0x02,
    PUSH_UNIT = 0x03,
    LOAD_LOCAL = 0x04,
    STORE_LOCAL = 0x05,
    POP = 0x06,

    // Mathematical ALU (0x10 - 0x1F)
    ADD = 0x10,
    SUB = 0x11,
    MUL = 0x12,
    DIV = 0x13,
    MOD = 0x14,
    
    // Bitwise ALU (0x20 - 0x2F)
    BIT_AND = 0x20,
    BIT_OR = 0x21,
    BIT_XOR = 0x22,
    BIT_NOT = 0x23,
    SHL = 0x24,
    SHR = 0x25,

    // Relational ALU (0x30 - 0x3F)
    EQ = 0x30,
    NEQ = 0x31,
    LT = 0x32,
    GT = 0x33,
    LTE = 0x34,
    GTE = 0x35,

    // Logical Unary (0x3A - 0x3F)
    NOT = 0x3A,
    NEGATE = 0x3B, // Unary Minus (e.g., -5)

    // Control Flow (0x40 - 0x4F)
    JUMP = 0x40,
    JUMP_IF_FALSE = 0x41,

    // Functions & Closures (0x50 - 0x5F)
    MAKE_CLOSURE = 0x50,
    CALL = 0x51,
    CALL_INTRINSIC = 0x52,
    RETURN = 0x53,

    // ADTs & Collections (0x60 - 0x6F)
    ALLOC_CON = 0x60,
    MATCH_TAG = 0x61,
    EXTRACT_FIELD = 0x62,
    ALLOC_ARRAY = 0x63,
    LIST_CONS = 0x64
}