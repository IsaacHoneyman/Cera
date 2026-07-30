#define _GNU_SOURCE
#define _POSIX_C_SOURCE 200809L

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
#include <ctype.h>
#include "vm.h"
#include "memory.h"
#include "logger.h"
#include "intrinsic.h"
#ifdef _WIN32 
#include <windows.h>
#endif

// #define DEBUG_TRACE_EXECUTION // to comment out for actual builds

void initVM(VM *vm, Module *module, int argc, char **argv)
{
    log_info("Initializing Virtual Machine execution state...");

    extern int max_system_threads; 
    if (max_system_threads == 0) {
#ifdef _WIN32
        SYSTEM_INFO sysinfo;
        GetSystemInfo(&sysinfo);
        max_system_threads = sysinfo.dwNumberOfProcessors;
#elif defined(_SC_NPROCESSORS_ONLN)
        max_system_threads = (int)sysconf(_SC_NPROCESSORS_ONLN);
#else
        max_system_threads = 4; // Safe universal fallback
#endif
        if (max_system_threads < 1) max_system_threads = 4; 
    }
    log_info("Hardware thread limit dynamically set to: %d", max_system_threads);

    srand((unsigned int)time(NULL));

    vm->stack_top = vm->stack;
    vm->frame_count = 0;
    vm->active_module = module;
    vm->open_upvalues = NULL;

    if (module->entry_index == -1)
    {
        log_error("Fatal: No entry point defined in module");
        exit(1);
    }

    CompiledFunction *entry_func = &module->functions[module->entry_index];
    log_detail("Binding entry point to Function Index: %d", module->entry_index);

    ObjClosure *entry_closure = newClosure(module->entry_index, entry_func->arity, 0);

    CeraValue closure_val;
    closure_val.tag = VAL_CLOSURE;
    closure_val.as.obj = (Obj *)entry_closure;
    push(vm, closure_val);

    ObjArray *args_array = newArray(argc);

    for (int i = 0; i < argc; i++)
    {
        CeraValue arg_val;
        arg_val.tag = VAL_STRING;
        arg_val.as.obj = (Obj *)newString(argv[i]);
        args_array->elements[i] = arg_val;
    }

    CeraValue args_val;
    args_val.tag = VAL_ARRAY;
    args_val.as.obj = (Obj *)args_array;
    push(vm, args_val);

    CallFrame *frame = &vm->frames[vm->frame_count++];
    frame->closure = entry_closure;
    frame->function_index = module->entry_index;
    frame->ip = entry_func->code;

    frame->slots = vm->stack;
}

#define BINARY_INT_OP(op) \
    do { \
        CeraValue b = pop(vm); \
        CeraValue a = pop(vm); \
        CeraValue res; \
        res.tag = VAL_INT; \
        res.as.int_val = a.as.int_val op b.as.int_val; \
        push(vm, res); \
    } while (false)

#define BINARY_BOOL_OP(op) \
    do \
    { \
        CeraValue b = pop(vm); \
        CeraValue a = pop(vm); \
        CeraValue res; \
        res.tag = VAL_BOOL; \
        bool a_is_str_like = (a.tag == VAL_STRING || a.tag == VAL_LIST); \
        bool b_is_str_like = (b.tag == VAL_STRING || b.tag == VAL_LIST); \
        if (a_is_str_like && b_is_str_like) \
        { \
            char *strA = flatten_char_list(a); \
            char *strB = flatten_char_list(b); \
            res.as.int_val = (strcmp(strA, strB) op 0) ? 1 : 0; \
            free(strA); \
            free(strB); \
        } \
        else if (a.tag != b.tag) \
        { \
            log_error("Type mismatch during comparison: left tag=%d right tag=%d", a.tag, b.tag); \
            return 1; \
        } \
        else if (a.tag == VAL_INT || a.tag == VAL_BOOL || a.tag == VAL_CHAR || a.tag == VAL_UNIT) \
        { \
            res.as.int_val = (a.as.int_val op b.as.int_val) ? 1 : 0; \
        } \
        else if (a.tag == VAL_FLOAT) \
        { \
            res.as.int_val = (a.as.float_val op b.as.float_val) ? 1 : 0; \
        } \
        else \
        { \
            RUNTIME_ERROR(vm, "Invalid or unimplemented types for comparison. Received tags: %d, %d", a.tag, b.tag); \
        } \
        release(a); \
        release(b); \
        push(vm, res); \
    } while (false)

