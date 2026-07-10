#ifndef LOGGER_H
#define LOGGER_H

#include <stdbool.h>
#include "value.h"

// ANSI Terminal Color Codes
#define ANSI_COLOR_RED     "\x1b[31m"
#define ANSI_COLOR_YELLOW  "\x1b[33m"
#define ANSI_COLOR_WHITE   "\x1b[37m"
#define ANSI_COLOR_RESET   "\x1b[0m"

extern bool log_detailed;
extern bool log_dump_to_file;

void init_logger();
void close_logger();

void log_info(const char* format, ...);
void log_detail(const char* format, ...);
void log_warning(const char* format, ...);
void log_error(const char* format, ...);

void dump_stack(CeraValue* stack, CeraValue* stack_top);

#endif 