#ifndef MEMORY_H
#define MEMORY_H

#include "value.h"

#define IS_OBJ(value) ((value).tag >= VAL_STRING)
#define AS_OBJ(value) ((value).as.obj)

void retain(CeraValue value);
void release(CeraValue value);
void freeObject(Obj* object);
ObjClosure* newClosure(int function_index, uint8_t arity, uint8_t upvalue_count);

ObjString* newString(const char* text);
ObjArray* newArray(int length);

#endif