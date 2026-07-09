// memory.h (remember your header guards!)
#ifndef MEMORY_H
#define MEMORY_H

#include "value.h"

// Macros for quick type checking
#define IS_OBJ(value) ((value).tag >= VAL_STRING)
#define AS_OBJ(value) ((value).as.obj)

void retain(CeraValue value);
void release(CeraValue value);
void freeObject(Obj* object);

#endif