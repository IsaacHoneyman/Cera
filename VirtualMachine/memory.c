#include <stdlib.h>
#include <string.h>
#include "memory.h"
#include "logger.h"

void retain(CeraValue value) { // increments ref count
    if (IS_OBJ(value) && AS_OBJ(value) != NULL) {
        AS_OBJ(value) -> ref_count++; 
    }
}

void release(CeraValue value) { // decrements ref count
    if (IS_OBJ(value) && AS_OBJ(value) != NULL) {
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
                ObjUpvalue* upvalue = closure->upvalues[i];                
                upvalue->header.ref_count--;
                
                if (upvalue->header.ref_count == 0) {
                    release(upvalue->closed_value); 
                    free(upvalue);                  
                }
            }
            free(closure->upvalues);
            free(closure);
            break;
        }
    }
}

Obj* allocateObject(size_t size, uint8_t type) {
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

ObjString* newString(const char* text) {
    ObjString* string = (ObjString*)allocateObject(sizeof(ObjString), VAL_STRING);
    
    string->length = strlen(text);
    string->chars = malloc(string->length + 1); // +1 for the null terminator
    strcpy(string->chars, text);
    
    return string;
}

ObjArray* newArray(int length) {
    ObjArray* array = (ObjArray*)allocateObject(sizeof(ObjArray), VAL_ARRAY);
    
    array->length = length;
    
    if (length > 0) {
        array->elements = malloc(sizeof(CeraValue) * length);
    } else {
        array->elements = NULL;
    }
    
    return array;
}

ObjADT* newOption(bool is_some, CeraValue payload) {
    ObjADT* adt = (ObjADT*)allocateObject(sizeof(ObjADT), VAL_ADT);
    // 0x03 is the compiler's 'Some' tag, 0x02 is 'None'
    adt->adt_tag = is_some ? 0x03 : 0x02; 
    adt->payload = payload;
    return adt;
}

ObjADT* newResult(bool is_ok, CeraValue payload) {
    ObjADT* adt = (ObjADT*)allocateObject(sizeof(ObjADT), VAL_ADT);
    // 0x05 is the compiler's 'Ok' tag, 0x04 is 'Error'
    adt->adt_tag = is_ok ? 0x05 : 0x04; 
    adt->payload = payload;
    return adt;
}
