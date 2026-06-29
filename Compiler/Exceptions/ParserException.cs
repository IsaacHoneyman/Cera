namespace Cera.Compiler.Exceptions;

public class ParserException(string message, Token token) :
Exception($"Parser: {message} in \"{token.File}\" at Line {token.Line}, Column {token.Column}")
{
    public readonly int Line = token.Line;
    public readonly int Column = token.Column;
    public readonly string File = token.File;
}