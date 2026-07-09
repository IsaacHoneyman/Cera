#include <stdio.h>
#include <stdlib.h>
#include "vm.h"
#include "loader.h"

int main(int argc, char** argv) {
    if (argc != 2) {
        fprintf(stderr, "Usage: ceravm <path_to_file.cerabc>\n");
        return 1;
    }

    Module* module = loadModule(argv[1]);
    if (module == NULL) {
        fprintf(stderr, "Error: Failed to load module\n");
        return 1;
    }

    freeModule(module);
    fprintf(stderr, "Virtual Machine Finished\n");
    return 0;
}