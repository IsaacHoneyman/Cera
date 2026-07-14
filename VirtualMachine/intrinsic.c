#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <math.h>
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

        ObjADT *adt = malloc(sizeof(ObjADT));
        adt->header.type = VAL_ADT;
        adt->header.ref_count = 1;

        if (idx >= 0 && idx < arr->length)
        {
            adt->adt_tag = 0x03; // Tag for Some
            CeraValue elem = arr->elements[idx];
            retain(elem); 
            adt->payload = elem;
        }
        else
        {
            adt->adt_tag = 0x02; 
            adt->payload.tag = VAL_UNIT;
            adt->payload.as.int_val = 0;
        }

        release(arr_val); 

        CeraValue res;
        res.tag = VAL_ADT;
        res.as.obj = (Obj *)adt;

        push(vm, res);
        return 0;
    }
    case INTR_ARR_CONCAT: {        
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
        
        if (arg.tag != VAL_STRING && arg.tag != VAL_LIST)
        {
            log_error("out() currently only supports Strings and char lists.");
            return 1;
        }
        
        char* text = flatten_char_list(arg);
        printf("%s", text);
        
        free(text);
        
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
                len--;
            }
        }

        size_t final_len = strlen(buffer);
        ObjString *str = malloc(sizeof(ObjString));
        str->header.type = VAL_STRING;
        str->header.ref_count = 1;
        str->length = final_len;

        str->chars = malloc(final_len + 1);
        strcpy(str->chars, buffer);

        CeraValue result;
        result.tag = VAL_STRING;
        result.as.obj = (Obj *)str;

        push(vm, result);
        return 0;
    }
    case INTR_ARR_TO_LIST: {
        CeraValue arr_val = pop(vm);
        ObjArray* arr = (ObjArray*)arr_val.as.obj;
        
        CeraValue current_tail;
        current_tail.tag = VAL_LIST;
        current_tail.as.obj = NULL; 
        
        for (int i = arr->length - 1; i >= 0; i--) {
            ObjList* node = malloc(sizeof(ObjList));
            node->header.type = VAL_LIST;
            node->header.ref_count = 1;
            
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

    case INTR_LIST_TO_ARR: {
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
    {
        CeraValue arg = pop(vm);
        CeraValue result;
        result.tag = VAL_INT;
        result.as.int_val = arg.as.int_val;
        push(vm, result);
        return 0;
    }

    case INTR_INT_TO_CHAR:
    {
        CeraValue arg = pop(vm);
        CeraValue result;
        result.tag = VAL_CHAR;
        result.as.int_val = arg.as.int_val;
        push(vm, result);
        return 0;
    }

    case INTR_INT_TO_CHARS:
    {
        CeraValue arg = pop(vm);

        char buffer[64];
        int len = snprintf(buffer, sizeof(buffer), "%ld", (long)arg.as.int_val);

        ObjString *str = malloc(sizeof(ObjString));
        str->header.type = VAL_STRING;
        str->header.ref_count = 1;
        str->length = len;

        str->chars = malloc(len + 1);
        strcpy(str->chars, buffer);

        CeraValue result;
        result.tag = VAL_STRING;
        result.as.obj = (Obj *)str;

        push(vm, result);
        return 0;
    }

    case INTR_BOOL_TO_CHARS:
    {
        CeraValue arg = pop(vm);

        const char *text = arg.as.int_val ? "true" : "false";
        int len = strlen(text);

        ObjString *str = malloc(sizeof(ObjString));
        str->header.type = VAL_STRING;
        str->header.ref_count = 1;
        str->length = len;

        str->chars = malloc(len + 1);
        strcpy(str->chars, text);

        CeraValue result;
        result.tag = VAL_STRING;
        result.as.obj = (Obj *)str;

        push(vm, result);
        return 0;
    }
    case INTR_FLOAT_TO_CHARS:
    {
        CeraValue arg = pop(vm);
        if (arg.tag != VAL_FLOAT)
        {
            log_error("floatToChars() requires a float.");
            return 1;
        }

        char buffer[64];
        int len = snprintf(buffer, sizeof(buffer), "%g", arg.as.float_val);

        ObjString *str = malloc(sizeof(ObjString));
        str->header.type = VAL_STRING;
        str->header.ref_count = 1;
        str->length = len;
        str->chars = malloc(len + 1);
        strcpy(str->chars, buffer);

        CeraValue result;
        result.tag = VAL_STRING;
        result.as.obj = (Obj *)str;

        push(vm, result);
        return 0;
    }
    case INTR_CHARS_TO_INT:
    {
        CeraValue arg = pop(vm);
        char *str = flatten_char_list(arg);

        char *endptr;
        int64_t parsed_val = strtoll(str, &endptr, 10);

        // Skip any trailing whitespace (like \n or Windows \r) that in() might have caught
        while (*endptr != '\0' && isspace((unsigned char)*endptr))
        {
            endptr++;
        }

        ObjADT *adt = malloc(sizeof(ObjADT));
        adt->header.type = VAL_ADT;
        adt->header.ref_count = 1;

        // Use the tags revealed by your debug probe
        if (str == endptr || *endptr != '\0')
        {
            adt->adt_tag = 0x02; // Update from 0x00 to the compiler's 'None' tag
            adt->payload.tag = VAL_UNIT;
            adt->payload.as.int_val = 0;
        }
        else
        {
            adt->adt_tag = 0x03; // Update from 0x01 to the compiler's 'Some' tag
            adt->payload.tag = VAL_INT;
            adt->payload.as.int_val = parsed_val;
        }

        free(str);
        release(arg); // Prevent memory leak of the consumed string

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

        ObjADT *adt = malloc(sizeof(ObjADT));
        adt->header.type = VAL_ADT;
        adt->header.ref_count = 1;

        if (str == endptr || *endptr != '\0')
        {
            adt->adt_tag = 0x02; // None
            adt->payload.tag = VAL_UNIT;
            adt->payload.as.int_val = 0;
        }
        else
        {
            adt->adt_tag = 0x03; // Some
            adt->payload.tag = VAL_FLOAT;
            adt->payload.as.float_val = parsed_val;
        }

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

        // Optimized String / Char List Concatenation Path
        if (left_is_str && right_is_str)
        {
            char *s1 = flatten_char_list(left);
            char *s2 = flatten_char_list(right);

            uint32_t len = strlen(s1) + strlen(s2);
            ObjString *new_str = malloc(sizeof(ObjString));
            new_str->header.type = VAL_STRING;
            new_str->header.ref_count = 1;
            new_str->length = len;
            new_str->chars = malloc(len + 1);

            strcpy(new_str->chars, s1);
            strcat(new_str->chars, s2);

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

            // Unpack left list references into stack/heap memory
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
                ObjList* node = malloc(sizeof(ObjList));
                node->header.type = VAL_LIST;
                node->header.ref_count = 1;

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

        log_error("Type mismatch or unhandled tags encountered during evaluation of concat().");
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

    case INTR_SQRT: {
        CeraValue arg = pop(vm);
        
        if (arg.tag != VAL_FLOAT)
        {
            log_error("sqrt() requires a float operand.");
            return 1;
        }

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
        ObjADT *result_adt = malloc(sizeof(ObjADT));
        result_adt->header.type = VAL_ADT;
        result_adt->header.ref_count = 1;

        if (file == NULL)
        {
            result_adt->adt_tag = 0x05; 
            
            ObjString *err_msg = malloc(sizeof(ObjString));
            err_msg->header.type = VAL_STRING;
            err_msg->header.ref_count = 1;
            err_msg->length = 24;
            err_msg->chars = malloc(25);
            strcpy(err_msg->chars, "File could not be opened");

            result_adt->payload.tag = VAL_STRING;
            result_adt->payload.as.obj = (Obj *)err_msg;
        }
        else
        {
            fseek(file, 0, SEEK_END);
            long size = ftell(file);
            fseek(file, 0, SEEK_SET);

            ObjString *content_str = malloc(sizeof(ObjString));
            content_str->header.type = VAL_STRING;
            content_str->header.ref_count = 1;
            content_str->length = size;
            content_str->chars = malloc(size + 1);

            size_t read_bytes = fread(content_str->chars, 1, size, file);
            content_str->chars[read_bytes] = '\0';
            fclose(file);

            // Success path: Construct Ok(content) -> Tag 0x04
            result_adt->adt_tag = 0x04;
            result_adt->payload.tag = VAL_STRING;
            result_adt->payload.as.obj = (Obj *)content_str;
        }

        free(path);
        release(path_val);

        CeraValue final_result;
        final_result.tag = VAL_ADT;
        final_result.as.obj = (Obj *)result_adt;
        push(vm, final_result);
        return 0;
    }

    case INTR_WRITE:
    {
        CeraValue content = pop(vm);
        CeraValue path = pop(vm);
        
        char* file_path = flatten_char_list(path);
        char* file_data = flatten_char_list(content);
        
        FILE *f = fopen(file_path, "w");
        
        // Allocate the ADT Wrapper
        ObjADT* adt = (ObjADT*)malloc(sizeof(ObjADT));
        adt->header.type = VAL_ADT;
        adt->header.ref_count = 1;

        if (f == NULL)
        {
            // Construct the Error(string) payload
            adt->adt_tag = 0x04; 
            CeraValue err_val;
            err_val.tag = VAL_STRING;
            
            // NOTE: newString is defined in your memory.h
            err_val.as.obj = (Obj*)newString("Failed to write to file");
            adt->payload = err_val;
        }
        else
        {
            // Write to disk
            fputs(file_data, f);
            fclose(f);
            
            // Construct the Ok(unit) payload
            adt->adt_tag = 0x05; 
            CeraValue ok_val;
            ok_val.tag = VAL_UNIT;
            ok_val.as.int_val = 0;
            adt->payload = ok_val;
        }
        
        free(file_path);
        free(file_data);
        
        CeraValue res;
        res.tag = VAL_ADT;
        res.as.obj = (Obj*)adt;
        push(vm, res);
        
        return 0;
    }

    default:
        log_error("Fatal: Unimplemented intrinsic ID: %d", intrinsic_id);
        return 1;
    }
}