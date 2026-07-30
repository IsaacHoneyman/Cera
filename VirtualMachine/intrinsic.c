#define _GNU_SOURCE
#define _POSIX_C_SOURCE 200809L

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <math.h>
#include <time.h>
#include "intrinsic.h"
#include "memory.h"
#include "logger.h"
#include <stdatomic.h>
#include <unistd.h>

static atomic_int global_active_threads = 0;
int max_system_threads = 0;

static CeraValue migrate_value(CeraValue val)
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

static void *run_worker(void *arg)
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

static void pin_value(CeraValue val)
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

static void unpin_value(CeraValue val)
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

int execute_intrinsic(VM *vm, uint8_t intrinsic_id)
{
    switch (intrinsic_id)
    {
    case INTR_ARR_LENGTH:
    {
        CeraValue arr_val = pop(vm);
        ObjArray *arr = (ObjArray *)arr_val.as.obj;

        CeraValue res;
        res.tag = VAL_INT;
        res.as.int_val = arr->length;

        release(arr_val);
        push(vm, res);
        return 0;
    }

    case INTR_GET:
    {
        CeraValue index_val = pop(vm);
        CeraValue arr_val = pop(vm);
        ObjArray *arr = (ObjArray *)arr_val.as.obj;
        int64_t idx = index_val.as.int_val;

        CeraValue payload;
        bool is_some = (idx >= 0 && idx < arr->length);

        if (is_some)
        {
            payload = arr->elements[idx];
            retain(payload);
        }
        else
        {
            payload.tag = VAL_UNIT;
            payload.as.int_val = 0;
        }

        ObjADT *adt = newOption(is_some, payload);
        release(arr_val);

        CeraValue res;
        res.tag = VAL_ADT;
        res.as.obj = (Obj *)adt;
        push(vm, res);
        return 0;
    }

    case INTR_ARR_CONCAT:
    {
        CeraValue right_val = pop(vm);
        CeraValue left_val = pop(vm);

        ObjArray *left = (ObjArray *)left_val.as.obj;
        ObjArray *right = (ObjArray *)right_val.as.obj;

        ObjArray *new_arr = newArray(left->length + right->length);

        for (int i = 0; i < left->length; i++)
        {
            new_arr->elements[i] = left->elements[i];
            retain(new_arr->elements[i]);
        }
        for (int i = 0; i < right->length; i++)
        {
            new_arr->elements[left->length + i] = right->elements[i];
            retain(new_arr->elements[left->length + i]);
        }

        release(left_val);
        release(right_val);

        CeraValue res;
        res.tag = VAL_ARRAY;
        res.as.obj = (Obj *)new_arr;

        push(vm, res);
        return 0;
    }

    case INTR_OUT:
    {
        CeraValue arg = pop(vm);

        char *text = flatten_char_list(arg);
        printf("%s", text);

        free(text);
        release(arg);

        CeraValue res;
        res.tag = VAL_UNIT;
        res.as.int_val = 0;
        push(vm, res);
        return 0;
    }

    case INTR_IN:
    {
        char buffer[1024];
        if (fgets(buffer, sizeof(buffer), stdin) == NULL)
        {
            buffer[0] = '\0';
        }
        else
        {
            size_t len = strlen(buffer);
            if (len > 0 && buffer[len - 1] == '\n')
            {
                buffer[len - 1] = '\0';
            }
        }

        CeraValue result;
        result.tag = VAL_STRING;
        result.as.obj = (Obj *)newString(buffer);

        push(vm, result);
        return 0;
    }

    case INTR_ARR_TO_LIST:
    {
        CeraValue arr_val = pop(vm);
        ObjArray *arr = (ObjArray *)arr_val.as.obj;

        CeraValue current_tail;
        current_tail.tag = VAL_LIST;
        current_tail.as.obj = NULL;

        for (int i = arr->length - 1; i >= 0; i--)
        {
            // Using correct managed allocator abstraction
            ObjList *node = (ObjList *)allocateObject(sizeof(ObjList), VAL_LIST);

            CeraValue elem = arr->elements[i];
            retain(elem);

            node->head = elem;
            node->tail = current_tail;

            current_tail.tag = VAL_LIST;
            current_tail.as.obj = (Obj *)node;
        }

        release(arr_val);
        push(vm, current_tail);
        return 0;
    }

    case INTR_LIST_TO_ARR:
    {
        CeraValue lst_val = pop(vm);

        int len = 0;
        ObjList *curr = (ObjList *)lst_val.as.obj;
        while (curr != NULL)
        {
            len++;
            if (curr->tail.tag != VAL_LIST)
                break;
            curr = (ObjList *)curr->tail.as.obj;
        }

        ObjArray *new_arr = newArray(len);
        curr = (ObjList *)lst_val.as.obj;

        for (int i = 0; i < len; i++)
        {
            if (curr == NULL)
                break;

            new_arr->elements[i] = curr->head;
            retain(curr->head);

            if (curr->tail.tag != VAL_LIST)
                break;
            curr = (ObjList *)curr->tail.as.obj;
        }

        release(lst_val);

        CeraValue res;
        res.tag = VAL_ARRAY;
        res.as.obj = (Obj *)new_arr;

        push(vm, res);
        return 0;
    }

    case INTR_INT_TO_FLOAT:
    {
        CeraValue arg = pop(vm);
        CeraValue result;
        result.tag = VAL_FLOAT;
        result.as.float_val = (double)arg.as.int_val;
        push(vm, result);
        return 0;
    }

    case INTR_FLOAT_TO_INT:
    {
        CeraValue arg = pop(vm);
        CeraValue result;
        result.tag = VAL_INT;
        result.as.int_val = (int64_t)arg.as.float_val;
        push(vm, result);
        return 0;
    }

    case INTR_CHAR_TO_INT:
    case INTR_INT_TO_CHAR:
    {
        CeraValue arg = pop(vm);
        CeraValue result;
        // CHAR and INT structurally share the same underlying memory mapping
        result.tag = (intrinsic_id == INTR_CHAR_TO_INT) ? VAL_INT : VAL_CHAR;
        result.as.int_val = arg.as.int_val;
        push(vm, result);
        return 0;
    }

    case INTR_INT_TO_CHARS:
    {
        CeraValue arg = pop(vm);
        char buffer[64];
        snprintf(buffer, sizeof(buffer), "%ld", (long)arg.as.int_val);

        CeraValue result;
        result.tag = VAL_STRING;
        result.as.obj = (Obj *)newString(buffer);

        push(vm, result);
        return 0;
    }

    case INTR_BOOL_TO_CHARS:
    {
        CeraValue arg = pop(vm);
        const char *text = arg.as.int_val ? "true" : "false";

        CeraValue result;
        result.tag = VAL_STRING;
        result.as.obj = (Obj *)newString(text);

        push(vm, result);
        return 0;
    }

    case INTR_FLOAT_TO_CHARS:
    {
        CeraValue arg = pop(vm);

        char buffer[64];
        snprintf(buffer, sizeof(buffer), "%g", arg.as.float_val);

        CeraValue result;
        result.tag = VAL_STRING;
        result.as.obj = (Obj *)newString(buffer);

        push(vm, result);
        return 0;
    }

    case INTR_CHARS_TO_INT:
    {
        CeraValue arg = pop(vm);
        char *str = flatten_char_list(arg);

        char *endptr;
        int64_t parsed_val = strtoll(str, &endptr, 10);
        while (*endptr != '\0' && isspace((unsigned char)*endptr))
        {
            endptr++;
        }

        CeraValue payload;
        bool is_some = (str != endptr && *endptr == '\0');

        if (is_some)
        {
            payload.tag = VAL_INT;
            payload.as.int_val = parsed_val;
        }
        else
        {
            payload.tag = VAL_UNIT;
            payload.as.int_val = 0;
        }

        ObjADT *adt = newOption(is_some, payload);

        free(str);
        release(arg);

        CeraValue result;
        result.tag = VAL_ADT;
        result.as.obj = (Obj *)adt;
        push(vm, result);
        return 0;
    }

    case INTR_CHARS_TO_FLOAT:
    {
        CeraValue arg = pop(vm);
        char *str = flatten_char_list(arg);

        char *endptr;
        double parsed_val = strtod(str, &endptr);
        while (*endptr != '\0' && isspace((unsigned char)*endptr))
        {
            endptr++;
        }

        CeraValue payload;
        bool is_some = (str != endptr && *endptr == '\0');

        if (is_some)
        {
            payload.tag = VAL_FLOAT;
            payload.as.float_val = parsed_val;
        }
        else
        {
            payload.tag = VAL_UNIT;
            payload.as.int_val = 0;
        }

        ObjADT *adt = newOption(is_some, payload);

        free(str);
        release(arg);

        CeraValue result;
        result.tag = VAL_ADT;
        result.as.obj = (Obj *)adt;
        push(vm, result);
        return 0;
    }

    case INTR_CONCAT:
    {
        CeraValue right = pop(vm);
        CeraValue left = pop(vm);

        bool left_is_str = (left.tag == VAL_STRING);
        bool right_is_str = (right.tag == VAL_STRING);

        if (left_is_str || right_is_str)
        {
            char *s1 = flatten_char_list(left);
            char *s2 = flatten_char_list(right);

            uint32_t len = strlen(s1) + strlen(s2);
            char *temp_buf = malloc(len + 1);
            strcpy(temp_buf, s1);
            strcat(temp_buf, s2);

            ObjString *new_str = newString(temp_buf);

            free(temp_buf);
            free(s1);
            free(s2);
            release(left);
            release(right);

            CeraValue res;
            res.tag = VAL_STRING;
            res.as.obj = (Obj *)new_str;
            push(vm, res);
            return 0;
        }

        // Standard Generic List Concatenation Path
        if (left.tag == VAL_LIST && right.tag == VAL_LIST)
        {
            if (left.as.obj == NULL)
            {
                release(left);
                push(vm, right);
                return 0;
            }

            int left_len = 0;
            ObjList *curr = (ObjList *)left.as.obj;
            while (curr != NULL)
            {
                left_len++;
                if (curr->tail.tag != VAL_LIST)
                    break;
                curr = (ObjList *)curr->tail.as.obj;
            }

            CeraValue *temp_buffer = malloc(sizeof(CeraValue) * left_len);
            curr = (ObjList *)left.as.obj;
            for (int i = 0; i < left_len; i++)
            {
                if (curr == NULL)
                    break;
                temp_buffer[i] = curr->head;
                if (curr->tail.tag != VAL_LIST)
                    break;
                curr = (ObjList *)curr->tail.as.obj;
            }

            CeraValue current_tail = right;
            retain(current_tail);

            for (int i = left_len - 1; i >= 0; i--)
            {
                ObjList *node = (ObjList *)allocateObject(sizeof(ObjList), VAL_LIST);

                CeraValue head_val = temp_buffer[i];
                retain(head_val);

                node->head = head_val;
                node->tail = current_tail;

                current_tail.tag = VAL_LIST;
                current_tail.as.obj = (Obj *)node;
            }

            free(temp_buffer);
            release(left);
            release(right);

            push(vm, current_tail);
            return 0;
        }

        return 1;
    }

    case INTR_RAND:
    {
        CeraValue result;
        result.tag = VAL_FLOAT;
        result.as.float_val = (double)rand() / (double)RAND_MAX;
        push(vm, result);
        return 0;
    }

    case INTR_RAND_INT:
    {
        CeraValue result;
        result.tag = VAL_INT;

        int64_t high = (int64_t)rand();
        int64_t low = (int64_t)rand();

        result.as.int_val = (high << 32) | low;
        push(vm, result);
        return 0;
    }

    case INTR_SQRT:
    {
        CeraValue arg = pop(vm);

        CeraValue result;
        result.tag = VAL_FLOAT;
        result.as.float_val = sqrt(arg.as.float_val);
        push(vm, result);
        return 0;
    }

    case INTR_READ:
    {
        CeraValue path_val = pop(vm);
        char *path = flatten_char_list(path_val);
        FILE *file = fopen(path, "rb");

        CeraValue payload;
        bool is_ok = false;

        if (file == NULL)
        {
            char err_buf[256];
            snprintf(err_buf, sizeof(err_buf), "File could not be opened: %s", path);
            payload.tag = VAL_STRING;
            payload.as.obj = (Obj *)newString(err_buf);
        }
        else
        {
            fseek(file, 0, SEEK_END);
            long size = ftell(file);
            fseek(file, 0, SEEK_SET);

            // Avoid double memory copying for large files by building manually here
            ObjString *content_str = (ObjString *)allocateObject(sizeof(ObjString), VAL_STRING);
            content_str->length = size;
            content_str->chars = malloc(size + 1);
            size_t read_bytes = fread(content_str->chars, 1, size, file);
            content_str->chars[read_bytes] = '\0';
            fclose(file);

            is_ok = true;
            payload.tag = VAL_STRING;
            payload.as.obj = (Obj *)content_str;
        }

        ObjADT *result_adt = newResult(is_ok, payload);
        free(path);
        release(path_val);

        CeraValue final_result;
        final_result.tag = VAL_ADT;
        final_result.as.obj = (Obj *)result_adt;
        push(vm, final_result);
        return 0;
    }

    case INTR_WRITE:
    case INTR_APPEND:
    {
        CeraValue content = pop(vm);
        CeraValue path = pop(vm);

        char *file_path = flatten_char_list(path);
        char *file_data = flatten_char_list(content);

        const char *mode = (intrinsic_id == INTR_WRITE) ? "w" : "a";
        FILE *f = fopen(file_path, mode);

        CeraValue payload;
        bool is_ok = false;

        if (f == NULL)
        {
            payload.tag = VAL_STRING;
            payload.as.obj = (Obj *)newString((intrinsic_id == INTR_WRITE)
                                                  ? "Failed to write to file"
                                                  : "Failed to append to file");
        }
        else
        {
            fputs(file_data, f);
            fclose(f);

            is_ok = true;
            payload.tag = VAL_UNIT;
            payload.as.int_val = 0;
        }

        ObjADT *adt = newResult(is_ok, payload);

        free(file_path);
        free(file_data);
        release(path);
        release(content);

        CeraValue res;
        res.tag = VAL_ADT;
        res.as.obj = (Obj *)adt;
        push(vm, res);
        return 0;
    }

    case INTR_TIME:
    {
        struct timespec ts;
        clock_gettime(CLOCK_REALTIME, &ts);
        CeraValue res;
        res.tag = VAL_INT;
        res.as.int_val = ((int64_t)ts.tv_sec * 1000LL) + ((int64_t)ts.tv_nsec / 1000000LL);
        push(vm, res);
        return 0;
    }

    case INTR_TIME_LOCAL:
    {
        struct timespec ts;
        clock_gettime(CLOCK_REALTIME, &ts);
        struct tm local_tm;
        localtime_r(&ts.tv_sec, &local_tm);
        char z_buf[8];
        strftime(z_buf, sizeof(z_buf), "%z", &local_tm);
        long offset = 0;
        if (strlen(z_buf) >= 5)
        {
            int hours = (z_buf[1] - '0') * 10 + (z_buf[2] - '0');
            int mins = (z_buf[3] - '0') * 10 + (z_buf[4] - '0');
            offset = (hours * 3600) + (mins * 60);
            if (z_buf[0] == '-')
            {
                offset = -offset;
            }
        }
        int64_t local_sec = (int64_t)ts.tv_sec + offset;

        CeraValue res;
        res.tag = VAL_INT;
        res.as.int_val = (local_sec * 1000LL) + ((int64_t)ts.tv_nsec / 1000000LL);
        push(vm, res);
        return 0;
    }

    case INTR_UPTIME:
    {
        struct timespec ts;
        clock_gettime(CLOCK_MONOTONIC, &ts);
        CeraValue res;
        res.tag = VAL_INT;
        res.as.int_val = ((int64_t)ts.tv_sec * 1000LL) + ((int64_t)ts.tv_nsec / 1000000LL);
        push(vm, res);
        return 0;
    }

    case INTR_READ_BIN:
    {
        CeraValue path_val = pop(vm);
        char *path = flatten_char_list(path_val);
        FILE *file = fopen(path, "rb");

        CeraValue payload;
        bool is_ok = false;

        if (file == NULL)
        {
            payload.tag = VAL_STRING;
            payload.as.obj = (Obj *)newString("Failed to open binary file");
        }
        else
        {
            fseek(file, 0, SEEK_END);
            long size = ftell(file);
            fseek(file, 0, SEEK_SET);

            ObjArray *arr = newArray(size);
            uint8_t *buffer = malloc(size);

            size_t bytes_read = fread(buffer, 1, size, file);
            fclose(file);

            if (bytes_read != (size_t)size)
            {
                free(buffer);
                payload.tag = VAL_STRING;
                payload.as.obj = (Obj *)newString("I/O Fault: File read interrupted or corrupted");
            }
            else
            {
                for (long i = 0; i < size; i++)
                {
                    CeraValue byte_val;
                    byte_val.tag = VAL_INT;
                    byte_val.as.int_val = buffer[i];
                    arr->elements[i] = byte_val;
                }
                free(buffer);

                is_ok = true;
                payload.tag = VAL_ARRAY;
                payload.as.obj = (Obj *)arr;
            }
        }

        ObjADT *result_adt = newResult(is_ok, payload);
        free(path);
        release(path_val);

        CeraValue final_result;
        final_result.tag = VAL_ADT;
        final_result.as.obj = (Obj *)result_adt;
        push(vm, final_result);
        return 0;
    }

    case INTR_WRITE_BIN:
    {
        CeraValue data_val = pop(vm);
        CeraValue path_val = pop(vm);

        char *file_path = flatten_char_list(path_val);
        ObjArray *arr = (ObjArray *)data_val.as.obj;
        FILE *f = fopen(file_path, "wb");

        CeraValue payload;
        bool is_ok = false;

        if (f == NULL)
        {
            payload.tag = VAL_STRING;
            payload.as.obj = (Obj *)newString("Failed to write binary file");
        }
        else
        {
            uint8_t *buffer = malloc(arr->length);
            for (int i = 0; i < arr->length; i++)
            {
                buffer[i] = (uint8_t)arr->elements[i].as.int_val;
            }

            fwrite(buffer, 1, arr->length, f);
            fclose(f);
            free(buffer);

            is_ok = true;
            payload.tag = VAL_UNIT;
            payload.as.int_val = 0;
        }

        ObjADT *adt = newResult(is_ok, payload);

        free(file_path);
        release(path_val);
        release(data_val);

        CeraValue res;
        res.tag = VAL_ADT;
        res.as.obj = (Obj *)adt;
        push(vm, res);
        return 0;
    }

    case INTR_LN:
    {
        CeraValue arg = pop(vm);
        CeraValue res;
        res.tag = VAL_FLOAT;
        res.as.float_val = log(arg.as.float_val);
        push(vm, res);
        return 0;
    }

    case INTR_FLOAT_POW:
    {
        CeraValue exp_val = pop(vm);
        CeraValue base_val = pop(vm);
        CeraValue res;
        res.tag = VAL_FLOAT;
        res.as.float_val = pow(base_val.as.float_val, exp_val.as.float_val);
        push(vm, res);
        return 0;
    }

    case INTR_THREADED_MAP:
    {
        CeraValue closure_val = pop(vm);
        CeraValue threads_val = pop(vm);
        CeraValue list_val = pop(vm);

        ObjClosure *mapped_func = (ObjClosure *)closure_val.as.obj;
        ObjList *target_list = (ObjList *)list_val.as.obj;
        int requested_threads = (int)threads_val.as.int_val;

        int len = 0;
        ObjList *curr = target_list;
        while (curr != NULL)
        {
            len++;
            curr = (ObjList *)curr->tail.as.obj;
        }

        if (len == 0)
        {
            push(vm, list_val);
            release(closure_val);
            return 0;
        }

        int num_threads = 1; 
        
        if (requested_threads > 1) {
            if (requested_threads > max_system_threads) {
                log_warning("Requested %d threads exceeds logical hardware limit (%d). Clamping allocation to prevent CPU thrashing.", 
                    requested_threads, max_system_threads);
            }

            int active = atomic_load(&global_active_threads);
            if (active < max_system_threads) {
                num_threads = (len < requested_threads) ? len : requested_threads;
                
                if (active + num_threads > max_system_threads) {
                    num_threads = max_system_threads - active;
                }
            }
        }

        atomic_fetch_add(&global_active_threads, num_threads);

        int chunk_size = len / num_threads;
        int remainder = len % num_threads;

        pthread_t threads[num_threads];
        WorkerState states[num_threads];

        pin_value(closure_val);
        pin_value(list_val);

        ObjList *chunk_start = target_list;
        for (int i = 0; i < num_threads; i++)
        {
            states[i].mapped_func = mapped_func;
            states[i].chunk_head = chunk_start;
            states[i].chunk_size = chunk_size + (i < remainder ? 1 : 0);
            states[i].active_module = vm->active_module;
            states[i].result_list.tag = VAL_LIST;
            states[i].result_list.as.obj = NULL;
            states[i].result_tail = NULL;

            // Notice we only spawn if we actually have more than 1 thread!
            // Otherwise, we could just run it synchronously on the master thread to save POSIX overhead.
            pthread_create(&threads[i], NULL, run_worker, &states[i]);

            for (int j = 0; j < states[i].chunk_size; j++)
            {
                chunk_start = (ObjList *)chunk_start->tail.as.obj;
            }
        }

        for (int i = 0; i < num_threads; i++)
        {
            pthread_join(threads[i], NULL);
        }

        // Release the resources back to the global pool
        atomic_fetch_sub(&global_active_threads, num_threads);

        CeraValue final_list;
        final_list.tag = VAL_LIST;
        final_list.as.obj = NULL;
        CeraValue *master_tail = &final_list;

        for (int i = 0; i < num_threads; i++)
        {
            if (states[i].result_list.as.obj != NULL)
            {
                *master_tail = states[i].result_list;
                master_tail = states[i].result_tail;
            }
        }

        unpin_value(closure_val);
        unpin_value(list_val);

        for (int i = 0; i < num_threads; i++)
        {
            free(states[i].arena_ptr->base);
            free(states[i].arena_ptr);
        }

        release(list_val);
        release(closure_val);

        push(vm, final_list);

        return 0;
    }

    default:
        log_error("Fatal: Unimplemented intrinsic ID: %d", intrinsic_id);
        return 1;
    }
}