#define BINARY_NUM_OP(op) \
    do { \
        CeraValue b = pop(vm); \
        CeraValue a = pop(vm); \
        CeraValue res; \
        if (a.tag == VAL_FLOAT) { \
            res.tag = VAL_FLOAT; \
            res.as.float_val = a.as.float_val op b.as.float_val; \
        } else { \
            res.tag = VAL_INT; \
            res.as.int_val = a.as.int_val op b.as.int_val; \
        } \
        push(vm, res); \
    } while (false)

#define FUSED_RELATIONAL_JUMP(op) \
    do { \
        uint16_t offset = READ_SHORT(); \
        CeraValue b = pop(vm); \
        CeraValue a = pop(vm); \
        bool cond = false; \
        if (a.tag == VAL_STRING || a.tag == VAL_LIST) { \
            char *strA = flatten_char_list(a); \
            char *strB = flatten_char_list(b); \
            cond = (strcmp(strA, strB) op 0); \
            free(strA); free(strB); \
        } else if (a.tag == VAL_FLOAT) { \
            cond = (a.as.float_val op b.as.float_val); \
        } else { \
            cond = (a.as.int_val op b.as.int_val); \
        } \
        release(a); release(b); \
        if (!cond) frame->ip += offset; \
    } while (false)

#define READ_BYTE() (*frame->ip++)
#define READ_SHORT() (frame->ip += 2, (uint16_t)((frame->ip[-2] << 8) | frame->ip[-1]))
#define READ_CONSTANT() (active_function->constants[READ_BYTE()])
#define READ_CONSTANT_LONG() (active_function->constants[READ_SHORT()])
#define PEEK(distance) (vm->stack_top[-1 - (distance)])

static CompiledFunction *get_function(Module *module, int target_index)
{
    for (uint32_t i = 0; i < module->function_count; i++)
    {
        if (module->functions[i].index == target_index)
        {
            return &module->functions[i];
        }
    }
    return NULL; 
}

static int call_function(VM *vm, ObjClosure *closure, uint8_t arg_count)
{
    CompiledFunction *func = get_function(vm->active_module, closure->function_index);

    if (vm->frame_count == FRAMES_MAX)
    {
        log_error("Stack overflow, infinite recursion detected");
        return 1;
    }

    CallFrame *frame = &vm->frames[vm->frame_count++];
    frame->closure = closure;
    frame->function_index = closure->function_index; 
    frame->ip = func->code;
    frame->slots = vm->stack_top - arg_count - 1;
    return 0;
}

static int call_static_function(VM *vm, uint32_t func_index, uint8_t arg_count)
{
    CompiledFunction *func = get_function(vm->active_module, func_index);

    if (vm->frame_count == FRAMES_MAX)
    {
        log_error("Stack overflow, infinite recursion detected");
        return 1;
    }

    CallFrame *frame = &vm->frames[vm->frame_count++];
    frame->closure = NULL; // No dynamic memory
    frame->function_index = func_index;
    frame->ip = func->code;
    frame->slots = vm->stack_top - arg_count - 1;

    return 0;
}

char *flatten_char_list(CeraValue val)
{
    if (val.tag == VAL_STRING)
    {
        ObjString *str = (ObjString *)val.as.obj;
        char *buf = malloc(str->length + 1);
        strcpy(buf, str->chars);
        return buf;
    }

    if (val.tag == VAL_LIST)
    {
        int len = 0;
        ObjList *curr = (ObjList *)val.as.obj;

        while (curr != NULL)
        {
            len++;
            if (curr->tail.tag != VAL_LIST)
                break;
            curr = (ObjList *)curr->tail.as.obj;
        }

        char *buf = malloc(len + 1);
        curr = (ObjList *)val.as.obj;

        for (int i = 0; i < len; i++)
        {
            if (curr == NULL)
                break;
            buf[i] = (char)curr->head.as.int_val;
            if (curr->tail.tag != VAL_LIST)
                break;
            curr = (ObjList *)curr->tail.as.obj;
        }
        buf[len] = '\0';
        return buf;
    }

    char *buf = malloc(1);
    buf[0] = '\0';
    return buf;
}

