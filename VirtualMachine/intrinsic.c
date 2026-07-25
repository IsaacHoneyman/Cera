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
        
        ObjArray* left = (ObjArray*)left_val.as.obj;
        ObjArray* right = (ObjArray*)right_val.as.obj;
        
        ObjArray* new_arr = newArray(left->length + right->length);
        
        for (int i = 0; i < left->length; i++) {
            new_arr->elements[i] = left->elements[i];
            retain(new_arr->elements[i]);
        }
        for (int i = 0; i < right->length; i++) {
            new_arr->elements[left->length + i] = right->elements[i];
            retain(new_arr->elements[left->length + i]);
        }
        
        release(left_val);
        release(right_val);
        
        CeraValue res;
        res.tag = VAL_ARRAY;
        res.as.obj = (Obj*)new_arr;
        
        push(vm, res);
        return 0;
    }

    case INTR_OUT:
    {
        CeraValue arg = pop(vm);
        
        char* text = flatten_char_list(arg);
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
        ObjArray* arr = (ObjArray*)arr_val.as.obj;
        
        CeraValue current_tail;
        current_tail.tag = VAL_LIST;
        current_tail.as.obj = NULL; 
        
        for (int i = arr->length - 1; i >= 0; i--) {
            // Using correct managed allocator abstraction
            ObjList* node = (ObjList*)allocateObject(sizeof(ObjList), VAL_LIST);
            
            CeraValue elem = arr->elements[i];
            retain(elem);
            
            node->head = elem;
            node->tail = current_tail; 
            
            current_tail.tag = VAL_LIST;
            current_tail.as.obj = (Obj*)node;
        }
        
        release(arr_val);
        push(vm, current_tail);
        return 0;
    }

    case INTR_LIST_TO_ARR: 
    {
        CeraValue lst_val = pop(vm);
        
        int len = 0;
        ObjList* curr = (ObjList*)lst_val.as.obj;
        while (curr != NULL) {
            len++;
            if (curr->tail.tag != VAL_LIST) break;
            curr = (ObjList*)curr->tail.as.obj;
        }
        
        ObjArray* new_arr = newArray(len);
        curr = (ObjList*)lst_val.as.obj;
        
        for (int i = 0; i < len; i++) {
            if (curr == NULL) break;
            
            new_arr->elements[i] = curr->head;
            retain(curr->head);
            
            if (curr->tail.tag != VAL_LIST) break;
            curr = (ObjList*)curr->tail.as.obj;
        }
        
        release(lst_val);
        
        CeraValue res;
        res.tag = VAL_ARRAY;
        res.as.obj = (Obj*)new_arr;
        
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
        while (*endptr != '\0' && isspace((unsigned char)*endptr)) { endptr++; }

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
        while (*endptr != '\0' && isspace((unsigned char)*endptr)) { endptr++; }

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
            char* temp_buf = malloc(len + 1);
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
            if (left.as.obj == NULL) {
                release(left); 
                push(vm, right);
                return 0;
            }

            int left_len = 0;
            ObjList* curr = (ObjList*)left.as.obj;
            while (curr != NULL) {
                left_len++;
                if (curr->tail.tag != VAL_LIST) break;
                curr = (ObjList*)curr->tail.as.obj;
            }

            CeraValue* temp_buffer = malloc(sizeof(CeraValue) * left_len);
            curr = (ObjList*)left.as.obj;
            for (int i = 0; i < left_len; i++) {
                if (curr == NULL) break;
                temp_buffer[i] = curr->head;
                if (curr->tail.tag != VAL_LIST) break;
                curr = (ObjList*)curr->tail.as.obj;
            }

            CeraValue current_tail = right;
            retain(current_tail); 

            for (int i = left_len - 1; i >= 0; i--) {
                ObjList* node = (ObjList*)allocateObject(sizeof(ObjList), VAL_LIST);

                CeraValue head_val = temp_buffer[i];
                retain(head_val);

                node->head = head_val;
                node->tail = current_tail; 

                current_tail.tag = VAL_LIST;
                current_tail.as.obj = (Obj*)node;
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
        
        char* file_path = flatten_char_list(path);
        char* file_data = flatten_char_list(content);
        
        const char* mode = (intrinsic_id == INTR_WRITE) ? "w" : "a";
        FILE *f = fopen(file_path, mode);
        
        CeraValue payload;
        bool is_ok = false;

        if (f == NULL)
        {
            payload.tag = VAL_STRING;
            payload.as.obj = (Obj*)newString((intrinsic_id == INTR_WRITE) 
                ? "Failed to write to file" : "Failed to append to file");
        }
        else
        {
            fputs(file_data, f);
            fclose(f);
            
            is_ok = true;
            payload.tag = VAL_UNIT;
            payload.as.int_val = 0;
        }
        
        ObjADT* adt = newResult(is_ok, payload);
        
        free(file_path);
        free(file_data);
        release(path);   
        release(content);
        
        CeraValue res;
        res.tag = VAL_ADT;
        res.as.obj = (Obj*)adt;
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
        if (strlen(z_buf) >= 5) {
            int hours = (z_buf[1] - '0') * 10 + (z_buf[2] - '0');
            int mins = (z_buf[3] - '0') * 10 + (z_buf[4] - '0');
            offset = (hours * 3600) + (mins * 60);
            if (z_buf[0] == '-') {
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
        
        char* file_path = flatten_char_list(path_val);
        ObjArray* arr = (ObjArray*)data_val.as.obj;
        FILE *f = fopen(file_path, "wb");
        
        CeraValue payload;
        bool is_ok = false;

        if (f == NULL)
        {
            payload.tag = VAL_STRING;
            payload.as.obj = (Obj*)newString("Failed to write binary file");
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
        
        ObjADT* adt = newResult(is_ok, payload);
        
        free(file_path);
        release(path_val);   
        release(data_val);
        
        CeraValue res;
        res.tag = VAL_ADT;
        res.as.obj = (Obj*)adt;
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

    default:
        log_error("Fatal: Unimplemented intrinsic ID: %d", intrinsic_id);
        return 1;
    }
}