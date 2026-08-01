#include "multithreading.h"
#include "memory.h"
#include "logger.h"
#include "vm.h"
#include <string.h>
#include <stdlib.h>

atomic_int global_active_threads = 0;
int max_system_threads = 0;

CeraValue migrate_value(CeraValue val)
{
    if (!IS_OBJ(val) || val.as.obj == NULL)
        return val;

    Obj *obj = val.as.obj;

    if (!obj->is_arena)
    {
        retain(val);
        return val;
    }

    switch (obj->type)
    {
    case VAL_STRING:
    {
        ObjString *old_str = (ObjString *)obj;
        ObjString *new_str = (ObjString *)allocateObject(sizeof(ObjString), VAL_STRING);
        new_str->length = old_str->length;
        new_str->chars = malloc(new_str->length + 1);
        strcpy(new_str->chars, old_str->chars);

        CeraValue res;
        res.tag = VAL_STRING;
        res.as.obj = (Obj *)new_str;
        return res;
    }
    case VAL_TUPLE:
    case VAL_ARRAY:
    {
        ObjTuple *old_seq = (ObjTuple *)obj;
        ObjTuple *new_seq = (ObjTuple *)allocateObject(sizeof(ObjTuple), obj->type);
        new_seq->length = old_seq->length;
        new_seq->elements = malloc(sizeof(CeraValue) * new_seq->length);

        for (int i = 0; i < new_seq->length; i++)
        {
            new_seq->elements[i] = migrate_value(old_seq->elements[i]);
        }

        CeraValue res;
        res.tag = obj->type;
        res.as.obj = (Obj *)new_seq;
        return res;
    }
    case VAL_ADT:
    {
        ObjADT *old_adt = (ObjADT *)obj;
        ObjADT *new_adt = (ObjADT *)allocateObject(sizeof(ObjADT), VAL_ADT);
        new_adt->adt_tag = old_adt->adt_tag;
        new_adt->payload = migrate_value(old_adt->payload);

        CeraValue res;
        res.tag = VAL_ADT;
        res.as.obj = (Obj *)new_adt;
        return res;
    }
    case VAL_LIST:
    {
        ObjList *old_lst = (ObjList *)obj;
        ObjList *new_lst = (ObjList *)allocateObject(sizeof(ObjList), VAL_LIST);
        new_lst->head = migrate_value(old_lst->head);
        new_lst->tail = migrate_value(old_lst->tail);

        CeraValue res;
        res.tag = VAL_LIST;
        res.as.obj = (Obj *)new_lst;
        return res;
    }
    default:
        return val;
    }
}

void *run_worker(void *arg)
{
    WorkerState *state = (WorkerState *)arg;

    current_arena = malloc(sizeof(ThreadArena));
    current_arena->capacity = 256 * 1024 * 1024;
    current_arena->base = malloc(current_arena->capacity);
    current_arena->current = current_arena->base;
    state->arena_ptr = current_arena;

    VM local_vm;
    local_vm.stack_top = local_vm.stack;
    local_vm.frame_count = 0;
    local_vm.active_module = state->active_module;
    local_vm.open_upvalues = NULL;

    ObjList *current_node = state->chunk_head;

    for (int i = 0; i < state->chunk_size; i++)
    {
        if (current_node == NULL)
            break;

        CeraValue closure_val;
        closure_val.tag = VAL_CLOSURE;
        closure_val.as.obj = (Obj *)state->mapped_func;

        push(&local_vm, closure_val);
        push(&local_vm, current_node->head);

        CallFrame *frame = &local_vm.frames[local_vm.frame_count++];
        frame->closure = state->mapped_func;
        frame->function_index = state->mapped_func->function_index;

        CompiledFunction *func = &state->active_module->functions[frame->function_index];
        frame->ip = func->code;
        frame->slots = local_vm.stack_top - 2;

        runVM(&local_vm);

        CeraValue mapped_res = pop(&local_vm);

        current_arena = NULL;

        CeraValue permanent_res = migrate_value(mapped_res);

        release(mapped_res);

        ObjList *res_node = (ObjList *)allocateObject(sizeof(ObjList), VAL_LIST);
        res_node->head = permanent_res;
        res_node->tail.tag = VAL_LIST;
        res_node->tail.as.obj = NULL;

        if (state->result_list.as.obj == NULL)
        {
            state->result_list.tag = VAL_LIST;
            state->result_list.as.obj = (Obj *)res_node;
        }
        else
        {
            state->result_tail->tag = VAL_LIST;
            state->result_tail->as.obj = (Obj *)res_node;
        }
        state->result_tail = &res_node->tail;

        current_arena = state->arena_ptr;
        current_arena->current = current_arena->base;

        current_node = (ObjList *)current_node->tail.as.obj;
    }

    return NULL;
}

