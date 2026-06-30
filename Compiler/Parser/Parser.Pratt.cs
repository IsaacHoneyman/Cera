namespace Cera.Compiler.Parser;

public partial class Parser
{
    public enum Precedence
    {
        None = 0,
        Ternary = 1,      // ? :
        LogicalOr = 2,    // ||
        LogicalAnd = 3,   // &&
        Equality = 4,     // ==. !=
        Comparison = 5,  // <. >, <=, >=
        BitwiseOr = 6,    // |
        BitwiseXor = 7,   // ^
        BitwiseAnd = 8,   // &
        Shift = 9,        // <<. >>
        ListConcat = 10,  // ::
        Term = 11,        // +, -
        Factor = 12,      // *, /, %
        Unary = 13,       // !, ~. - (e.g. -5)
        Call = 14,        // (  
    }

    private delegate IExprAST PrefixParseFn();
    private delegate IExprAST InfixParseFn(IExprAST left);

    private readonly Dictionary<TokenType, PrefixParseFn> prefixParsers = [];
    private readonly Dictionary<TokenType, InfixParseFn> infixParsers = [];
    private readonly Dictionary<TokenType, Precedence> precedences = [];

    private void InitializePrattParser()
    {
        // --- PREFIX PARSERS ---

        // Literals
        RegisterPrefix(TokenType.IntLiteral, ParseLiteral);
        RegisterPrefix(TokenType.FloatLiteral, ParseLiteral);
        RegisterPrefix(TokenType.CharLiteral, ParseLiteral);
        RegisterPrefix(TokenType.StringLiteral, ParseLiteral);
        RegisterPrefix(TokenType.True, ParseLiteral);
        RegisterPrefix(TokenType.False, ParseLiteral);
        RegisterPrefix(TokenType.WildCard, ParseLiteral);

        // Identifiers
        RegisterPrefix(TokenType.Identifier, ParseIdentifier);
        RegisterPrefix(TokenType.Constructor, ParseConstructor);

        // Grouping & Complex Literals
        RegisterPrefix(TokenType.LPar, ParseGroupingOrTupleOrUnit);
        RegisterPrefix(TokenType.LBracket, ParseListLit);
        RegisterPrefix(TokenType.Arr, ParseArrLit);

        // Control Flow Expressions
        RegisterPrefix(TokenType.If, ParseIfExpr);
        RegisterPrefix(TokenType.Switch, ParseSwitchExpr);

        // Lambdas
        RegisterPrefix(TokenType.Func, ParseLambda);

        // Unary Operators
        RegisterPrefix(TokenType.Not, ParseUnary);   // !
        RegisterPrefix(TokenType.BitNot, ParseUnary);  // ~
        RegisterPrefix(TokenType.Minus, ParseUnary);  // - (e.g., -5)


        // --- INFIX PARSERS ---

        // Arithmetic Operators
        RegisterInfix(TokenType.Plus, ParseBinary, Precedence.Term);
        RegisterInfix(TokenType.Minus, ParseBinary, Precedence.Term);
        RegisterInfix(TokenType.Star, ParseBinary, Precedence.Factor);
        RegisterInfix(TokenType.Slash, ParseBinary, Precedence.Factor);
        RegisterInfix(TokenType.Mod, ParseBinary, Precedence.Factor);

        // Relational Operators
        RegisterInfix(TokenType.EqualEqual, ParseBinary, Precedence.Equality);
        RegisterInfix(TokenType.NotEqual, ParseBinary, Precedence.Equality);
        RegisterInfix(TokenType.Lesser, ParseBinary, Precedence.Comparison);
        RegisterInfix(TokenType.Greater, ParseBinary, Precedence.Comparison);
        RegisterInfix(TokenType.LesserEqual, ParseBinary, Precedence.Comparison);
        RegisterInfix(TokenType.GreaterEqual, ParseBinary, Precedence.Comparison);

        // Logical Operators
        RegisterInfix(TokenType.And, ParseBinary, Precedence.LogicalAnd);
        RegisterInfix(TokenType.Or, ParseBinary, Precedence.LogicalOr);

        // Bitwise Operators
        RegisterInfix(TokenType.BitAnd, ParseBinary, Precedence.BitwiseAnd);
        RegisterInfix(TokenType.Pipe, ParseBinary, Precedence.BitwiseOr);
        RegisterInfix(TokenType.BitXor, ParseBinary, Precedence.BitwiseXor);
        RegisterInfix(TokenType.LShift, ParseBinary, Precedence.Shift);
        RegisterInfix(TokenType.RShift, ParseBinary, Precedence.Shift);

        // List Concatenation
        RegisterInfix(TokenType.ColonColon, ParseBinary, Precedence.ListConcat);

        // Conditional Ternary
        RegisterInfix(TokenType.Question, ParseTernary, Precedence.Ternary);

        // Function Calls
        // When a '(' appears after an expression (like an identifier), it is an infix call.
        RegisterInfix(TokenType.LPar, ParseCall, Precedence.Call);
    }

    private void RegisterPrefix(TokenType type, PrefixParseFn fn)
    {
        prefixParsers[type] = fn;
    }

    private void RegisterInfix(TokenType type, InfixParseFn fn, Precedence precedence)
    {
        infixParsers[type] = fn;
        precedences[type] = precedence;
    }

    private int GetPrecedence(TokenType type)
    {
        if (precedences.TryGetValue(type, out var precedence))
        {
            return (int)precedence;
        }
        return (int)Precedence.None;
    }
}