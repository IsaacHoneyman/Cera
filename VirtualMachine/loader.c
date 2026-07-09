#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "loader.h"

Module* loadModule(const char* file_path) {
    FILE* file = fopen(file_path, "rb");
    if (file == NULL) {
        return NULL;
    }

    char magic[4];
    if (fread(magic, sizeof(char), 4, file) != 4 || strncmp(magic, "CERA", 4) != 0) {
        fprintf(stderr, "Error: Invalid or corrupted .cerabc file.\n");
        fclose(file);
        return NULL;
    }

    uint32_t version;
    fread(&version, sizeof(uint32_t), 1, file);

    Module* module = malloc(sizeof(Module));
    
    fread(&module->entry_index, sizeof(int32_t), 1, file);
    fread(&module->function_count, sizeof(uint32_t), 1, file);

    module->functions = malloc(sizeof(CompiledFunction) * module->function_count);
    
    fclose(file);
    return module;
}

void freeModule(Module* module) {
    if (!module) return;
    
    // TODO: Loop through functions and free their bytecode and constant arrays
    free(module->functions);
    free(module);
}