void *run_pool_worker(void *arg)
{
    PoolWorkerState *state = (PoolWorkerState *)arg;
    PoolSharedState *shared = state->shared;

    current_arena = malloc(sizeof(ThreadArena));
    current_arena->capacity = 256 * 1024 * 1024;
    current_arena->base = malloc(current_arena->capacity);
    current_arena->current = current_arena->base;
    state->arena_ptr = current_arena;

    VM local_vm;
    local_vm.stack_top = local_vm.stack;
    local_vm.frame_count = 0;
    local_vm.active_module = shared->active_module;
    local_vm.open_upvalues = NULL;

    while (1)
    {
        int task_idx = atomic_fetch_add(&shared->task_counter, 1);
        
        if (task_idx >= shared->total_tasks) break;

        CeraValue closure_val;
        closure_val.tag = VAL_CLOSURE;
        closure_val.as.obj = (Obj *)shared->mapped_func;

        push(&local_vm, closure_val);
        push(&local_vm, shared->input_array[task_idx]); 

        CallFrame *frame = &local_vm.frames[local_vm.frame_count++];
        frame->closure = shared->mapped_func;
        frame->function_index = shared->mapped_func->function_index;

        CompiledFunction *func = &shared->active_module->functions[frame->function_index];
        frame->ip = func->code;
        frame->slots = local_vm.stack_top - 2;

        runVM(&local_vm);

        CeraValue mapped_res = pop(&local_vm);

        current_arena = NULL;
        CeraValue permanent_res = migrate_value(mapped_res);
        release(mapped_res);

        shared->result_array[task_idx] = permanent_res; 

        current_arena = state->arena_ptr;
        current_arena->current = current_arena->base;
    }

    return NULL;
}

void *run_invoke_worker(void *arg)
{
    InvokeWorkerState *state = (InvokeWorkerState *)arg;
    current_arena = malloc(sizeof(ThreadArena));
    current_arena->capacity = 256 * 1024 * 1024;
    current_arena->base = malloc(current_arena->capacity);
    current_arena->current = current_arena->base;
    state->arena_ptr = current_arena;

    VM local_vm;
    local_vm.stack_top = local_vm.stack;
    local_vm.frame_count = 0;
    local_vm.active_module = state->active_module;
    local_vm.open_upvalues = NULL;

    CeraValue closure_val;
    closure_val.tag = VAL_CLOSURE;
    closure_val.as.obj = (Obj *)state->func;

    CeraValue unit_val;
    unit_val.tag = VAL_UNIT;
    unit_val.as.int_val = 0;

    push(&local_vm, closure_val);
    push(&local_vm, unit_val);

    CallFrame *frame = &local_vm.frames[local_vm.frame_count++];
    frame->closure = state->func;
    frame->function_index = state->func->function_index;
    CompiledFunction *func = &state->active_module->functions[frame->function_index];
    
    frame->ip = func->code;
    frame->slots = local_vm.stack_top - 2;

    runVM(&local_vm);

    CeraValue res = pop(&local_vm);
    current_arena = NULL;
    state->result = migrate_value(res);
    release(res);

    return NULL;
}

