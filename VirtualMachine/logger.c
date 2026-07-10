#include <stdio.h>
#include <stdarg.h>
#include <time.h>   
#include "logger.h"

bool log_detailed = false;
bool log_dump_to_file = false;

static FILE* dump_file = NULL;

void init_logger() {
    if (!log_dump_to_file) return;

    time_t now = time(NULL);
    struct tm* t = localtime(&now);
    
    char filepath[256];
    strftime(filepath, sizeof(filepath), "Out/Dump/Cera_VM_Dump_%Y-%m-%d_%H-%M-%S.txt", t);

    dump_file = fopen(filepath, "w");
    if (dump_file == NULL) {
        fprintf(stderr, ANSI_COLOR_RED "[Error] Could not create dump file: %s" ANSI_COLOR_RESET "\n", filepath);
    }
}

void close_logger() {
    if (dump_file) {
        fclose(dump_file);
        dump_file = NULL;
    }
}

void log_info(const char* format, ...) {
    va_list args;
    va_start(args, format);
    
    if (dump_file) {
        va_list args_copy;
        va_copy(args_copy, args);
        vfprintf(dump_file, format, args_copy);
        fprintf(dump_file, "\n");
        va_end(args_copy);
    }

    printf(ANSI_COLOR_WHITE);
    vprintf(format, args);
    printf(ANSI_COLOR_RESET "\n");
    
    va_end(args);
}

void log_detail(const char* format, ...) {
    if (!log_detailed) return;
    
    va_list args;
    va_start(args, format);
    
    if (dump_file) {
        va_list args_copy;
        va_copy(args_copy, args);
        vfprintf(dump_file, format, args_copy);
        fprintf(dump_file, "\n");
        va_end(args_copy);
    }

    printf(ANSI_COLOR_WHITE);
    vprintf(format, args);
    printf(ANSI_COLOR_RESET "\n");
    
    va_end(args);
}

void log_warning(const char* format, ...) {
    va_list args;
    va_start(args, format);
    
    if (dump_file) {
        fprintf(dump_file, "[Warning] ");
        va_list args_copy;
        va_copy(args_copy, args);
        vfprintf(dump_file, format, args_copy);
        fprintf(dump_file, "\n");
        va_end(args_copy);
    }

    printf(ANSI_COLOR_YELLOW "[Warning] ");
    vprintf(format, args);
    printf(ANSI_COLOR_RESET "\n");
    
    va_end(args);
}

void log_error(const char* format, ...) {
    va_list args;
    va_start(args, format);
    
    if (dump_file) {
        fprintf(dump_file, "[Error] ");
        va_list args_copy;
        va_copy(args_copy, args);
        vfprintf(dump_file, format, args_copy);
        fprintf(dump_file, "\n");
        va_end(args_copy);
    }

    fprintf(stderr, ANSI_COLOR_RED "[Error] ");
    vfprintf(stderr, format, args);
    fprintf(stderr, ANSI_COLOR_RESET "\n");
    
    va_end(args);
}

void dump_stack(CeraValue* stack, CeraValue* stack_top) {
    printf(ANSI_COLOR_WHITE "          "); 
    
    for (CeraValue* slot = stack; slot < stack_top; slot++) {
        printf("[ ");
        
        switch (slot->tag) {
            case VAL_INT:
                printf("%ld", slot->as.int_val);
                break;
            case VAL_FLOAT:
                printf("%f", slot->as.float_val);
                break;
            case VAL_BOOL:
                printf(slot->as.int_val == 1 ? "true" : "false");
                break;
            case VAL_CHAR:
                printf("'%c'", (char)slot->as.int_val); 
                break;
            case VAL_UNIT:
                printf("()");
                break;
            case VAL_STRING: {
                ObjString* str = (ObjString*)slot->as.obj;
                printf("\"%s\"", str->chars);
                break;
            }
            case VAL_CLOSURE:
                printf("<closure fn:%d>", ((ObjClosure*)slot->as.obj)->function_index);
                break;
            default:
                printf("<obj tag:%d>", slot->tag);
                break;
        }
        
        printf(" ]");
    }
    printf(ANSI_COLOR_RESET "\n");
}