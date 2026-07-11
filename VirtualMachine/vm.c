#include <stdio.h>
#include <stdlib.h>
#include <string.h>
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
        if (a.tag != b.tag) { \
            log_error("Type mismatch during comparison"); return 1; \
        } \
        if (a.tag == VAL_INT || a.tag == VAL_BOOL || a.tag == VAL_CHAR || a.tag == VAL_UNIT) { \
            res.as.int_val = (a.as.int_val op b.as.int_val) ? 1 : 0; \
        } \
        else if (a.tag == VAL_FLOAT) { \
            res.as.int_val = (a.as.float_val op b.as.float_val) ? 1 : 0; \
        } \
        else if (a.tag == VAL_STRING) { \
            ObjString* strA = (ObjString*)a.as.obj; \
            ObjString* strB = (ObjString*)b.as.obj; \
            res.as.int_val = (strcmp(strA->chars, strB->chars) op 0) ? 1 : 0; \
        } \
        else { \
            log_error("Invalid or unimplemented types for comparison"); return 1; \
        } \
        push(vm, res); \
    } while (false)

#define READ_BYTE() (*frame->ip++)
#define READ_SHORT() (frame->ip += 2, (uint16_t)((frame->ip[-2] << 8) | frame->ip[-1]))
#define READ_CONSTANT() (active_function->constants[READ_BYTE()])    
#define READ_CONSTANT_LONG() (active_function->constants[READ_SHORT()])
#define PEEK(distance) (vm->stack_top[-1 - (distance)])

static CompiledFunction* get_function(Module* module, int target_index) {
    for (uint32_t i = 0; i < module->function_count; i++) {
        if (module->functions[i].index == target_index) {
            return &module->functions[i];
        }
    }
    return NULL; // Should never be reached
}

static int call_function(VM* vm, ObjClosure* closure, uint8_t arg_count) {
    CompiledFunction* func = get_function(vm->active_module, closure->function_index);
    
    if (func == NULL) {
        log_error("Fatal: Attempted to call unknown function index %d.", closure->function_index);
        return 1;
    }
    
    if (func->code_size == 0) {
        log_error("Fatal: Function %d has 0 bytes of bytecode compiled.", func->index);
        return 1;
    }

    if (arg_count != func->arity) {
        log_error("Expected %d arguments but got %d.", func->arity, arg_count);
        return 1;
    }

    if (vm->frame_count == FRAMES_MAX) {
        log_error("Stack overflow. Infinite recursion detected.");
        return 1;
    }

    CallFrame* frame = &vm->frames[vm->frame_count++];
    frame->closure = closure;
    frame->ip = func->code;
    
    frame->slots = vm->stack_top - arg_count - 1;
    return 0; 
}