void *run_fold_worker(void *arg)
{
    FoldWorkerState *state = (FoldWorkerState *)arg;
    current_arena = malloc(sizeof(ThreadArena));
    current_arena->capacity = 256 * 1024 * 1024;
    current_arena->base = malloc(current_arena->capacity);
    current_arena->current = current_arena->base;
    state->arena_ptr = current_arena;

    VM local_vm;
    local_vm.stack_top = local_vm.stack;
    local_vm.frame_count = 0;
    local_vm.active_module = state->active_module;
    local_vm.open_upvalues = NULL;

    CeraValue acc = state->init_val;
    retain(acc);

    ObjList *current_node = state->chunk_head;

    for (int i = 0; i < state->chunk_size; i++)
    {
        if (current_node == NULL) break;

        CeraValue closure_val;
        closure_val.tag = VAL_CLOSURE;
        closure_val.as.obj = (Obj *)state->func;

        push(&local_vm, closure_val);

        ObjTuple *pair = (ObjTuple *)allocateObject(sizeof(ObjTuple), VAL_TUPLE);
        pair->length = 2;
        pair->elements = malloc(sizeof(CeraValue) * 2);
        pair->elements[0] = acc;
        pair->elements[1] = current_node->head;

        retain(acc);
        retain(current_node->head);

        CeraValue tuple_val;
        tuple_val.tag = VAL_TUPLE;
        tuple_val.as.obj = (Obj *)pair;

        push(&local_vm, tuple_val); 

        CallFrame *frame = &local_vm.frames[local_vm.frame_count++];
        frame->closure = state->func;
        frame->function_index = state->func->function_index;

        CompiledFunction *func = &state->active_module->functions[frame->function_index];
        frame->ip = func->code;        
        frame->slots = local_vm.stack_top - 2; 

        runVM(&local_vm);

        CeraValue raw_res = pop(&local_vm);

        current_arena = NULL;
        CeraValue permanent_res = migrate_value(raw_res);

        release(acc);          
        acc = permanent_res;   
        retain(acc);           
        release(raw_res);      

        current_arena = state->arena_ptr;
        current_arena->current = current_arena->base;

        current_node = (ObjList *)current_node->tail.as.obj;
    }

    current_arena = NULL;
    state->result = migrate_value(acc);
    release(acc);

    return NULL;
}

void pin_value(CeraValue val)
{
    if (!IS_OBJ(val) || val.as.obj == NULL)
        return;

    Obj *obj = val.as.obj;
    if (obj->is_pinned)
        return;

    obj->is_pinned = 1;

    switch (obj->type)
    {
    case VAL_TUPLE:
    case VAL_ARRAY:
    {
        ObjTuple *seq = (ObjTuple *)obj;
        for (int i = 0; i < seq->length; i++)
            pin_value(seq->elements[i]);
        break;
    }
    case VAL_ADT:
    {
        pin_value(((ObjADT *)obj)->payload);
        break;
    }
    case VAL_LIST:
    {
        pin_value(((ObjList *)obj)->head);
        pin_value(((ObjList *)obj)->tail);
        break;
    }
    case VAL_CLOSURE:
    {
        ObjClosure *closure = (ObjClosure *)obj;
        for (int i = 0; i < closure->upvalue_count; i++)
        {
            pin_value(*(closure->upvalues[i]->location));
        }
        break;
    }
    }
}

void unpin_value(CeraValue val)
{
    if (!IS_OBJ(val) || val.as.obj == NULL)
        return;

    Obj *obj = val.as.obj;
    if (!obj->is_pinned)
        return;

    obj->is_pinned = 0;

    switch (obj->type)
    {
    case VAL_TUPLE:
    case VAL_ARRAY:
    {
        ObjTuple *seq = (ObjTuple *)obj;
        for (int i = 0; i < seq->length; i++)
            unpin_value(seq->elements[i]);
        break;
    }
    case VAL_ADT:
    {
        unpin_value(((ObjADT *)obj)->payload);
        break;
    }
    case VAL_LIST:
    {
        unpin_value(((ObjList *)obj)->head);
        unpin_value(((ObjList *)obj)->tail);
        break;
    }
    case VAL_CLOSURE:
    {
        ObjClosure *closure = (ObjClosure *)obj;
        for (int i = 0; i < closure->upvalue_count; i++)
        {
            unpin_value(*(closure->upvalues[i]->location));
        }
        break;
    }
    }
}