static void close_upvalues(VM *vm, CeraValue *last)
{
    while (vm->open_upvalues != NULL && vm->open_upvalues->location >= last)
    {
        ObjUpvalue *upvalue = vm->open_upvalues;

        upvalue->closed_value = *upvalue->location;
        retain(upvalue->closed_value); 
        upvalue->location = &upvalue->closed_value;
        vm->open_upvalues = upvalue->next;

        upvalue->header.ref_count--;
        if (upvalue->header.ref_count == 0)
        {
            release(upvalue->closed_value);
            free(upvalue);
        }
    }
}

static ObjUpvalue *capture_upvalue(VM *vm, CeraValue *local)
{
    ObjUpvalue *prev_upvalue = NULL;
    ObjUpvalue *upvalue = vm->open_upvalues;

    while (upvalue != NULL && upvalue->location > local)
    {
        prev_upvalue = upvalue;
        upvalue = upvalue->next;
    }

    if (upvalue != NULL && upvalue->location == local)
    {
        upvalue->header.ref_count++; 
        return upvalue;
    }

    ObjUpvalue *created_upvalue = malloc(sizeof(ObjUpvalue));
    created_upvalue->header.type = 0;

    created_upvalue->header.ref_count = 2;
    
    created_upvalue->header.is_arena = 0;
    created_upvalue->header.is_pinned = 0;

    created_upvalue->location = local;
    created_upvalue->closed_value.tag = VAL_INT;
    created_upvalue->closed_value.as.int_val = 0;
    created_upvalue->next = upvalue;

    if (prev_upvalue == NULL)
    {
        vm->open_upvalues = created_upvalue;
    }
    else
    {
        prev_upvalue->next = created_upvalue;
    }

    return created_upvalue;
}

static inline void populate_seq_from_stack(VM *vm, CeraValue *elements, int length)
{
    for (int i = length - 1; i >= 0; i--)
    {
        elements[i] = pop(vm);
    }
}

void freeVM(VM *vm)
{
    log_info("Tearing down Virtual Machine...");

    close_upvalues(vm, vm->stack);

    while (vm->stack_top > vm->stack)
    {
        CeraValue popped = pop(vm);
        release(popped);
    }
}

static void print_stack_trace(VM *vm)
{
    log_error("--- CeraVM Stack Trace ---");
    for (int i = vm->frame_count - 1; i >= 0; i--)
    {
        CallFrame *frame = &vm->frames[i];
        CompiledFunction *func = get_function(vm->active_module, frame->function_index);

        int offset = (int)(frame->ip - func->code - 1);
        log_error("  [Frame %d] Function Index: %d at instruction offset: %04d", i, func->index, offset);
    }
}

