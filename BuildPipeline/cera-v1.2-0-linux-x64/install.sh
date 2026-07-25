#!/bin/bash

# Define target Unix directories
INSTALL_DIR="$HOME/.local/share/cera"
BIN_DIR="$HOME/.local/bin"

echo "Installing Cera Compiler v1.2-0 for Linux..."

# 1. Create the installation directories if they don't exist
mkdir -p "$INSTALL_DIR"
mkdir -p "$BIN_DIR"

# 2. Copy the distribution files to the installation directory
cp -r ./bin "$INSTALL_DIR/"
cp -r ./lib "$INSTALL_DIR/"

# 3. Ensure the executables maintain their correct permissions
chmod +x "$INSTALL_DIR/bin/cera"
chmod +x "$INSTALL_DIR/bin/cerac"
chmod +x "$INSTALL_DIR/bin/ceravm"

# 4. Create a symbolic link to the global bash wrapper
ln -sf "$INSTALL_DIR/bin/cera" "$BIN_DIR/cera"

echo "Installation complete!"
echo "Cera has been installed to $INSTALL_DIR"
echo "A symbolic link has been created in $BIN_DIR"

# 5. Check if the binary directory is in the user's PATH
if [[ ":$PATH:" != *":$BIN_DIR:"* ]]; then
    echo ""
    echo "WARNING: $BIN_DIR is not in your PATH."
    echo "Add the following line to your ~/.bashrc or ~/.zshrc:"
    echo 'export PATH="$HOME/.local/bin:$PATH"'
fi