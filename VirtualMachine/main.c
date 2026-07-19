#include <stdio.h>
#include <stdlib.h>
#include "vm.h"
#include "loader.h"
#include "logger.h"
#include <string.h>

int main(int argc, char** argv) {
    const char* file_path = NULL;

    int cera_argc = 0;
    char** cera_argv = NULL;

    for (int i = 1; i < argc; i++) {
        if (strcmp(argv[i], "-d") == 0) {
            log_detailed = true;
        } else if (strcmp(argv[i], "-f") == 0) {
            log_dump_to_file = true;
        } else if (strcmp(argv[i], "-s") == 0) {
            log_silent = false;
        } else if (argv[i][0] == '-') {
            log_warning("Unknown VM flag: %s", argv[i]);
        } else {
            file_path = argv[i];
            cera_argc = argc - i - 1;
            cera_argv = &argv[i + 1];
            break;
        }
    }

    init_logger();

    if (file_path == NULL) {
        log_error("Usage: ceravm [-d] [-f] <path_to_file.cerabc> [program_args...]");        
        return 1;
    }

    log_info("Starting CeraVM...");
    log_detail("Attempting to load module from: %s", file_path);
    if (cera_argc > 0) {
        log_detail("Program arguments detected: %d", cera_argc);
    }

    Module* module = loadModule(file_path);
    if (module == NULL) {
        log_error("Failed to load module");
        return 1;
    }

    log_info("Module loaded successfully. Function count: %d", module->function_count);

    VM vm;
    initVM(&vm, module, cera_argc, cera_argv);
    int exit_code = runVM(&vm); 
    if (exit_code == 1) {
        log_error("Virtual Machine terminated");
        return 1;
    }
    freeVM(&vm);

    freeModule(module);
    log_info("Virtual Machine finished cleanly");
    
    close_logger();
    return 0;
}