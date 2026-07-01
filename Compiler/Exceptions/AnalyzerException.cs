using Cera.Compiler.Analyzer;

namespace Cera.Compiler.Exceptions;

public class AnalyzerException(string message, Token token) :
Exception($"Analyzer: {message} in \"{token.File}\" at Line {token.Line}, Column {token.Column}")
{
    public readonly int Line = token.Line;
    public readonly int Column = token.Column;
    public readonly string File = token.File;
}