#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "loader.h"
#include "memory.h"
#include "logger.h"

#define SAFE_READ(ptr, size, count, stream)                                         \
    do                                                                              \
    {                                                                               \
        if (fread(ptr, size, count, stream) != (count))                             \
        {                                                                           \
            log_error("Fatal I/O error: Unexpected EOF or corrupted .cerabc file"); \
            exit(1);                                                                \
        }                                                                           \
    } while (0)

Module *loadModule(const char *file_path)
{
    FILE *file = fopen(file_path, "rb");
    if (file == NULL)
    {
        log_error("Could not open file: %s", file_path);
        return NULL;
    }

    char magic[4];
    // The magic number check already safely evaluates the return value
    if (fread(magic, sizeof(char), 4, file) != 4 || strncmp(magic, "CERA", 4) != 0)
    {
        log_error("Invalid or corrupted .cerabc file signature");
        fclose(file);
        return NULL;
    }

    uint32_t version;
    SAFE_READ(&version, sizeof(uint32_t), 1, file);
    log_detail("Detected CeraBC Version: %d", version);

    // if (version != 2) {
    //     log_error("Incompatible CeraBC version. Expected 2 (v1.1), got %d", version);
    //     fclose(file);
    //     return NULL;
    // }

    Module *module = malloc(sizeof(Module));

    SAFE_READ(&module->entry_index, sizeof(int32_t), 1, file);
    SAFE_READ(&module->function_count, sizeof(uint32_t), 1, file);

    module->functions = malloc(sizeof(CompiledFunction) * module->function_count);

    for (uint32_t i = 0; i < module->function_count; i++)
    {
        CompiledFunction *func = &(module->functions[i]);

        SAFE_READ(&func->index, sizeof(int32_t), 1, file);
        SAFE_READ(&func->arity, sizeof(uint8_t), 1, file);
        SAFE_READ(&func->constant_count, sizeof(uint16_t), 1, file);

        log_detail("  -> Decoding Function [%d] (Arity: %d, Constants: %d)",
                   func->index, func->arity, func->constant_count);

        func->constants = malloc(sizeof(CeraValue) * func->constant_count);

        for (uint16_t c = 0; c < func->constant_count; c++)
        {
            uint8_t tag;
            SAFE_READ(&tag, sizeof(uint8_t), 1, file);

            func->constants[c].tag = tag;
            switch (tag)
            {
            case VAL_INT:
                SAFE_READ(&func->constants[c].as.int_val, sizeof(int64_t), 1, file);
                break;
            case VAL_FLOAT:
                SAFE_READ(&func->constants[c].as.float_val, sizeof(double), 1, file);
                break;
            case VAL_CHAR:
            {
                uint32_t char_val;
                SAFE_READ(&char_val, sizeof(uint32_t), 1, file);
                func->constants[c].as.int_val = char_val;
                break;
            }
            case VAL_BOOL:
            {
                uint8_t bool_val;
                SAFE_READ(&bool_val, sizeof(uint8_t), 1, file);
                func->constants[c].as.int_val = bool_val;
                break;
            }
            case VAL_UNIT:
                func->constants[c].as.int_val = 0;
                break;
            case VAL_STRING:
            {
                uint32_t length;
                SAFE_READ(&length, sizeof(uint32_t), 1, file);

                ObjString *str = (ObjString *)allocateObject(sizeof(ObjString), VAL_STRING);
                str->length = length;

                str->chars = malloc(length + 1); // +1 for null terminator
                SAFE_READ(str->chars, sizeof(char), length, file);
                str->chars[length] = '\0';

                func->constants[c].tag = VAL_STRING;
                func->constants[c].as.obj = (Obj *)str;
                break;
            }
            default:
                log_error("Unknown constant tag %d at index %d", tag, c);
                exit(1);
            }
        }

        SAFE_READ(&func->code_size, sizeof(uint32_t), 1, file);
        func->code = malloc(sizeof(uint8_t) * func->code_size);
        SAFE_READ(func->code, sizeof(uint8_t), func->code_size, file);

        log_detail("     Loaded %d bytes of bytecode", func->code_size);
    }

    fclose(file);
    return module;
}

void freeModule(Module *module)
{
    if (!module)
        return;

    for (uint32_t i = 0; i < module->function_count; i++)
    {
        CompiledFunction *func = &module->functions[i];

        free(func->code);

        for (uint16_t c = 0; c < func->constant_count; c++)
        {
            if (func->constants[c].tag == VAL_STRING)
            {
                release(func->constants[c]);
            }
        }

        free(func->constants);
    }

    free(module->functions);
    free(module);
}