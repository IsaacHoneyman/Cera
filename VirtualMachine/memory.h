#ifndef MEMORY_H
#define MEMORY_H

#include "value.h"

#define IS_OBJ(value) ((value).tag >= VAL_STRING)
#define AS_OBJ(value) ((value).as.obj)

void retain(CeraValue value);
void release(CeraValue value);
void freeObject(Obj* object);
Obj* allocateObject(size_t size, uint8_t type); 
ObjClosure* newClosure(int function_index, uint8_t arity, uint8_t upvalue_count);

ObjString* newString(const char* text);
ObjArray* newArray(int length);
ObjADT* newOption(bool is_some, CeraValue payload);
ObjADT* newResult(bool is_ok, CeraValue payload);

#endif