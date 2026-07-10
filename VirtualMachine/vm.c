#include <stdio.h>
#include <stdlib.h>
#include "vm.h"
#include "memory.h"
#include "logger.h"

#define DEBUG_TRACE_EXECUTION // to comment out for actual builds

static inline void push(VM* vm, CeraValue value) {
    *vm->stack_top = value;
    vm->stack_top++;
}

static inline CeraValue pop(VM* vm) {
    vm->stack_top--;
    return *vm->stack_top;
}

void initVM(VM* vm, Module* module, int argc, char** argv) {
    log_info("Initializing Virtual Machine execution state...");
    
    vm->stack_top = vm->stack;
    vm->frame_count = 0;
    vm->active_module = module;
    vm->open_upvalues = NULL;

    if (module->entry_index == -1) {
        log_error("Fatal: No entry point defined in module");
        exit(1);
    }

    CompiledFunction* entry_func = &module->functions[module->entry_index];
    log_detail("Binding entry point to Function Index: %d", module->entry_index);

    ObjClosure* entry_closure = newClosure(module->entry_index, entry_func->arity, 0);
    
    CeraValue closure_val;
    closure_val.tag = VAL_CLOSURE;
    closure_val.as.obj = (Obj*)entry_closure;
    push(vm, closure_val);

    // TODO: Loop through argc/argv and construct 'char list' linked lists.
    ObjArray* args_array = (ObjArray*)malloc(sizeof(ObjArray));
    args_array->header.type = VAL_ARRAY;
    args_array->header.ref_count = 1;
    args_array->length = 0;
    args_array->elements = NULL;
    
    CeraValue args_val;
    args_val.tag = VAL_ARRAY;
    args_val.as.obj = (Obj*)args_array;
    push(vm, args_val);

    CallFrame* frame = &vm->frames[vm->frame_count++];
    frame->closure = entry_closure;
    frame->ip = entry_func->code;
    
    // Index 0 is the closure itself, Index 1 is the args array.
    frame->slots = vm->stack; 
}

void freeVM(VM* vm) {
    log_info("Tearing down Virtual Machine...");
    
    while (vm->stack_top > vm->stack) {
        CeraValue popped = pop(vm);
        release(popped);
    }
}

#define BINARY_INT_OP(op) \
    do { \
        CeraValue b = pop(vm); \
        CeraValue a = pop(vm); \
        if (a.tag != VAL_INT || b.tag != VAL_INT) { \
            log_error("Operands must be integers"); \
            return 1; \
        } \
        CeraValue res; res.tag = VAL_INT; res.as.int_val = a.as.int_val op b.as.int_val; \
        push(vm, res); \
    } while (false)

#define BINARY_BOOL_OP(op) \
    do { \
        CeraValue b = pop(vm); \
        CeraValue a = pop(vm); \
        CeraValue res; res.tag = VAL_BOOL; \
        if (a.tag == VAL_INT && b.tag == VAL_INT) res.as.int_val = (a.as.int_val op b.as.int_val) ? 1 : 0; \
        else if (a.tag == VAL_FLOAT && b.tag == VAL_FLOAT) res.as.int_val = (a.as.float_val op b.as.float_val) ? 1 : 0; \
        else { log_error("Invalid types for comparison"); return 1; } \
        push(vm, res); \
    } while (false)

#define READ_BYTE() (*frame->ip++)
#define READ_SHORT() (frame->ip += 2, (uint16_t)((frame->ip[-2] << 8) | frame->ip[-1]))
#define READ_CONSTANT() (active_function->constants[READ_BYTE()])    
#define READ_CONSTANT_LONG() (active_function->constants[READ_SHORT()])
#define PEEK(distance) (vm->stack_top[-1 - (distance)])

