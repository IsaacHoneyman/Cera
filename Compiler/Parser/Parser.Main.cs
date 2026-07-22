using System.Diagnostics.CodeAnalysis;
using Cera.Compiler.Logging;
using Cera.Compiler.Lexer;

namespace Cera.Compiler.Parser;

public partial class Parser(List<Token> tokens, Diagnostics diag)
{
    private int position = 0;

    public FileAST ParseFile(string filePath)
    {
        InitializePrattParser();

        diag.DetailLog($"Parsing {tokens.Count} tokens from {filePath}");
        
        return ParseProgram(filePath);
    }

    private Token Peek()
    {
        if (IsAtEnd()) return tokens[^1];
        return tokens[position];
    }

    private Token Advance()
    {
        if (!IsAtEnd()) position++;
        return tokens[position - 1];
    }

    private bool Check(TokenType type)
    {
        if (IsAtEnd()) return false;
        return Peek().Tag == type;
    }

    private Token Consume(TokenType type, string errorMessage)
    {
        if (Check(type)) return Advance();
        
        throw ThrowableFatalError(errorMessage, Peek());
    }
    
    private bool Match(TokenType type)
    {
        if (Check(type)) { 
            Advance();
            return true;
        }
        return false;
    }

    [DoesNotReturn]
    private void FatalError(string message, Token token)
    {
        throw ThrowableFatalError(message, token);
    }

    private ParserException ThrowableFatalError(string message, Token token)
    {
        ParserException e = new(message, token);
        diag.LogError(e.Message);
        return e;
    }

    private bool IsAtEnd() => position >= tokens.Count;
}