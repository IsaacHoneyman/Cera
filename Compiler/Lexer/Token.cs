namespace Cera.Compiler.Lexer;

public readonly struct Token(TokenType tag, string lexeme, int line, int column, string file)
{
    public static readonly Dictionary<string, TokenType> KeywordTokens = new()
    {
        {"var", TokenType.Var}, {"def", TokenType.Def}, {"func", TokenType.Func}, {"type", TokenType.Type},
        {"if", TokenType.If}, {"else", TokenType.Else}, {"switch", TokenType.Switch}, {"import", TokenType.Import},
        {"int", TokenType.Int}, {"float", TokenType.Float}, {"bool", TokenType.Bool}, {"char", TokenType.Char},
        {"list", TokenType.List}, {"arr", TokenType.Arr}, {"unit", TokenType.Unit}, {"true", TokenType.True},
        {"false", TokenType.False}, {"hidden", TokenType.Hidden}, {"inline", TokenType.Inline}, {"extern", TokenType.Extern}
    };

    public readonly TokenType Tag = tag;
    public readonly string Lexeme = lexeme;

    public readonly int Line = line;
    public readonly int Column = column;
    public readonly string File = file;

    public override string ToString()
    {
        return $"{Tag}: {Lexeme}";
    }

    public static Token None()
    {
        return new(TokenType.None, "", 0, 0, "");
    }

    public static Token BuiltIn(TokenType type, string lexeme)
    {
        return new(type, lexeme, 0, 0, "<builtin>");
    }
}

public enum TokenType
{
    // keywords
    Var, Def, Func, Type, Hidden, Inline, Extern, // declaration  
    If, Else, Switch, // control flow
    Int, Float, Bool, Char, List, Arr, Unit, // primitive types
    Import,

    // literals
    True, False, // boolean literals
    Identifier, 
    Constructor,
    IntLiteral,
    FloatLiteral,
    CharLiteral,
    StringLiteral,

    // flow
    LBracket, RBracket, LBrace, RBrace, LPar, RPar,

    // structural
    Equal, Semicolon, Comma, WildCard,

    // operators
    Plus, Minus, Star, Slash, Mod, // arithmetic
    BitAnd, BitXor, BitNot, LShift, RShift, // bitwise
    EqualEqual, NotEqual, Lesser, LesserEqual, Greater, GreaterEqual, // relational
    And, Or, Not, // logical
    ColonColon, Question, Colon, // misc
    Pipe, Arrow,

    // special 
    None
}