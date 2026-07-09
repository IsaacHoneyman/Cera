#ifndef VM_H
#define VM_H

#include "value.h"
#define FRAMES_MAX 256
#define STACK_MAX (FRAMES_MAX * 255) 

typedef struct {
    ObjClosure* closure;
    uint8_t* ip;         // Instruction Pointer
    CeraValue* slots;    // Pointer into the VM's operand stack for locals
} CallFrame;

typedef struct {
    CallFrame frames[FRAMES_MAX];
    int frame_count;
    
    CeraValue stack[STACK_MAX];
    CeraValue* stack_top;
    
    Module* active_module;
    ObjUpvalue* open_upvalues; // Head of the open upvalue linked list
} VM;

#endif 