int runVM(VM* vm) {
    CallFrame* frame = &vm->frames[vm->frame_count-1];
    CompiledFunction* active_function = &vm->active_module->functions[frame->closure->function_index];

    log_info("--- Execution Started ---");

    for (;;) {
        #ifdef DEBUG_TRACE_EXECUTION
        if (log_detailed) {
            dump_stack(vm->stack, vm->stack_top);
            int offset = (int)(frame->ip - active_function->code);
            log_detail("Executing OpCode at offset %04d", offset);
        }
        #endif

        uint8_t instruction = READ_BYTE();
        switch (instruction) {
            case OP_NOP: break;
            case OP_POP: 
                release(pop(vm));
                break;
            case OP_DUP: {
                CeraValue top = PEEK(0);
                retain(top);
                push(vm, top);
                break;
            }
            case OP_LOAD_CONST: {
                CeraValue constant = READ_CONSTANT();
                retain(constant);
                push(vm, constant);
                break;
            }
            case OP_LOAD_CONST_LONG: {
                CeraValue constant = READ_CONSTANT_LONG();
                retain(constant);
                push(vm, constant);
                break;
            }
            case OP_PUSH_0: {
                CeraValue v; v.tag = VAL_INT; v.as.int_val = 0;
                push(vm, v);
                break;
            }
            case OP_PUSH_1: {
                CeraValue v; v.tag = VAL_INT; v.as.int_val = 1;
                push(vm, v);
                break;
            }
            case OP_PUSH_BYTE: {
                CeraValue v; v.tag = VAL_INT; v.as.int_val = (int8_t)READ_BYTE();
                push(vm, v);
                break;
            }
            case OP_PUSH_TRUE: {
                CeraValue v; v.tag = VAL_BOOL; v.as.int_val = 1;
                push(vm, v);
                break;
            }
            case OP_PUSH_FALSE: {
                CeraValue v; v.tag = VAL_BOOL; v.as.int_val = 0;
                push(vm, v);
                break;
            }
            case OP_PUSH_UNIT: {
                CeraValue v; v.tag = VAL_UNIT; v.as.int_val = 0;
                push(vm, v);
                break;
            }
            case OP_PUSH_CHAR: {
                uint8_t b1 = READ_BYTE();
                uint8_t b2 = READ_BYTE();
                uint8_t b3 = READ_BYTE();
                uint8_t b4 = READ_BYTE();
                
                uint32_t char_val = (uint32_t)(b1 | (b2 << 8) | (b3 << 16) | (b4 << 24));
                
                CeraValue v; 
                v.tag = VAL_CHAR; 
                v.as.int_val = char_val;
                push(vm, v);
                break;
            }

            case OP_LOAD_LOCAL: {
                uint8_t slot = READ_BYTE();
                CeraValue local = frame -> slots[slot];
                retain(local);
                push(vm, local);
                break;
            }
            case OP_STORE_LOCAL: {
                uint8_t slot = READ_BYTE();
                release(frame->slots[slot]); 
                CeraValue value = PEEK(0);
                retain(value);
                frame->slots[slot] = value;
                break;
            }
            case OP_LOAD_FUNCTION: {
            uint8_t index = READ_BYTE();
                CeraValue v; v.tag = VAL_INT; v.as.int_val = index; // Storing index
                push(vm, v);
                break;
            }
            case OP_LOAD_FUNCTION_LONG: {
                uint16_t index = READ_SHORT();
                CeraValue v; v.tag = VAL_INT; v.as.int_val = index;
                push(vm, v);
                break;
            }
            case OP_LOAD_UPVALUE: {
                uint8_t slot = READ_BYTE();
                log_error("OP_LOAD_UPVALUE not implemented");
                return 1;
            }

            case OP_ADD: {
                CeraValue b = pop(vm);
                CeraValue a = pop(vm);
                CeraValue res;
                if (a.tag == VAL_INT && b.tag == VAL_INT) { res.tag = VAL_INT; res.as.int_val = a.as.int_val + b.as.int_val; } 
                else if (a.tag == VAL_FLOAT && b.tag == VAL_FLOAT) { res.tag = VAL_FLOAT; res.as.float_val = a.as.float_val + b.as.float_val; } 
                else { log_error("Operands for '+' must be numbers"); return 1; }
                push(vm, res);
                break;
            }
            case OP_SUB: {
                CeraValue b = pop(vm); CeraValue a = pop(vm); CeraValue res;
                if (a.tag == VAL_INT && b.tag == VAL_INT) { res.tag = VAL_INT; res.as.int_val = a.as.int_val - b.as.int_val; } 
                else if (a.tag == VAL_FLOAT && b.tag == VAL_FLOAT) { res.tag = VAL_FLOAT; res.as.float_val = a.as.float_val - b.as.float_val; } 
                else { log_error("Operands for '-' must be numbers"); return 1; }
                push(vm, res); break;
            }
            case OP_MUL: {
                CeraValue b = pop(vm); CeraValue a = pop(vm); CeraValue res;
                if (a.tag == VAL_INT && b.tag == VAL_INT) { res.tag = VAL_INT; res.as.int_val = a.as.int_val * b.as.int_val; } 
                else if (a.tag == VAL_FLOAT && b.tag == VAL_FLOAT) { res.tag = VAL_FLOAT; res.as.float_val = a.as.float_val * b.as.float_val; } 
                else { log_error("Operands for '*' must be numbers"); return 1; }
                push(vm, res); break;
            }
            case OP_DIV: {
                CeraValue b = pop(vm); CeraValue a = pop(vm); CeraValue res;
                if (a.tag == VAL_INT && b.tag == VAL_INT) { res.tag = VAL_INT; res.as.int_val = a.as.int_val / b.as.int_val; } 
                else if (a.tag == VAL_FLOAT && b.tag == VAL_FLOAT) { res.tag = VAL_FLOAT; res.as.float_val = a.as.float_val / b.as.float_val; } 
                else { log_error("Operands for '/' must be numbers"); return 1; }
                push(vm, res); break;
            }
            case OP_MOD: BINARY_INT_OP(%); break;
            case OP_NEGATE: {
                CeraValue a = pop(vm);
                if (a.tag == VAL_INT) a.as.int_val = -a.as.int_val;
                else if (a.tag == VAL_FLOAT) a.as.float_val = -a.as.float_val;
                else { log_error("Cannot negate non-number"); return 1; }
                push(vm, a);
                break;
            }
            case OP_BIT_AND: BINARY_INT_OP(&); break;
            case OP_BIT_OR:  BINARY_INT_OP(|); break;
            case OP_BIT_XOR: BINARY_INT_OP(^); break;
            case OP_SHL:     BINARY_INT_OP(<<); break;
            case OP_SHR:     BINARY_INT_OP(>>); break;
            case OP_BIT_NOT: {
                CeraValue a = pop(vm);
                if (a.tag != VAL_INT) { log_error("Operand must be int"); return 1; }
                a.as.int_val = ~a.as.int_val;
                push(vm, a);
                break;
            }

            case OP_EQ:  BINARY_BOOL_OP(==); break;
            case OP_NEQ: BINARY_BOOL_OP(!=); break;
            case OP_LT:  BINARY_BOOL_OP(<); break;
            case OP_GT:  BINARY_BOOL_OP(>); break;
            case OP_LTE: BINARY_BOOL_OP(<=); break;
            case OP_GTE: BINARY_BOOL_OP(>=); break;
            case OP_NOT: {
                CeraValue a = pop(vm);
                if (a.tag != VAL_BOOL) { log_error("Operand must be bool"); return 1; }
                a.as.int_val = (a.as.int_val == 0) ? 1 : 0;
                push(vm, a);
                break;
            }

            case OP_JUMP: {
                uint16_t offset = READ_SHORT();
                frame->ip += offset;
                break;
            }
            case OP_JUMP_IF_FALSE: {
                uint16_t offset = READ_SHORT();
                CeraValue condition = pop(vm);
                if (condition.as.int_val == 0) frame->ip += offset;
                break;
            }
            case OP_JUMP_IF_TRUE: {
                uint16_t offset = READ_SHORT();
                CeraValue condition = pop(vm);
                if (condition.as.int_val == 1) frame->ip += offset;
                break;
            }

            case OP_RETURN: {
                CeraValue result = pop(vm);
                
                // TODO: Close upvalues logic
                
                vm->frame_count--;
                if (vm->frame_count == 0) {
                    log_info("Program Terminated Cleanly.");
                    return 0; 
                }

                while (vm->stack_top > frame->slots) {
                    release(pop(vm));
                }
                
                push(vm, result); 
                
                frame = &vm->frames[vm->frame_count - 1];
                active_function = &vm->active_module->functions[frame->closure->function_index];
                break;
            }
            
            case OP_CALL:
            case OP_CALL_INTRINSIC:
            case OP_TAIL_CALL:
            case OP_MAKE_CLOSURE:
                log_error("Function Call/Closure operations not yet implemented");
                return 1;

            case OP_ALLOC_CON:
            case OP_ALLOC_TUPLE:
            case OP_ALLOC_ARRAY:
            case OP_ALLOC_ARRAY_LONG:
            case OP_LIST_EMPTY:
            case OP_LIST_CONS:
                log_error("Heap Allocation operations not yet implemented");
                return 1;

            case OP_MATCH_TAG:
            case OP_UNPACK_CON:
            case OP_UNPACK_TUPLE:
            case OP_UNPACK_LIST:
            case OP_IS_LIST_EMPTY:
            case OP_MATCH_ARRAY_LENGTH:
            case OP_UNPACK_ARRAY:
            case OP_MATCH_FAIL:
                log_error("Pattern Matching operations not yet implemented");
                return 1;

            default:
                log_error("Fatal: Unknown opcode 0x%02X at offset %d", instruction, (int)(frame->ip - active_function->code - 1));
                return 1;
        }
    }
}

#undef READ_BYTE
#undef READ_SHORT
#undef READ_CONSTANT
#undef READ_CONSTANT_LONG
#undef PEEK
#undef BINARY_INT_OP
#undef BINARY_BOOL_OP