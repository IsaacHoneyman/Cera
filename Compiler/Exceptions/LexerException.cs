namespace Cera.Compiler.Exceptions;

public class LexerException(string message, int line, int column, string file) : 
Exception($"Lexer: {message} in \"{file}\" at Line {line}, Column {column}")
{
    public readonly int Line = line;
    public readonly int Column = column;
    public readonly string File = file;
}