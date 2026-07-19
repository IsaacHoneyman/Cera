#ifndef VM_H
#define VM_H

#include "value.h"
#define FRAMES_MAX 256
#define STACK_MAX (FRAMES_MAX * 255) 

typedef struct {
    ObjClosure* closure;
    uint8_t* ip;         // Instruction Pointer
    CeraValue* slots;    // Pointer into the VM's operand stack for locals
} CallFrame;

typedef struct {
    CallFrame frames[FRAMES_MAX];
    int frame_count;
    
    CeraValue stack[STACK_MAX];
    CeraValue* stack_top;
    
    Module* active_module;
    ObjUpvalue* open_upvalues; // Head of the open upvalue linked list
} VM;

static inline void push(VM *vm, CeraValue value) {
    *vm->stack_top = value;
    vm->stack_top++;
}

static inline CeraValue pop(VM *vm) {
    vm->stack_top--;
    return *vm->stack_top;
}

char* flatten_char_list(CeraValue val);

void initVM(VM* vm, Module* module, int argc, char** argv);
void freeVM(VM* vm);
int runVM(VM* vm);

typedef enum {
    // --- Stack & Constants (0x00 - 0x0F) ---
    OP_NOP = 0x00,           
    OP_POP = 0x01,           
    OP_DUP = 0x02,           
    OP_LOAD_CONST = 0x03,    
    OP_PUSH_0 = 0x04,        
    OP_PUSH_1 = 0x05,        
    OP_PUSH_BYTE = 0x06,     
    OP_PUSH_TRUE = 0x07,     
    OP_PUSH_FALSE = 0x08,    
    OP_PUSH_UNIT = 0x09,     
    OP_PUSH_CHAR = 0x0A,     
    OP_LOAD_CONST_LONG = 0x0B, 

    // --- Environment & Scoping (0x10 - 0x1F) ---
    OP_LOAD_LOCAL = 0x10,    
    OP_STORE_LOCAL = 0x11,   
    OP_LOAD_UPVALUE = 0x12,  
    OP_LOAD_FUNCTION = 0x13, 
    OP_LOAD_FUNCTION_LONG = 0x14,

    // --- Mathematical ALU (0x20 - 0x2F) ---
    OP_ADD = 0x20,
    OP_SUB = 0x21,
    OP_MUL = 0x22,
    OP_DIV = 0x23,
    OP_MOD = 0x24,
    OP_NEGATE = 0x25,        

    // --- Bitwise ALU (0x30 - 0x3F) ---
    OP_BIT_AND = 0x30,
    OP_BIT_OR = 0x31,
    OP_BIT_XOR = 0x32,
    OP_BIT_NOT = 0x33,       
    OP_SHL = 0x34,           
    OP_SHR = 0x35,           

    // --- Relational & Logical ALU (0x40 - 0x4F) ---
    OP_EQ = 0x40,            
    OP_NEQ = 0x41,           
    OP_LT = 0x42,            
    OP_GT = 0x43,            
    OP_LTE = 0x44,           
    OP_GTE = 0x45,           
    OP_NOT = 0x46,           

    // --- Control Flow (0x50 - 0x5F) ---
    OP_JUMP = 0x50,          
    OP_JUMP_IF_FALSE = 0x51, 
    OP_JUMP_IF_TRUE = 0x52,  

    // --- Functions & Closures (0x60 - 0x6F) ---
    OP_CALL = 0x60,          
    OP_CALL_INTRINSIC = 0x61,
    OP_TAIL_CALL = 0x62,     
    OP_RETURN = 0x63,        
    OP_MAKE_CLOSURE = 0x64,  

    // --- Memory & ADTs (0x70 - 0x7F) ---
    OP_ALLOC_CON = 0x70,     
    OP_ALLOC_TUPLE = 0x71,   
    OP_ALLOC_ARRAY = 0x72,   
    OP_ALLOC_ARRAY_LONG = 0x7A, 
    OP_LIST_EMPTY = 0x73,    
    OP_LIST_CONS = 0x74,     

    // --- Pattern Matching / Switch Operations (0x80 - 0x8F) ---
    OP_MATCH_TAG = 0x80,     
    OP_UNPACK_CON = 0x81,    
    OP_UNPACK_TUPLE = 0x82,  
    OP_UNPACK_LIST = 0x83,   
    OP_IS_LIST_EMPTY = 0x84,  
    OP_MATCH_ARRAY_LENGTH = 0x85, 
    OP_UNPACK_ARRAY = 0x86,       
    OP_MATCH_FAIL = 0x87,         
} OpCode;

typedef enum {
    // --- Array & List Memory Operations ---
    INTR_GET = 0x00,
    INTR_ARR_LENGTH = 0x01,
    INTR_CONCAT = 0x02,
    INTR_ARR_CONCAT = 0x03,
    INTR_ARR_TO_LIST = 0x04,
    INTR_LIST_TO_ARR = 0x05,

    // --- I/O Operations ---
    INTR_OUT = 0x10,
    INTR_IN = 0x11,
    INTR_READ = 0x12,
    INTR_WRITE = 0x13,
    INTR_APPEND = 0x14,

    // --- Type Conversions ---
    INTR_INT_TO_FLOAT = 0x20,
    INTR_FLOAT_TO_INT = 0x21,
    INTR_CHAR_TO_INT = 0x22,
    INTR_INT_TO_CHAR = 0x23,
    INTR_INT_TO_CHARS = 0x24,
    INTR_FLOAT_TO_CHARS = 0x25,
    INTR_BOOL_TO_CHARS = 0x26,
    INTR_CHARS_TO_INT = 0x27,
    INTR_CHARS_TO_FLOAT = 0x28,

    // --- Math ---
    INTR_RAND = 0x30,
    INTR_RAND_INT = 0x31,
    INTR_SQRT = 0x32,
    INTR_TIME = 0x33
} IntrinsicId;

#endif 