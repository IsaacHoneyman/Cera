#include <stdlib.h>
#include "memory.h"
#include "logger.h"

void retain(CeraValue value) { // increments ref count
    if (IS_OBJ(value)) {
        AS_OBJ(value) -> ref_count++; 
    }
}

void release(CeraValue value) { // decrements ref count
    if (IS_OBJ(value)) {
        Obj* obj = AS_OBJ(value);
        obj -> ref_count--;

        if (obj -> ref_count == 0) {
            freeObject(obj);
        }
    }
}

void freeObject(Obj* obj) {
    log_detail("Freeing heap object (Type: %d)", obj->type); 
    switch (obj -> type) {
        case VAL_STRING: {
            ObjString* string = (ObjString*)obj;
            free(string -> chars); // free char arr
            free(string); // free wrapper
            break;
        }
        case VAL_TUPLE: case VAL_ARRAY: {
            ObjTuple* seq = (ObjTuple*)obj;
            for (int i = 0; i < seq -> length; i++) {
                release(seq -> elements[i]);
            }
            free(seq -> elements);
            free (seq);
            break;
        }
        case VAL_ADT: {
            ObjADT* adt = (ObjADT*) obj;
            release(adt -> payload);
            free(adt);
            break;
        }
        case VAL_LIST: {
            ObjList* lst = (ObjList*)obj;
            release(lst -> head);
            release(lst -> tail);
            free(lst);
            break;
        }
        case VAL_CLOSURE: { 
            ObjClosure* closure = (ObjClosure*)obj;
            for (int i = 0; i < closure -> upvalue_count; i++) {
                release(closure->upvalues[i]->closed_value);
            }
            free(closure->upvalues);
            free(closure);
            break;
        }
    }
}

static Obj* allocateObject(size_t size, uint8_t type) {
    log_detail("Allocating heap object (Type: %d, Size: %zu bytes)", type, size); 
    Obj* obj = (Obj*)malloc(size);
    obj -> type = type;
    obj -> ref_count = 1;

    return obj;
}

ObjClosure* newClosure(int function_index, uint8_t arity, uint8_t upvalue_count) {
    ObjClosure* closure = (ObjClosure*)allocateObject(sizeof(ObjClosure), VAL_CLOSURE);

    closure -> function_index = function_index;
    closure -> arity = arity;
    closure -> upvalue_count = upvalue_count;

    if (upvalue_count > 0) {
        closure->upvalues = malloc(sizeof(ObjUpvalue*) * upvalue_count);
        for (int i = 0; i < upvalue_count; i++) {
            closure->upvalues[i] = NULL;
        }
    } else {
        closure->upvalues = NULL;
    }
    
    return closure;
}
