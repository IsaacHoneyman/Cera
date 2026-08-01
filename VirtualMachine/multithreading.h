#ifndef MULTITHREADING_H
#define MULTITHREADING_H

#include "vm.h"

extern atomic_int global_active_threads;
extern int max_system_threads;

void unpin_value(CeraValue val);
void pin_value(CeraValue val);
void *run_fold_worker(void *arg);
void *run_invoke_worker(void *arg);
void *run_pool_worker(void *arg);
void *run_worker(void *arg);
CeraValue migrate_value(CeraValue val);


#endif