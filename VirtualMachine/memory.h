#ifndef MEMORY_H
#define MEMORY_H

#include "value.h"
#include <stddef.h>
#include <stdatomic.h>

#define IS_OBJ(value) ((value).tag >= VAL_STRING)
#define AS_OBJ(value) ((value).as.obj)

typedef struct {
    uint8_t* base;
    uint8_t* current;
    size_t capacity;
} ThreadArena;

typedef struct {
    CeraValue *input_array;
    CeraValue *result_array;
    atomic_int task_counter;
    int total_tasks;
    ObjClosure *mapped_func; 
    Module *active_module;   
} PoolSharedState;

typedef struct {
    PoolSharedState *shared;
    ThreadArena *arena_ptr;
} PoolWorkerState;

typedef struct {
    ObjClosure *func;
    Module *active_module;
    ThreadArena *arena_ptr;
    CeraValue result;
} InvokeWorkerState;

typedef struct {
    ObjClosure *func;
    Module *active_module;
    ThreadArena *arena_ptr;
    ObjList *chunk_head;
    int chunk_size;
    CeraValue init_val;
    CeraValue result;
} FoldWorkerState;

extern _Thread_local ThreadArena* current_arena;

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