#ifndef LOADER_H
#define LOADER_H

#include "value.h"

Module* loadModule(const char* file_path); // Reads a .cerabc file from disk and constructs the Module struct

void freeModule(Module* module); // Safely frees the Module, all CompiledFunctions, and their Constant Pools

#endif 