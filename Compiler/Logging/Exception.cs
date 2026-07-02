using Cera.Compiler.Lexer;

namespace Cera.Compiler.Logging;

public abstract class CeraException : Exception
{
    public readonly int Line;
    public readonly int Column;
    public readonly string File;

    protected CeraException(string phase, string message, int line, int column, string file)
    : base($"{phase}: {message} in \"{file}\" at Line {line}, Column {column}")
    {
        Line = line;
        Column = column;
        File = file;
    }

    protected CeraException(string phase, string message, Token token)
    : base(token.Tag == TokenType.None
    ? $"{phase}: {message}" // Clean message if None
    : $"{phase}: {message} in \"{token.File}\" at Line {token.Line}, Column {token.Column}")
    {
        Line = token.Tag == TokenType.None ? 0 : token.Line;
        Column = token.Tag == TokenType.None ? 0 : token.Column;
        File = token.Tag == TokenType.None ? "" : token.File;
    }
}

/// --- Compiler Stage Exceptions ---

public class CompilerException(string message) : CeraException("Compiler", message, Token.None());

public class LexerException(string message, int line, int column, string file) : 
    CeraException("Lexer", message, line, column, file);

public class ImportResolverException(string message, Token token) : 
    CeraException("Import Resolver", message, token);

public class ParserException(string message, Token token) : 
    CeraException("Parser", message, token);

public class AnalyzerException(string message, Token token) :
    CeraException("Analyser", message, token);