#define RUNTIME_ERROR(vm, fmt, ...) \
    do \
    { \
        log_error(fmt, ##__VA_ARGS__); \
        print_stack_trace(vm); \
        return 1; \
    } while (false)

int runVM(VM *vm)
{
    CallFrame *frame = &vm->frames[vm->frame_count - 1];
    CompiledFunction *active_function = get_function(vm->active_module, frame->function_index);

    for (;;)
    {

        uint8_t instruction = READ_BYTE();

#ifdef DEBUG_TRACE_EXECUTION
        if (log_detailed)
        {
            dump_stack(vm->stack, vm->stack_top);
            int offset = (int)(frame->ip - active_function->code);
            log_detail("Executing OpCode %02x at offset %04d", instruction, offset);
        }
#endif

        switch (instruction)
        {
        case OP_NOP: break;
        case OP_POP: release(pop(vm)); break;

        case OP_LOAD_CONST:
        {
            CeraValue constant = READ_CONSTANT();
            retain(constant);
            push(vm, constant);
            break;
        }
        
        case OP_LOAD_CONST_LONG:
        {
            CeraValue constant = READ_CONSTANT_LONG();
            retain(constant);
            push(vm, constant);
            break;
        }
        
        case OP_PUSH_0:
        {
            CeraValue v;
            v.tag = VAL_INT;
            v.as.int_val = 0;
            push(vm, v);
            break;
        }
        
        case OP_PUSH_1:
        {
            CeraValue v;
            v.tag = VAL_INT;
            v.as.int_val = 1;
            push(vm, v);
            break;
        }
        
        case OP_PUSH_BYTE:
        {
            CeraValue v;
            v.tag = VAL_INT;
            v.as.int_val = (int8_t)READ_BYTE();
            push(vm, v);
            break;
        }
        
        case OP_PUSH_TRUE:
        {
            CeraValue v;
            v.tag = VAL_BOOL;
            v.as.int_val = 1;
            push(vm, v);
            break;
        }
        
        case OP_PUSH_FALSE:
        {
            CeraValue v;
            v.tag = VAL_BOOL;
            v.as.int_val = 0;
            push(vm, v);
            break;
        }
        
        case OP_PUSH_UNIT:
        {
            CeraValue v;
            v.tag = VAL_UNIT;
            v.as.int_val = 0;
            push(vm, v);
            break;
        }
        
        case OP_PUSH_CHAR:
        {
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

        case OP_LOAD_LOCAL:
        {
            uint8_t slot = READ_BYTE();
            CeraValue local = frame->slots[slot];
            retain(local);
            push(vm, local);
            break;
        }
        
        case OP_STORE_LOCAL:
        {
            uint8_t slot = READ_BYTE();
            release(frame->slots[slot]);
            CeraValue value = PEEK(0);
            retain(value);
            frame->slots[slot] = value;
            break;
        }
        
        case OP_LOAD_FUNCTION:
        {
            uint8_t index = READ_BYTE();
            CeraValue v;
            v.tag = VAL_INT;
            v.as.int_val = index; 
            push(vm, v);
            break;
        }
        
        case OP_LOAD_FUNCTION_LONG:
        {
            uint16_t index = READ_SHORT();
            CeraValue v;
            v.tag = VAL_INT;
            v.as.int_val = index;
            push(vm, v);
            break;
        }
        
        case OP_LOAD_UPVALUE:
        {
            uint8_t slot = READ_BYTE();
            CeraValue upvalue = *frame->closure->upvalues[slot]->location;
            retain(upvalue);
            push(vm, upvalue);
            break;
        }
        
        case OP_ADD: BINARY_NUM_OP(+); break;
        case OP_SUB: BINARY_NUM_OP(-); break;
        case OP_MUL: BINARY_NUM_OP(*); break;
        case OP_DIV: BINARY_NUM_OP(/); break;
        case OP_MOD: BINARY_INT_OP(%); break;
        
        case OP_ADD_1: 
        {
            CeraValue a = pop(vm);
            if (a.tag == VAL_FLOAT) a.as.float_val += 1.0;
            else a.as.int_val += 1;
            push(vm, a);
            break;
        }
        
        case OP_SUB_1: 
        {
            CeraValue a = pop(vm);
            if (a.tag == VAL_FLOAT) a.as.float_val -= 1.0;
            else a.as.int_val -= 1;
            push(vm, a);
            break;
        }
        
        case OP_NEGATE:
        {
            CeraValue a = pop(vm);
            if (a.tag == VAL_INT)
                a.as.int_val = -a.as.int_val;
            else if (a.tag == VAL_FLOAT)
                a.as.float_val = -a.as.float_val;
            else
                RUNTIME_ERROR(vm, "Cannot negate non-number. Received tag: %d", a.tag);
            push(vm, a);
            break;
        }
        
        case OP_BIT_AND: BINARY_INT_OP(&); break;
        case OP_BIT_OR: BINARY_INT_OP(|); break;
        case OP_BIT_XOR: BINARY_INT_OP(^); break;
        case OP_SHL: BINARY_INT_OP(<<); break;
        case OP_SHR: BINARY_INT_OP(>>); break;

        case OP_BIT_NOT:
        {
            CeraValue a = pop(vm);
            a.as.int_val = ~a.as.int_val;
            push(vm, a);
            break;
        }

        case OP_EQ: BINARY_BOOL_OP(==); break;
        case OP_NEQ: BINARY_BOOL_OP(!=); break;
        case OP_LT: BINARY_BOOL_OP(<); break;
        case OP_GT: BINARY_BOOL_OP(>); break;
        case OP_LTE: BINARY_BOOL_OP(<=); break;
        case OP_GTE: BINARY_BOOL_OP(>=); break;

        case OP_NOT:
        {
            CeraValue a = pop(vm);
            a.as.int_val = (a.as.int_val == 0) ? 1 : 0;
            push(vm, a);
            break;
        }

        case OP_JUMP:
        {
            uint16_t offset = READ_SHORT();
            frame->ip += offset;
            break;
        }

        case OP_JUMP_IF_FALSE:
        {
            uint16_t offset = READ_SHORT();
            CeraValue condition = pop(vm);
            if (condition.as.int_val == 0)
                frame->ip += offset;
            break;
        }
        
        case OP_JUMP_IF_TRUE:
        {
            uint16_t offset = READ_SHORT();
            CeraValue condition = pop(vm);
            if (condition.as.int_val == 1)
                frame->ip += offset;
            break;
        }
        
        case OP_JUMP_IF_FALSE_EQ: FUSED_RELATIONAL_JUMP(==); break;
        case OP_JUMP_IF_FALSE_NEQ: FUSED_RELATIONAL_JUMP(!=); break;
        case OP_JUMP_IF_FALSE_LT: FUSED_RELATIONAL_JUMP(<); break;
        case OP_JUMP_IF_FALSE_LTE: FUSED_RELATIONAL_JUMP(<=); break;
        case OP_JUMP_IF_FALSE_GT: FUSED_RELATIONAL_JUMP(>); break;
        case OP_JUMP_IF_FALSE_GTE: FUSED_RELATIONAL_JUMP(>=); break;
        
        case OP_JUMP_IF_FALSE_PEEK:
        {
            uint16_t offset = READ_SHORT();
            if (PEEK(0).as.int_val == 0) frame->ip += offset;
            break;
        }
        
        case OP_JUMP_IF_TRUE_PEEK:
        {
            uint16_t offset = READ_SHORT();
            if (PEEK(0).as.int_val == 1) frame->ip += offset;
            break;
        }
        
        case OP_JUMP_IF_LOCAL_NOT_EQ_CONST:
        {
            uint8_t local_slot = READ_BYTE();
            uint8_t const_idx = READ_BYTE();
            uint16_t offset = READ_SHORT();
            
            CeraValue a = frame->slots[local_slot];
            CeraValue b = active_function->constants[const_idx];
            bool matches = false;
            
            if (a.tag == VAL_STRING || a.tag == VAL_LIST) {
                char *strA = flatten_char_list(a);
                char *strB = flatten_char_list(b);
                matches = (strcmp(strA, strB) == 0);
                free(strA); free(strB);
            } else if (a.tag == VAL_FLOAT) {
                matches = (a.as.float_val == b.as.float_val);
            } else {
                matches = (a.as.int_val == b.as.int_val);
            }
            
            if (!matches) frame->ip += offset;
            break;
        }

        case OP_RETURN:
        {
            CeraValue result = pop(vm);
            close_upvalues(vm, frame->slots);

            vm->frame_count--;

            while (vm->stack_top > frame->slots)
            {
                release(pop(vm));
            }

            push(vm, result);

            if (vm->frame_count == 0)
            {
                // The main program OR the worker thread terminated cleanly
                return 0;
            }

            frame = &vm->frames[vm->frame_count - 1];
            active_function = get_function(vm->active_module, frame->function_index);
            break;
        }

        case OP_CALL:
        {
            uint8_t arg_count = READ_BYTE();
            CeraValue callee = PEEK(arg_count);

            if (callee.tag != VAL_CLOSURE)
                RUNTIME_ERROR(vm, "Attempted to call a non-function value. Received tag: %d", callee.tag);
            if (call_function(vm, (ObjClosure *)callee.as.obj, arg_count) != 0)
            {
                return 1;
            }

            frame = &vm->frames[vm->frame_count - 1];
            active_function = get_function(vm->active_module, frame->closure->function_index);
            break;
        }

        case OP_CALL_INTRINSIC:
            uint8_t intrinsic_id = READ_BYTE();
            frame->ip++;
            if (execute_intrinsic(vm, intrinsic_id) != 0)
            {
                return 1;
            }
            break;

        case OP_TAIL_CALL:
        {
            uint8_t arg_count = READ_BYTE();
            CeraValue callee = PEEK(arg_count);

            if (callee.tag != VAL_CLOSURE)
                RUNTIME_ERROR(vm, "Attempted to tail-call a non-function value. Received tag: %d", callee.tag);

            ObjClosure *closure = (ObjClosure *)callee.as.obj;
            CompiledFunction *func = get_function(vm->active_module, closure->function_index);

            if (func == NULL)
            {
                log_error("Fatal: Unknown function index %d in tail call", closure->function_index);
                return 1;
            }
            if (func->code_size == 0)
            {
                log_error("Fatal: Function %d has 0 bytes of bytecode", func->index);
                return 1;
            }

            if (arg_count != func->arity)
            {
                log_error("Expected %d arguments but got %d", func->arity, arg_count);
                return 1;
            }

            close_upvalues(vm, frame->slots);

            CeraValue *temp_args = malloc(sizeof(CeraValue) * (arg_count + 1));
            for (int i = 0; i <= arg_count; i++)
            {
                temp_args[i] = PEEK(arg_count - i);
                retain(temp_args[i]);
            }

            while (vm->stack_top > frame->slots)
            {
                release(pop(vm));
            }

            for (int i = 0; i <= arg_count; i++)
            {
                push(vm, temp_args[i]);
            }
            free(temp_args);

            frame->closure = closure;
            frame->function_index = closure->function_index;
            frame->ip = func->code;
            active_function = func;
            break;
        }
        
        case OP_MAKE_CLOSURE:
        {
            uint8_t upvalue_count = READ_BYTE();
            CeraValue func_val = pop(vm);
            if (func_val.tag != VAL_INT)
                RUNTIME_ERROR(vm, "OP_MAKE_CLOSURE expected int on stack. Received tag: %d", func_val.tag);

            uint32_t func_index = (uint32_t)func_val.as.int_val;
            CompiledFunction *function = get_function(vm->active_module, func_index);

            if (function == NULL)
            {
                log_error("Fatal: Attempted to make closure for unknown function index %d", func_index);
                return 1;
            }

            ObjClosure *closure = newClosure(func_index, function->arity, upvalue_count);

            for (int i = 0; i < upvalue_count; i++)
            {
                uint8_t is_local = READ_BYTE();
                uint8_t index = READ_BYTE();

                if (is_local)
                {
                    closure->upvalues[i] = capture_upvalue(vm, frame->slots + index);
                }
                else
                {
                    closure->upvalues[i] = frame->closure->upvalues[index];                    
                    closure->upvalues[i]->header.ref_count++;
                }
            }

            CeraValue closure_val;
            closure_val.tag = VAL_CLOSURE;
            closure_val.as.obj = (Obj *)closure;
            push(vm, closure_val);
            break;
        }
        
        case OP_CALL_GLOBAL:
        {
            uint8_t func_index = READ_BYTE();
            uint8_t arg_count = READ_BYTE();

            if (call_static_function(vm, func_index, arg_count) != 0) return 1;

            frame = &vm->frames[vm->frame_count - 1];
            active_function = get_function(vm->active_module, frame->function_index);
            break;
        }

        case OP_CALL_GLOBAL_LONG:
        {
            uint16_t func_index = READ_SHORT();
            uint8_t arg_count = READ_BYTE();

            if (call_static_function(vm, func_index, arg_count) != 0) return 1;

            frame = &vm->frames[vm->frame_count - 1];
            active_function = get_function(vm->active_module, frame->function_index);
            break;
        }
        
        case OP_TAIL_CALL_GLOBAL:
        {
            uint8_t func_index = READ_BYTE();
            uint8_t arg_count = READ_BYTE();

            CompiledFunction *func = get_function(vm->active_module, func_index);

            if (func == NULL)
            {
                log_error("Fatal: Unknown function index %d in static tail call", func_index);
                return 1;
            }
            if (func->code_size == 0)
            {
                log_error("Fatal: Function %d has 0 bytes of bytecode", func->index);
                return 1;
            }

            if (arg_count != func->arity)
            {
                log_error("Expected %d arguments but got %d", func->arity, arg_count);
                return 1;
            }

            close_upvalues(vm, frame->slots);

            CeraValue *temp_args = NULL;
            if (arg_count > 0) 
            {
                temp_args = malloc(sizeof(CeraValue) * arg_count);
                for (int i = 0; i < arg_count; i++)
                {
                    temp_args[i] = PEEK(arg_count - 1 - i);
                    retain(temp_args[i]);
                }
            }

            while (vm->stack_top > frame->slots)
            {
                release(pop(vm));
            }

            CeraValue padding;
            padding.tag = VAL_UNIT;
            padding.as.int_val = 0;
            push(vm, padding);

            if (arg_count > 0) 
            {
                for (int i = 0; i < arg_count; i++)
                {
                    push(vm, temp_args[i]);
                }
                free(temp_args);
            }

            frame->closure = NULL; 
            frame->function_index = func_index;
            frame->ip = func->code;
            active_function = func;
            break;
        }

        case OP_TAIL_CALL_GLOBAL_LONG:
        {
            uint16_t func_index = READ_SHORT();
            uint8_t arg_count = READ_BYTE();

            CompiledFunction *func = get_function(vm->active_module, func_index);

            if (func == NULL)
            {
                log_error("Fatal: Unknown function index %d in static tail call", func_index);
                return 1;
            }
            if (func->code_size == 0)
            {
                log_error("Fatal: Function %d has 0 bytes of bytecode", func->index);
                return 1;
            }

            if (arg_count != func->arity)
            {
                log_error("Expected %d arguments but got %d", func->arity, arg_count);
                return 1;
            }

            close_upvalues(vm, frame->slots);

            CeraValue *temp_args = NULL;
            if (arg_count > 0) 
            {
                temp_args = malloc(sizeof(CeraValue) * arg_count);
                for (int i = 0; i < arg_count; i++)
                {
                    temp_args[i] = PEEK(arg_count - 1 - i);
                    retain(temp_args[i]);
                }
            }

            while (vm->stack_top > frame->slots)
            {
                release(pop(vm));
            }

            CeraValue padding;
            padding.tag = VAL_UNIT;
            padding.as.int_val = 0;
            push(vm, padding);

            if (arg_count > 0) 
            {
                for (int i = 0; i < arg_count; i++)
                {
                    push(vm, temp_args[i]);
                }
                free(temp_args);
            }

            frame->closure = NULL; 
            frame->function_index = func_index;
            frame->ip = func->code;
            active_function = func;
            break;
        }
        
        case OP_ALLOC_CON:
        {
            uint8_t tag_id = READ_BYTE();
            CeraValue payload = pop(vm);

            ObjADT *adt = (ObjADT *)allocateObject(sizeof(ObjADT), VAL_ADT);
            adt->adt_tag = tag_id;
            adt->payload = payload;

            CeraValue res;
            res.tag = VAL_ADT;
            res.as.obj = (Obj *)adt;
            push(vm, res);
            break;
        }
        
        case OP_ALLOC_TUPLE:
        {
            uint8_t size = READ_BYTE();
            ObjTuple *tuple = (ObjTuple *)allocateObject(sizeof(ObjTuple), VAL_TUPLE);
            tuple->length = size;
            tuple->elements = malloc(sizeof(CeraValue) * size);

            populate_seq_from_stack(vm, tuple->elements, size);

            CeraValue res;
            res.tag = VAL_TUPLE;
            res.as.obj = (Obj *)tuple;
            push(vm, res);
            break;
        }

        case OP_ALLOC_ARRAY:
        {
            uint8_t size = READ_BYTE();
            ObjArray *arr = newArray(size);

            populate_seq_from_stack(vm, arr->elements, size);

            CeraValue res;
            res.tag = VAL_ARRAY;
            res.as.obj = (Obj *)arr;
            push(vm, res);
            break;
        }

        case OP_ALLOC_ARRAY_LONG:
        {
            uint16_t size = READ_SHORT();
            ObjArray *arr = newArray(size);

            populate_seq_from_stack(vm, arr->elements, size);

            CeraValue res;
            res.tag = VAL_ARRAY;
            res.as.obj = (Obj *)arr;
            push(vm, res);
            break;
        }

        case OP_LIST_EMPTY:
        {
            CeraValue res;
            res.tag = VAL_LIST;
            res.as.obj = NULL;
            push(vm, res);
            break;
        }

        case OP_LIST_CONS:
        {
            CeraValue tail = pop(vm);
            CeraValue head = pop(vm);

            // Intercept char :: string (or char :: [])
            if (head.tag == VAL_CHAR && (tail.tag == VAL_STRING || (tail.tag == VAL_LIST && tail.as.obj == NULL)))
            {
                uint32_t tail_len = (tail.tag == VAL_STRING) ? ((ObjString *)tail.as.obj)->length : 0;
                uint32_t new_len = tail_len + 1;

                ObjString *new_str = (ObjString *)allocateObject(sizeof(ObjString), VAL_STRING);
                new_str->length = new_len;
                new_str->chars = malloc(new_len + 1);

                new_str->chars[0] = (char)head.as.int_val; // Insert the prepended char
                if (tail_len > 0)
                {
                    memcpy(new_str->chars + 1, ((ObjString *)tail.as.obj)->chars, tail_len);
                }
                new_str->chars[new_len] = '\0';

                release(tail);

                CeraValue res;
                res.tag = VAL_STRING;
                res.as.obj = (Obj *)new_str;
                push(vm, res);
            }
            else
            {
                ObjList *list = (ObjList *)allocateObject(sizeof(ObjList), VAL_LIST);

                list->head = head;
                list->tail = tail;

                CeraValue res;
                res.tag = VAL_LIST;
                res.as.obj = (Obj *)list;
                push(vm, res);
            }
            break;
        }

        case OP_JUMP_IF_NOT_TAG:
        {
            uint8_t expected_tag = READ_BYTE();
            uint16_t offset = READ_SHORT();
            CeraValue top = pop(vm); 

            bool matches = false;

            if (top.tag == VAL_ADT && ((ObjADT *)top.as.obj)->adt_tag == expected_tag) {
                matches = true;
            }
            else if (top.tag == VAL_LIST && top.as.obj != NULL && expected_tag == 0x01) {
                matches = true;
            }
            else if (top.tag == VAL_LIST && top.as.obj == NULL && expected_tag == 0x00) {
                matches = true;
            }
            else if (top.tag == VAL_STRING) {
                ObjString *str = (ObjString *)top.as.obj;
                if (expected_tag == 0x01 && str->length > 0) matches = true;
                else if (expected_tag == 0x00 && str->length == 0) matches = true;
            }
            
            release(top); 

            if (!matches) {
                frame->ip += offset;
            }
            break;
        }

        case OP_UNPACK_CON:
        {
            CeraValue val = pop(vm);
            ObjADT *adt = (ObjADT *)val.as.obj;
            CeraValue payload = adt->payload;

            retain(payload);
            release(val);

            push(vm, payload);
            break;
        }

        case OP_UNPACK_TUPLE:
        {
            CeraValue val = pop(vm);
            ObjTuple *tuple = (ObjTuple *)val.as.obj;

            // Push FORWARD: elements[0] takes the lowest index slot
            for (int i = 0; i < tuple->length; i++)
            {
                CeraValue elem = tuple->elements[i];
                retain(elem);
                push(vm, elem);
            }
            release(val);
            break;
        }

        case OP_UNPACK_LIST:
        {
            CeraValue val = pop(vm);

            if (val.tag == VAL_STRING)
            {
                ObjString *str = (ObjString *)val.as.obj;

                CeraValue head;
                head.tag = VAL_CHAR;
                head.as.int_val = (uint32_t)str->chars[0]; // Slice the first character

                uint32_t new_len = str->length - 1;
                ObjString *tail_str = (ObjString *)allocateObject(sizeof(ObjString), VAL_STRING);
                tail_str->length = new_len;
                tail_str->chars = malloc(new_len + 1);

                // Copy the remaining substring
                memcpy(tail_str->chars, str->chars + 1, new_len);
                tail_str->chars[new_len] = '\0';

                CeraValue tail;
                tail.tag = VAL_STRING;
                tail.as.obj = (Obj *)tail_str;

                release(val);

                push(vm, head);
                push(vm, tail);
            }
            else
            {
                ObjList *list = (ObjList *)val.as.obj;

                CeraValue head = list->head;
                CeraValue tail = list->tail;

                retain(head);
                retain(tail);
                release(val);

                push(vm, head);
                push(vm, tail);
            }
            break;
        }

        case OP_UNPACK_ARRAY:
        {
            CeraValue val = pop(vm);
            ObjArray *arr = (ObjArray *)val.as.obj;

            for (int i = 0; i < arr->length; i++)
            {
                CeraValue elem = arr->elements[i];
                retain(elem);
                push(vm, elem);
            }
            release(val);
            break;
        }

        case OP_JUMP_IF_NOT_LIST_EMPTY:
        {
            uint16_t offset = READ_SHORT();
            CeraValue top = pop(vm);
            
            bool matches = false;

            if (top.tag == VAL_LIST && top.as.obj == NULL)
            {
                matches = true;
            }

            else if (top.tag == VAL_STRING && ((ObjString *)top.as.obj)->length == 0)
            {
                matches = true;
            }

            release(top);

            if (!matches)
            {
                frame->ip += offset;
            }
            break;
        }

        case OP_JUMP_IF_NOT_ARRAY_LENGTH:
        {
            uint16_t expected_len = READ_SHORT();
            uint16_t offset = READ_SHORT();
            CeraValue top = pop(vm);
            
            bool matches = false;

            if (top.tag == VAL_ARRAY && ((ObjArray *)top.as.obj)->length == expected_len)
            {
                matches = true;
            }

            release(top);

            if (!matches)
            {
                frame->ip += offset;
            }
            break;
        }

        case OP_MATCH_FAIL:
            RUNTIME_ERROR(vm, "Pattern match exhaustiveness failure. Execution fell through all switch branches.");

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
#undef BINARY_NUM_OP