static int execute_intrinsic(VM* vm, uint8_t intrinsic_id, uint8_t arg_count) {
    switch (intrinsic_id) {
        case INTR_OUT: {
            if (arg_count != 1) { log_error("out() expects exactly 1 argument."); return 1; }
            CeraValue arg = pop(vm);
            
            if (arg.tag == VAL_STRING) {
                ObjString* str = (ObjString*)arg.as.obj;                
                printf(ANSI_COLOR_WHITE "%s" ANSI_COLOR_RESET, str->chars);                
                fflush(stdout); 
                
            } else {
                log_error("out() currently only supports native Strings.");
                return 1;
            }
            
            release(arg); 
            CeraValue unit; 
            unit.tag = VAL_UNIT; 
            unit.as.int_val = 0;
            push(vm, unit);
            return 0; 
        }   
        case INTR_IN: {
            char buffer[1024];
            if (fgets(buffer, sizeof(buffer), stdin) == NULL) {
                buffer[0] = '\0';
            } else {
                size_t len = strlen(buffer);
                if (len > 0 && buffer[len-1] == '\n') {
                    buffer[len-1] = '\0';
                    len--;
                }
            }
            
            size_t final_len = strlen(buffer);
            ObjString* str = malloc(sizeof(ObjString));
            str->header.type = VAL_STRING;
            str->header.ref_count = 1; 
            str->length = final_len;
            
            str->chars = malloc(final_len + 1);
            strcpy(str->chars, buffer);
            
            CeraValue result;
            result.tag = VAL_STRING;
            result.as.obj = (Obj*)str;
            
            push(vm, result);
            return 0; 
        }

        case INTR_INT_TO_FLOAT: {
            CeraValue arg = pop(vm);
            CeraValue result;
            result.tag = VAL_FLOAT;
            result.as.float_val = (double)arg.as.int_val;
            push(vm, result);
            return 0;
        }

        case INTR_FLOAT_TO_INT: {
            CeraValue arg = pop(vm);
            CeraValue result;
            result.tag = VAL_INT;
            result.as.int_val = (int64_t)arg.as.float_val; 
            push(vm, result);
            return 0;
        }

        case INTR_CHAR_TO_INT: {
            CeraValue arg = pop(vm);
            CeraValue result;
            result.tag = VAL_INT;
            result.as.int_val = arg.as.int_val; 
            push(vm, result);
            return 0;
        }

        case INTR_INT_TO_CHAR: {
            CeraValue arg = pop(vm);
            CeraValue result;
            result.tag = VAL_CHAR;
            result.as.int_val = arg.as.int_val;
            push(vm, result);
            return 0;
        }

        case INTR_INT_TO_CHARS: {
            CeraValue arg = pop(vm);
            
            char buffer[64];
            int len = snprintf(buffer, sizeof(buffer), "%ld", (long)arg.as.int_val);
            
            ObjString* str = malloc(sizeof(ObjString));
            str->header.type = VAL_STRING;
            str->header.ref_count = 1; 
            str->length = len;
            
            str->chars = malloc(len + 1);
            strcpy(str->chars, buffer);
            
            CeraValue result;
            result.tag = VAL_STRING;
            result.as.obj = (Obj*)str;
            
            push(vm, result);
            return 0;
        }

        case INTR_BOOL_TO_CHARS: {
            CeraValue arg = pop(vm);
            
            const char* text = arg.as.int_val ? "true" : "false";
            int len = strlen(text);
            
            ObjString* str = malloc(sizeof(ObjString));
            str->header.type = VAL_STRING;
            str->header.ref_count = 1;
            str->length = len;
            
            str->chars = malloc(len + 1);
            strcpy(str->chars, text);
            
            CeraValue result;
            result.tag = VAL_STRING;
            result.as.obj = (Obj*)str;
            
            push(vm, result);
            return 0;
        }
        case INTR_FLOAT_TO_CHARS: {
            if (arg_count != 1) { log_error("floatToChars() expects exactly 1 argument."); return 1; }
            
            CeraValue arg = pop(vm);
            if (arg.tag != VAL_FLOAT) { log_error("floatToChars() requires a float."); return 1; }
            
            char buffer[64];
            int len = snprintf(buffer, sizeof(buffer), "%g", arg.as.float_val);
            
            ObjString* str = malloc(sizeof(ObjString));
            str->header.type = VAL_STRING;
            str->header.ref_count = 1; 
            str->length = len;
            str->chars = malloc(len + 1);
            strcpy(str->chars, buffer);
            
            CeraValue result;
            result.tag = VAL_STRING;
            result.as.obj = (Obj*)str;
            
            push(vm, result);
            return 0; 
        }
        case INTR_CONCAT: {
            CeraValue right = pop(vm);
            CeraValue left = pop(vm);
            
            // Intercept standard String concatenation
            if (left.tag == VAL_STRING && right.tag == VAL_STRING) {
                ObjString* s1 = (ObjString*)left.as.obj;
                ObjString* s2 = (ObjString*)right.as.obj;
                
                uint32_t len = s1->length + s2->length;
                ObjString* new_str = malloc(sizeof(ObjString));
                new_str->header.type = VAL_STRING;
                new_str->header.ref_count = 1;
                new_str->length = len;
                new_str->chars = malloc(len + 1);
                
                strcpy(new_str->chars, s1->chars);
                strcat(new_str->chars, s2->chars);
                
                CeraValue res; 
                res.tag = VAL_STRING; 
                res.as.obj = (Obj*)new_str;
                
                push(vm, res);
                return 0;
            }
            
            // Fallback for standard list concatenation (to be implemented later)
            log_error("List concatenation not fully implemented for non-strings yet");
            return 1;
        }

        default:
            log_error("Fatal: Unimplemented intrinsic ID: %d", intrinsic_id);
            return 1; 
    }
}

