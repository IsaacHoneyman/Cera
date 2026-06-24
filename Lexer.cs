using System.Runtime.CompilerServices;

public class Lexer(string content, Diagnostics diag)
{
    private readonly string content = content;
    private readonly List<Token> tokens = [];

    private int index = 0;
    private int line = 1;
    private int column = 1;
    private int tokenStartLine = 1;
    private int tokenStartColumn = 1;

    private bool consumed = false;

    public List<Token> Lex()
    {
        while (!(index >= content.Length))
        {
            ScanToken();
        }

        tokens.Add(new Token(TokenType.EOF, "", line, column));
        return tokens;
    }

    private void ScanToken()
    {
        char c = Peek();
        consumed = false;

        tokenStartLine = line;
        tokenStartColumn = column;

        switch (c)
        {
            case '\0': return; // EOF

            // white space, consume
            case ' ': case '\r': case '\t': case '\n': break;

            // arithmetic
            case '+': Emit(TokenType.Plus, "+"); break;
            case '-':
                if (Match('>')) Emit(TokenType.Arrow, "->");
                else Emit(TokenType.Minus, "-");
                break;
            case '*': Emit(TokenType.Star, "*"); break;
            case '/':
                if (Match('/'))
                {
                    ConsumeSingleLineComment();
                    consumed = true;
                }
                else if (Match('*')) ConsumeBlockComment();
                else Emit(TokenType.Slash, "/");
                break;
            case '%': Emit(TokenType.Mod, "%"); break;

            // relational & assignment
            case '=':
                if (Match('=')) Emit(TokenType.EqualEqual, "==");
                else Emit(TokenType.Equal, "=");
                break;
            case '!':
                if (Match('=')) Emit(TokenType.NotEqual, "!=");
                else Emit(TokenType.Not, "!");
                break;
            case '<':
                if (Match('=')) Emit(TokenType.LesserEqual, "<=");
                else if (Match('<')) Emit(TokenType.LShift, "<<");
                else Emit(TokenType.Lesser, "<");
                break;
            case '>':
                if (Match('=')) Emit(TokenType.GreaterEqual, ">=");
                else if (Match('>')) Emit(TokenType.RShift, ">>");
                else Emit(TokenType.Greater, ">");
                break;

            // bitwise & logical
            case '&':
                if (Match('&')) Emit(TokenType.And, "&&");
                else Emit(TokenType.BitAnd, "&");
                break;
            case '|':
                if (Match('|')) Emit(TokenType.Or, "||");
                else Emit(TokenType.Pipe, "|");
                break;
            case '^': Emit(TokenType.BitXor, "^"); break;
            case '~': Emit(TokenType.BitNot, "~"); break;

            // Structural
            case ':':
                if (Match(':')) Emit(TokenType.ColonColon, "::");
                else Emit(TokenType.Colon, ":");
                break;
            case '?': Emit(TokenType.Question, "?"); break;

            // Puncation
            case '(': Emit(TokenType.LPar, "("); break;
            case ')': Emit(TokenType.RPar, ")"); break;
            case '{': Emit(TokenType.LBrace, "{"); break;
            case '}': Emit(TokenType.RBrace, "}"); break;
            case '[': Emit(TokenType.LBracket, "["); break;
            case ']': Emit(TokenType.RBracket, "]"); break;
            case ';': Emit(TokenType.Semicolon, ";"); break;
            case ',': Emit(TokenType.Comma, ","); break;

            case '\'': ConsumeCharLiteral(); break;
            case '"': ConsumeStringLiteral(); break;

            default:
                if (char.IsDigit(c)) ConsumeNumberLiteral();
                else if (char.IsAsciiLetter(c)) ConsumeWordLiteral();
                else FatalError($"Invalid char '{c}' found");
                break;
        }

        if (!consumed) Advance();
    }

    // --- Consumers ---

    private void ConsumeNumberLiteral()
    {
        consumed = true;

        int start = index;
        bool isFloat = false;

        while (char.IsDigit(Peek()) || (Peek() == '.' && char.IsDigit(PeekNext())))
        {
            if (Peek() == '.')
            {
                if (isFloat) break;
                isFloat = true;
            }
            Advance();
        }

        Emit(isFloat ? TokenType.FloatLiteral : TokenType.IntLiteral, content[start..index]);
    }

    private void ConsumeWordLiteral()
    {
        consumed = true;

        int start = index;
        bool isConstructor = char.IsUpper(content[start]);

        while (char.IsAsciiLetterOrDigit(Peek()) || Peek() == '_') Advance();

        string lexeme = content[start..index];

        if (Token.KeywordTokens.TryGetValue(lexeme, out TokenType type)) Emit(type, lexeme);
        else Emit(isConstructor ? TokenType.Constructor : TokenType.Identifier, lexeme);
    }

    private void ConsumeSingleLineComment()
    {
        consumed = true;

        while (Peek() != '\n' && Peek() != '\0') Advance();
    }

    private void ConsumeBlockComment()
    {
        consumed = true;

        while (Peek() != '\0')
        {
            if (Peek() == '*' && PeekNext() == '/')
            {
                Advance(); Advance();
                return;
            }
            Advance();
        }

        FatalError("Unterminated block comment");
    }

    private void ConsumeCharLiteral()
    {
        consumed = true;

        int start = index;
        Advance();


        while (Peek() != '\'' && Peek() != '\0' && Peek() != '\n') Advance();
        if (Peek() == '\'') Advance();
        else FatalError("Unterminated char literal");

        Emit(TokenType.CharLiteral, content[start..index]);
    }

    private void ConsumeStringLiteral()
    {
        consumed = true;

        int start = index;
        Advance();

        while (Peek() != '"' && Peek() != '\0' && Peek() != '\n')
        {
            if (Peek() == '\\' && PeekNext() == '"')
            {
                Advance();
            }
            Advance();
        }

        if (Peek() == '"') Advance();
        else
        {
            FatalError("Unterminated string literal");
        }

        Emit(TokenType.StringLiteral, content[start..index]);
    }



    // --- Helper Methods ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Advance()
    {
        if (index >= content.Length) return;

        if (content[index] == '\n')
        {
            column = 0;
            line++;
        }

        index++;
        column++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Emit(TokenType tag, string lexeme)
    {
        tokens.Add(new Token(tag, lexeme, tokenStartLine, tokenStartColumn));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Match(char expected)
    {
        if (PeekNext() == expected)
        {
            Advance();
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char Peek()
    {
        if (index >= content.Length) return '\0';
        return content[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private char PeekNext()
    {
        if (index + 1 >= content.Length) return '\0';
        return content[index + 1];
    }

    private void FatalError(string message)
    {
        diag.LogError($"{message} at Line {tokenStartLine}, Column {tokenStartColumn}");

        throw new LexerException(message, tokenStartLine, tokenStartColumn);
    }
}