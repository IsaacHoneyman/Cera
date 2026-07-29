namespace Cera.Compiler.Backend;

public enum OpCode : byte
{
    // --- Stack & Constants (0x00 - 0x0F) ---
    NOP = 0x00,           // No operation (useful for alignment or patching)
    POP = 0x01,           // Discard the top value (for ExprStmt)
    DUP = 0x02,           // Duplicate the top value (Crucial for SwitchExpr target testing)
    LOAD_CONST = 0x03,    // Load constant from module pool (Strings, large floats)
    PUSH_0 = 0x04,        // Pushes integer 0 (1 byte total)
    PUSH_1 = 0x05,        // Pushes integer 1 (1 byte total)
    PUSH_BYTE = 0x06,     // Pushes a small integer between -128 and 127 (2 bytes total)
    PUSH_TRUE = 0x07,     // Optimized boolean true
    PUSH_FALSE = 0x08,    // Optimized boolean false
    PUSH_UNIT = 0x09,     // Optimized unit literal '()'
    PUSH_CHAR = 0x0A,     // Immediate char load
    LOAD_CONST_LONG = 0x0B, // Takes a 2-byte operand (up to 65,535 constants)

    // --- Environment & Scoping (0x10 - 0x1F) ---
    LOAD_LOCAL = 0x10,    // Load variable from current stack frame
    STORE_LOCAL = 0x11,   // Store variable in current stack frame (VarDeclStmt)
    LOAD_UPVALUE = 0x12,  // Load captured variable for closures (LambdaExpr)
    LOAD_FUNCTION = 0x13, // Load a top-level function definition by index
    LOAD_FUNCTION_LONG = 0x14,

    // --- Mathematical ALU (0x20 - 0x2F) ---
    ADD = 0x20,
    SUB = 0x21,
    MUL = 0x22,
    DIV = 0x23,
    MOD = 0x24,
    NEGATE = 0x25,        // Unary minus (e.g., -5)

    // --- Bitwise ALU (0x30 - 0x3F) ---
    BIT_AND = 0x30,
    BIT_OR = 0x31,
    BIT_XOR = 0x32,
    BIT_NOT = 0x33,       // Unary bitwise negation (~)
    SHL = 0x34,           // Left shift (<<)
    SHR = 0x35,           // Right shift (>>)

    // --- Relational & Logical ALU (0x40 - 0x4F) ---
    EQ = 0x40,            // ==
    NEQ = 0x41,           // !=
    LT = 0x42,            // <
    GT = 0x43,            // >
    LTE = 0x44,           // <=
    GTE = 0x45,           // >=
    NOT = 0x46,           // Logical negation (!)

    // --- Control Flow (0x50 - 0x5F) ---
    JUMP = 0x50,          // Unconditional jump (skipping Else blocks, end of switch cases)
    JUMP_IF_FALSE = 0x51, // Pop and jump if false (IfExpr, &&, || short-circuiting)
    JUMP_IF_TRUE = 0x52,  // Pop and jump if true (Useful for || short-circuit optimizations)

    // --- Functions & Closures (0x60 - 0x6F) ---
    CALL = 0x60,          // Call user-defined function
    CALL_INTRINSIC = 0x61,// Call native compiler-provided function
    TAIL_CALL = 0x62,     // Tail recursive call
    RETURN = 0x63,        // Tear down frame and return value
    MAKE_CLOSURE = 0x64,  // Wrap function pointer with upvalues (LambdaExpr)

    // --- Memory & ADTs (0x70 - 0x7F) ---
    ALLOC_CON = 0x70,     // Allocate Constructor instance (ConExpr)
    ALLOC_TUPLE = 0x71,   // Allocate Tuple from N stack items (TupleLitExpr)
    ALLOC_ARRAY = 0x72,   // Allocate Array from N stack items (ArrLitExpr)
    ALLOC_ARRAY_LONG = 0x7A, // Takes a 2-byte operand (up to 65,535 elements)

    LIST_EMPTY = 0x73,    // Push empty list literal '[]'
    LIST_CONS = 0x74,     // Allocate Cons node joining Head and Tail (::)

    // --- Pattern Matching / Switch Operations (0x80 - 0x8F) ---
    JUMP_IF_NOT_TAG = 0x80,     // Peeks top value; if tag mismatches, branches by 16-bit offset
    UNPACK_CON = 0x81,    // Extract constructor payload to stack (ConPattern)
    UNPACK_TUPLE = 0x82,  // Extract all elements of a tuple to stack (TuplePattern)
    UNPACK_LIST = 0x83,   // Pop list, push head and tail (ConsPattern)
    IS_LIST_EMPTY = 0x84,  // Check if list is empty (ListPattern termination)
    MATCH_ARRAY_LENGTH = 0x85, // Pop length, check if array length matches, push bool
    UNPACK_ARRAY = 0x86,       // Pop array, push N elements onto the stack
    MATCH_FAIL = 0x87,         // Runtime error, pattern matching falls through
}