static void close_upvalues(VM* vm, CeraValue* last) {
    while (vm->open_upvalues != NULL && vm->open_upvalues->location >= last) {
        ObjUpvalue* upvalue = vm->open_upvalues;
        
        upvalue->closed_value = *upvalue->location;        
        upvalue->location = &upvalue->closed_value;        
        vm->open_upvalues = upvalue->next;
    }
}

static ObjUpvalue* capture_upvalue(VM* vm, CeraValue* local) {
    ObjUpvalue* prev_upvalue = NULL;
    ObjUpvalue* upvalue = vm->open_upvalues;

    while (upvalue != NULL && upvalue->location > local) {
        prev_upvalue = upvalue;
        upvalue = upvalue->next;
    }

    if (upvalue != NULL && upvalue->location == local) {
        upvalue -> header.ref_count++;
        return upvalue;
    }

    ObjUpvalue* created_upvalue = malloc(sizeof(ObjUpvalue));
    created_upvalue->header.type = 0; // Upvalues are internal 
    created_upvalue->header.ref_count = 1;
    created_upvalue->location = local;
    
    created_upvalue->closed_value.tag = VAL_INT; 
    created_upvalue->closed_value.as.int_val = 0;
    created_upvalue->next = upvalue;

    if (prev_upvalue == NULL) {
        vm->open_upvalues = created_upvalue;
    } else {
        prev_upvalue->next = created_upvalue;
    }

    return created_upvalue;
}

