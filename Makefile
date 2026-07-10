# Compiler settings
CC = gcc
CFLAGS = -Wall -Wextra -g

# Directories and files
SRC_DIR = VirtualMachine
OUT_DIR = Out/VirtualMachine
TARGET = $(OUT_DIR)/CeraVirtualMachine

# Automatically grab every .c file in the VirtualMachine folder
SRCS = $(wildcard $(SRC_DIR)/*.c)

# The default target that runs when you just type 'make'
all: $(TARGET)

# The rule to build the VM
$(TARGET): $(SRCS)
	@echo "Building CeraVM..."
	$(CC) $(SRCS) -o $(TARGET) $(CFLAGS)
	@echo "Build complete!"

# A rule to clean up the compiled executable
clean:
	rm -f $(TARGET)