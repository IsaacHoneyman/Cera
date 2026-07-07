#include <stdint.h>
#include <stdbool.h>

typedef enum { 
    VAL_INT = 0,
    VAL_FLOAT = 1,
    VAL_BOOL = 2,
    VAL_CHAR = 3,
    VAL_UNIT = 4,
    VAL_STRING = 5,   // below are heap-allocated objects
    VAL_CLOSURE = 6,
    VAL_TUPLE = 7,
    VAL_ARRAY = 8,
    VAL_ADT = 9
} ValueTag;

typedef struct sObj Obj; // base heap type

typedef struct {
    uint8_t tag;
    union {
        int64_t int_val; // used for int, bool, char, unit
        double float_val; // used for float
        Obj* obj; // used for all heap allocations
    } as;
} CeraValue;

struct sObj {
    int ref_count;
    uint8_t type;
};

typedef struct sObjUpvalue {
    Obj header;
    CeraValue* location; // pointer to live stack slot
    CeraValue closed_value; // copied value once the stack frame is destroyed
    struct sObjUpvalue* next; // linked list
} ObjUpvalue;

typedef struct {
    Obj header;
    int function_index;      // Maps to the global module function array
    uint8_t arity;
    uint8_t upvalue_count;
    ObjUpvalue** upvalues;   // Array of pointers to Upvalue objects
} ObjClosure;

typedef struct {
    int index;
    uint8_t arity;
    uint16_t constant_count;
    CeraValue* constants;
    uint32_t code_size;
    uint8_t* code;
} CompiledFunction;

typedef struct {
    uint32_t function_count;
    CompiledFunction* functions;
    int entry_index;
} Module;