int runVM(VM* vm) {
    CallFrame* frame = &vm->frames[vm->frame_count-1];
    CompiledFunction* active_function = get_function(vm->active_module, frame->closure->function_index);

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
                CeraValue upvalue = *frame->closure->upvalues[slot]->location;
                retain(upvalue);
                push(vm, upvalue);
                break;
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
                close_upvalues(vm, frame->slots);
                
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
                active_function = get_function(vm->active_module, frame->closure->function_index);
                break;
            }
            
            case OP_CALL: {
                uint8_t arg_count = READ_BYTE();
                CeraValue callee = PEEK(arg_count);

                if (callee.tag != VAL_CLOSURE) {
                    log_error("Attempted to call a non-function value.");
                    return 1;
                }
                if (call_function(vm, (ObjClosure*)callee.as.obj, arg_count) != 0) {
                    return 1; 
                }             

                frame = &vm->frames[vm->frame_count - 1];
                active_function = get_function(vm->active_module, frame->closure->function_index);
                break;
            }

            case OP_CALL_INTRINSIC:
                uint8_t intrinsic_id = READ_BYTE();
                uint8_t arg_count = READ_BYTE();                
                if (execute_intrinsic(vm, intrinsic_id, arg_count) != 0) {
                    return 1; 
                }
                break;

            case OP_TAIL_CALL: {
                uint8_t arg_count = READ_BYTE();
                CeraValue callee = PEEK(arg_count);
                
                if (callee.tag != VAL_CLOSURE) {
                    log_error("Attempted to tail-call a non-function.");
                    return 1;
                }
                
                ObjClosure* closure = (ObjClosure*)callee.as.obj;
                CompiledFunction* func = get_function(vm->active_module, closure->function_index);
                
                if (func == NULL) {
                    log_error("Fatal: Unknown function index %d in tail call.", closure->function_index);
                    return 1;
                }
                if (func->code_size == 0) {
                    log_error("Fatal: Function %d has 0 bytes of bytecode.", func->index);
                    return 1;
                }
                
                if (arg_count != func->arity) {
                    log_error("Expected %d arguments but got %d.", func->arity, arg_count);
                    return 1;
                }

                frame->closure = closure;
                frame->ip = func->code;
                
                // Shift the new arguments down the stack to replace the current function's locals
                for (int i = 0; i <= arg_count; i++) {
                    frame->slots[i] = PEEK(arg_count - i);
                }
                vm->stack_top = frame->slots + arg_count + 1;                
                active_function = func;
                break;
            }
            case OP_MAKE_CLOSURE: {                
                uint8_t upvalue_count = READ_BYTE();                
                CeraValue func_val = pop(vm);
                if (func_val.tag != VAL_INT) {
                    log_error("Runtime Error: OP_MAKE_CLOSURE expected int on stack.");
                    return 1;
                }
                
                uint32_t func_index = (uint32_t)func_val.as.int_val;
                CompiledFunction* function = get_function(vm->active_module, func_index);
                
                if (function == NULL) {
                    log_error("Fatal: Attempted to make closure for unknown function index %d", func_index);
                    return 1;
                }
                
                ObjClosure* closure = newClosure(func_index, function->arity, upvalue_count);                
                
                for (int i = 0; i < upvalue_count; i++) {
                    uint8_t is_local = READ_BYTE();
                    uint8_t index = READ_BYTE();

                    if (is_local) {
                        closure->upvalues[i] = capture_upvalue(vm, frame->slots + index);
                    } else {
                        closure->upvalues[i] = frame->closure->upvalues[index];
                    }
                }
                
                CeraValue closure_val;
                closure_val.tag = VAL_CLOSURE;
                closure_val.as.obj = (Obj*)closure;
                push(vm, closure_val);
                break;
            }
            case OP_ALLOC_CON: {
                uint8_t tag_id = READ_BYTE();
                CeraValue payload = pop(vm);
                
                ObjADT* adt = malloc(sizeof(ObjADT));
                adt->header.type = VAL_ADT;
                adt->header.ref_count = 1;
                adt->adt_tag = tag_id;
                
                retain(payload); 
                adt->payload = payload;
                
                CeraValue res;
                res.tag = VAL_ADT;
                res.as.obj = (Obj*)adt;
                push(vm, res);
                break;
            }
            case OP_ALLOC_TUPLE: {
                uint8_t size = READ_BYTE();
                ObjTuple* tuple = malloc(sizeof(ObjTuple));
                tuple->header.type = VAL_TUPLE;
                tuple->header.ref_count = 1;
                tuple->length = size;
                tuple->elements = malloc(sizeof(CeraValue) * size);
                
                for (int i = size - 1; i >= 0; i--) {
                    CeraValue elem = pop(vm);
                    retain(elem);
                    tuple->elements[i] = elem;
                }
                
                CeraValue res;
                res.tag = VAL_TUPLE;
                res.as.obj = (Obj*)tuple;
                push(vm, res);
                break;
            }

            case OP_ALLOC_ARRAY: {
                uint8_t size = READ_BYTE();
                ObjArray* arr = malloc(sizeof(ObjArray));
                arr->header.type = VAL_ARRAY;
                arr->header.ref_count = 1;
                arr->length = size;
                arr->elements = malloc(sizeof(CeraValue) * size);
                
                for (int i = size - 1; i >= 0; i--) {
                    CeraValue elem = pop(vm);
                    retain(elem);
                    arr->elements[i] = elem;
                }
                
                CeraValue res;
                res.tag = VAL_ARRAY;
                res.as.obj = (Obj*)arr;
                push(vm, res);
                break;
            }

            case OP_ALLOC_ARRAY_LONG: {
                uint16_t size = READ_SHORT(); // Extract 16-bit payload
                ObjArray* arr = malloc(sizeof(ObjArray));
                arr->header.type = VAL_ARRAY;
                arr->header.ref_count = 1;
                arr->length = size;
                arr->elements = malloc(sizeof(CeraValue) * size);
                
                for (int i = size - 1; i >= 0; i--) {
                    CeraValue elem = pop(vm);
                    retain(elem);
                    arr->elements[i] = elem;
                }
                
                CeraValue res;
                res.tag = VAL_ARRAY;
                res.as.obj = (Obj*)arr;
                push(vm, res);
                break;
            }

            case OP_LIST_EMPTY: {
                CeraValue res; 
                res.tag = VAL_LIST; 
                res.as.obj = NULL; 
                push(vm, res);
                break;
            }

            case OP_LIST_CONS: {
                CeraValue tail = pop(vm);
                CeraValue head = pop(vm);
                
                ObjList* list = malloc(sizeof(ObjList));
                list->header.type = VAL_LIST;
                list->header.ref_count = 1;
                
                retain(head);
                retain(tail);
                list->head = head;
                list->tail = tail;
                
                CeraValue res; 
                res.tag = VAL_LIST; 
                res.as.obj = (Obj*)list;
                push(vm, res);
                break;
            }

            case OP_MATCH_TAG: {
                uint8_t tag_id = READ_BYTE();
                CeraValue top = PEEK(0);
                CeraValue res; 
                res.tag = VAL_BOOL;
                
                if (top.tag == VAL_ADT && ((ObjADT*)top.as.obj)->adt_tag == tag_id) {
                    res.as.int_val = 1;
                } else {
                    res.as.int_val = 0;
                }
                push(vm, res);
                break;
            }

            case OP_UNPACK_CON: {
                CeraValue val = pop(vm);
                ObjADT* adt = (ObjADT*)val.as.obj;
                CeraValue payload = adt->payload;
                
                retain(payload);
                release(val);
                
                push(vm, payload);
                break;
            }

            case OP_UNPACK_TUPLE: {
                CeraValue val = pop(vm);
                ObjTuple* tuple = (ObjTuple*)val.as.obj;
                
                // Push forward: elements[0] is correctly assigned to the first local slot
                for (int i = 0; i < tuple->length; i++) {
                    CeraValue elem = tuple->elements[i];
                    retain(elem);
                    push(vm, elem);
                }
                release(val);
                break;
            }

            case OP_UNPACK_LIST: {
                CeraValue val = pop(vm);
                ObjList* list = (ObjList*)val.as.obj;
                
                CeraValue head = list->head;
                CeraValue tail = list->tail;
                
                retain(head);
                retain(tail);
                release(val);
                
                // Push head then tail to align with left-to-right variable slots
                push(vm, head);
                push(vm, tail);
                break;
            }

            case OP_UNPACK_ARRAY: {
                CeraValue val = pop(vm);
                ObjArray* arr = (ObjArray*)val.as.obj;
                
                for (int i = 0; i < arr->length; i++) {
                    CeraValue elem = arr->elements[i];
                    retain(elem);
                    push(vm, elem);
                }
                release(val); 
                break;
            }

            case OP_IS_LIST_EMPTY: {
                CeraValue top = PEEK(0);
                CeraValue res; 
                res.tag = VAL_BOOL;
                
                res.as.int_val = (top.tag == VAL_LIST && top.as.obj == NULL) ? 1 : 0;
                push(vm, res);
                break;
            }

            case OP_MATCH_ARRAY_LENGTH: {
                CeraValue expected_len = pop(vm); 
                CeraValue top = PEEK(0);          
                CeraValue res; 
                res.tag = VAL_BOOL;
                
                if (top.tag == VAL_ARRAY && ((ObjArray*)top.as.obj)->length == expected_len.as.int_val) {
                    res.as.int_val = 1;
                } else {
                    res.as.int_val = 0;
                }
                push(vm, res);
                break;
            }

            case OP_MATCH_FAIL: {
                log_error("Non-exhaustive pattern match fallthrough.");
                return 1;
            }

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