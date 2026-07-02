using Cera.Compiler.Lexer;

namespace Cera.Compiler.Parser;

public partial class Parser
{
    private ProgramNode ParseProgram()
    {
        List<FuncDeclNode> functions = [];
        List<TypeDeclNode> types = [];

        while (!IsAtEnd())
        {
            if (Check(TokenType.Def)) functions.Add(ParseFuncDecl());
            else if (Check(TokenType.Type)) types.Add(ParseTypeDecl());
            else FatalError("Expected 'def' or 'type' declaration at the top level", Peek());
        }

        return new ProgramNode(functions, types);
    }

    // --- Decl ---

    private FuncDeclNode ParseFuncDecl()
    {
        Consume(TokenType.Def, "Expected 'def' keyword");
        var id = Consume(TokenType.Identifier, "Expected function name");

        GenericDeclNode? generics = Check(TokenType.Lesser) ? ParseGenericDecl() : null;
        Consume(TokenType.LPar, "Expected '(' after function name");

        List<ParamNode> parameters = [];
        if (!Check(TokenType.RPar))
        {
            do parameters.Add(ParseParamDecl());
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RPar, "Expected ')' after function parameters");
        Consume(TokenType.Colon, "Expected ':'");
        var returnType = ParseType();
        Consume(TokenType.Equal, "Expected '=' after function declaration");
        var exprBlock = ParseExpressionBlock();

        return new FuncDeclNode(id, generics, parameters, returnType, exprBlock);
    }

    private GenericDeclNode ParseGenericDecl()
    {
        List<Token> generics = [];
        Consume(TokenType.Lesser, "Expected '<'");

        do generics.Add(Consume(TokenType.Identifier, "Expected generic type identifier"));
        while (Match(TokenType.Comma));

        Consume(TokenType.Greater, "Expected '>'");

        return new GenericDeclNode(generics);
    }

    private ParamNode ParseParamDecl()
    {
        var id = Consume(TokenType.Identifier, "Expected param identifier");
        Consume(TokenType.Colon, "Expected ':'");
        var type = ParseType();

        return new ParamNode(id, type);
    }

    private TypeDeclNode ParseTypeDecl()
    {
        Consume(TokenType.Type, "Expected 'type' keyword");
        var id = Consume(TokenType.Identifier, "Expected type identifier");
        GenericDeclNode? generics = Check(TokenType.Lesser) ? ParseGenericDecl() : null;
        Consume(TokenType.Equal, "Expected '=' in type declaration");

        List<ConDeclNode> constructors = [];
        do constructors.Add(ParseConDecl());
        while (Match(TokenType.Pipe));

        Consume(TokenType.Semicolon, "Expected ';' after type declaration");
        return new TypeDeclNode(id, generics, constructors);
    }

    private ConDeclNode ParseConDecl()
    {
        var id = Consume(TokenType.Constructor, "Expected constructor name");

        ITypeAST? payload = null;
        if (Match(TokenType.Colon)) payload = ParseType();

        return new ConDeclNode(id, payload);
    }

    private VarDeclStmt ParseVarDecl()
    {
        Consume(TokenType.Var, "Expected 'var' keyword");
        var id = Consume(TokenType.Identifier, "Expected variable identifier");

        ITypeAST? declaredType = null;
        if (Match(TokenType.Colon)) declaredType = ParseType();

        Consume(TokenType.Equal, "Expected '=' in variable declaration");
        var init = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after variable declaration");

        return new VarDeclStmt(id, declaredType, init);
    }

    // --- Expressions ---

    private ExprBlock ParseExpressionBlock()
    {
        Consume(TokenType.LBrace, "Expected '{' to begin expression block");

        List<IStmtAST> stmts = [];// Update the falseBranch assignment:
        IExprAST? returnExpr = null;

        while (!Check(TokenType.RBrace) && !IsAtEnd())
        {
            if (Check(TokenType.Var)) stmts.Add(ParseVarDecl());
            else
            {
                var expr = ParseExpression();

                if (Check(TokenType.RBrace))
                {
                    returnExpr = expr;
                    break;
                }

                Consume(TokenType.Semicolon, "Expected ';' after expression");

                if (Check(TokenType.RBrace)) // to allow optional semicolon
                {
                    returnExpr = expr;
                    break;
                }
                
                stmts.Add(new ExprStmt(expr));
            }
        }

        Consume(TokenType.RBrace, "Expected '}' to end expression block");
        if (returnExpr == null) throw ThrowableFatalError("Expression block must evaluate to a final expression", Peek());
        return new ExprBlock(stmts, returnExpr);
    }

    private IExprAST ParseExpression(Precedence precedence = Precedence.None)
    {
        var token = Peek();
        if (!prefixParsers.TryGetValue(token.Tag, out var prefixFn))
            throw ThrowableFatalError($"Expected expression, found '{token.Tag}'", token);

        var left = prefixFn(); // grab function associated with thing
        while (!IsAtEnd() && (int)precedence < GetPrecedence(Peek().Tag))
        {
            var nextToken = Peek();

            if (!infixParsers.TryGetValue(nextToken.Tag, out var infixFn)) break;

            left = infixFn(left);
        }

        return left;
    }

    private IExprAST ParseLiteral()
    {
        return new LiteralExpr(Advance());
    }

    private IExprAST ParseConstructor()
    {
        var conName = Consume(TokenType.Constructor, "Expected constructor name");
        List<IExprAST> payloads = [];

        if (Match(TokenType.LPar))
        {
            if (!Check(TokenType.RPar))
            {
                do payloads.Add(ParseExpression());
                while (Match(TokenType.Comma));
            }
            Consume(TokenType.RPar, "Expected ')' after constructor payload pattern");
        }

        return new ConExpr(conName, payloads);
    }

    private IExprAST ParseIdentifier()
    {
        return new IdentifierExpr(Advance());
    }

    private IExprAST ParseGroupingOrTupleOrUnit()
    {
        var lPar = Consume(TokenType.LPar, "Expected '('");

        if (Match(TokenType.RPar))
            return new LiteralExpr(new(TokenType.LPar, "()", lPar.Line, lPar.Column, lPar.File));

        List<IExprAST> exprs = [];

        do exprs.Add(ParseExpression());
        while (Match(TokenType.Comma));

        Consume(TokenType.RPar, "Expected ')'");

        if (exprs.Count == 1) return exprs[0];
        return new TupleLitExpr(lPar, exprs);
    }

    private IExprAST ParseListLit()
    {
        var lBrac = Consume(TokenType.LBracket, "Expected '['");

        List<IExprAST> exprs = [];
        if (!Check(TokenType.RBracket))
        {
            do exprs.Add(ParseExpression());
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RBracket, "Expected ']'");

        return new ListLitExpr(lBrac, exprs);
    }

    private IExprAST ParseArrLit()
    {
        Consume(TokenType.Arr, "Expected 'arr' keyword");
        var lBrac =  Consume(TokenType.LBracket, "Expected '['");

        List<IExprAST> exprs = [];
        if (!Check(TokenType.RBracket))
        {
            do exprs.Add(ParseExpression());
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RBracket, "Expected ']'");

        return new ArrLitExpr(lBrac, exprs);
    }

    private IExprAST ParseIfExpr()
    {
        Consume(TokenType.If, "Expected 'if' keyword");
        var op = Consume(TokenType.LPar, "Expected '(' after 'if'");
        var condition = ParseExpression();
        Consume(TokenType.RPar, "Expected ')' after if condition");

        var trueBlock = ParseExpressionBlock();

        List<(IExprAST condition, ExprBlock block)> elseIfs = [];
        ExprBlock? elseBlock = null;

        while (Match(TokenType.Else))
        {
            if (Match(TokenType.If))
            {
                Consume(TokenType.LPar, "Expected '(' after 'if'");
                var elifCondition = ParseExpression();
                Consume(TokenType.RPar, "Expected ')' after if condition");
                elseIfs.Add((elifCondition, ParseExpressionBlock()));
            }
            else
            {
                elseBlock = ParseExpressionBlock();
                break;
            }
        }

        return new IfExpr(op, condition, trueBlock, elseIfs, elseBlock);
    }

    private IExprAST ParseSwitchExpr()
    {
        Consume(TokenType.Switch, "Expected 'switch' keyword");
        var op = Consume(TokenType.LPar, "Expected '(' after switch");
        var target = ParseExpression();
        Consume(TokenType.RPar, "Expected ')' after switch target");
        Consume(TokenType.LBrace, "Expected '{' to begin switch cases");

        List<PatternMatchNode> cases = [];

        do
        {
            var pattern = ParsePattern();
            Consume(TokenType.Arrow, "Expected '->' after pattern in switch case");
            cases.Add(new PatternMatchNode(pattern, ParseExpression()));
        }
        while (Match(TokenType.Comma));

        Consume(TokenType.RBrace, "Expected '}' to close switch cases");

        return new SwitchExpr(op, target, cases);
    }

    private IExprAST ParseLambda()
    {
        Consume(TokenType.Func, "Expected 'func' keyword");
        Consume(TokenType.LPar, "Expected '(' after 'func'");

        List<ParamNode> parameters = [];
        if (!Check(TokenType.RPar))
        {
            do parameters.Add(ParseParamDecl());
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RPar, "Expected ')' after lambda parameters");
        Consume(TokenType.Colon, "Expected ':' after lambda parameters");
        var returnType = ParseType();
        Consume(TokenType.Equal, "Expected '=' before lambda body");

        IExprAST body;
        if (Check(TokenType.LBrace)) body = ParseExpressionBlock();
        else body = ParseExpression();

        return new LambdaExpr(parameters, returnType, body);
    }

    private IExprAST ParseUnary()
    {
        var opToken = Advance();
        return new UnaryExpr(opToken, ParseExpression(Precedence.Unary));
    }

    private IExprAST ParseBinary(IExprAST left)
    {
        var opToken = Advance();
        var precedence = (int)GetPrecedence(opToken.Tag);

        if (opToken.Tag == TokenType.ColonColon) precedence -= 1; // as right associative

        return new BinaryExpr(left, opToken, ParseExpression((Precedence)precedence)); 
    }

    private IExprAST ParseTernary(IExprAST left)
    {
        Token op = Consume(TokenType.Question, "Expected '?'");
        var trueBranch = ParseExpression();
        Consume(TokenType.Colon, "Expected ':' in ternary expression");
        var falseBranch = ParseExpression((Precedence)((int)Precedence.Ternary - 1)); // as right associative
        return new TernaryExpr(left, op, trueBranch, falseBranch);
    }


    private IExprAST ParseCall(IExprAST left)
    {
        Consume(TokenType.LPar, "Expected '(' for function call");

        List<IExprAST> arguments = [];

        if (!Check(TokenType.RPar))
        {
            do arguments.Add(ParseExpression());
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RPar, "Expected ')' after arguments");

        return new CallExpr(left, arguments);
    }

    // --- Types ---

    private ITypeAST ParseType()
    {
        var type = ParseSuffixType();
        if (Match(TokenType.Arrow)) return new FuncType(type, ParseType()); // func type 
        return type;
    }

    private ITypeAST ParseSuffixType()
    {
        var type = ParseAtomType();

        while (true)
        {
            if (Match(TokenType.List)) type = new ListType(type);
            else if (Match(TokenType.Arr)) type = new ArrType(type);
            else break;
        }

        return type;
    }

    private ITypeAST ParseAtomType()
    {
        var t = Peek();

        return t.Tag switch
        {
            TokenType.Int or TokenType.Float or TokenType.Bool or TokenType.Char or TokenType.Unit =>
                new BaseType(Advance()),
            TokenType.Identifier =>
                ParseCustomOrGenericType(),
            TokenType.LPar =>
                ParseTupleOrWrappedType(),
            _ => throw ThrowableFatalError("Valid type expected", t),
        };
    }

    private ITypeAST ParseCustomOrGenericType()
    {
        var id = Consume(TokenType.Identifier, "Expected generic type identifier");
        
        if (!Match(TokenType.Lesser)) 
        {
            return new BaseType(id); 
        }
        
        List<ITypeAST> types = [];
        do types.Add(ParseType());
        while (Match(TokenType.Comma));

        // id<id<x>> fix
        var end = Peek();
        if (end.Tag == TokenType.RShift)
        {
            Advance();
            tokens.Insert(position, new Token(TokenType.Greater, ">", end.Line, end.Column, end.File));
            tokens.Insert(position, new Token(TokenType.Greater, ">", end.Line, end.Column, end.File));
        }

        Consume(TokenType.Greater, "Expected '>'");

        return new GenericType(id, types);
    }

    private ITypeAST ParseTupleOrWrappedType()
    {
        Consume(TokenType.LPar, "Expected '('");

        List<ITypeAST> types = [];

        do types.Add(ParseType());
        while (Match(TokenType.Star));

        Consume(TokenType.RPar, "Expected ')'");

        if (types.Count == 1) return types[0];
        return new TupleType(types);
    }

    // --- Patterns ---

    private IPatternAST ParsePattern()
    {
        var token = Peek();

        return token.Tag switch
        {
            TokenType.IntLiteral or TokenType.FloatLiteral or
            TokenType.CharLiteral or TokenType.StringLiteral or 
            TokenType.True or TokenType.False or TokenType.WildCard => new LiteralPattern(Advance()),
            TokenType.Identifier => new IdPattern(Advance()),
            TokenType.Constructor => ParseConstructorPattern(),
            TokenType.LPar => ParseTupleOrConsOrUnitPattern(),
            TokenType.LBracket => ParseListPattern(),
            TokenType.Arr => ParseArrPattern(),
            _ => throw ThrowableFatalError("Expected a valid pattern", token),
        };
    }

    private ConPattern ParseConstructorPattern()
    {
        var conName = Consume(TokenType.Constructor, "Expected constructor name in pattern");
        List<IPatternAST> payloads = [];

        if (Match(TokenType.LPar))
        {
            if (!Check(TokenType.RPar))
            {
                do payloads.Add(ParsePattern());
                while (Match(TokenType.Comma));
            }
            Consume(TokenType.RPar, "Expected ')' after constructor payload pattern");
        }

        return new ConPattern(conName, payloads);
    }

    private IPatternAST ParseTupleOrConsOrUnitPattern()
    {
        var lPar = Consume(TokenType.LPar, "Expected '('");
        if (Match(TokenType.RPar))
            return new LiteralPattern(new(TokenType.LPar, "()", lPar.Line, lPar.Column, lPar.File));

        var patternA = ParsePattern();
        if (Match(TokenType.ColonColon))
        {
            var patternB = ParsePattern();
            Consume(TokenType.RPar, "Expected ')' after cons pattern");
            return new ConsPattern(patternA, patternB);
        }
        
        if (Match(TokenType.RPar)) return patternA;

        List<IPatternAST> patterns = [patternA];
        while (Match(TokenType.Comma)) patterns.Add(ParsePattern());

        Consume(TokenType.RPar, "Expected ')' after tuple pattern");
        return new TuplePattern(patterns);
    }

    private ListPattern ParseListPattern()
    {
        Consume(TokenType.LBracket, "Expected '[' to begin list pattern");
        List<IPatternAST> patterns = [];

        if (!Check(TokenType.RBracket))
        {
            do patterns.Add(ParsePattern());
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RBracket, "Expected ']' to end list pattern");
        return new ListPattern(patterns);
    }

    private ArrPattern ParseArrPattern()
    {
        Consume(TokenType.Arr, "Expected 'arr' keyword");
        Consume(TokenType.LBracket, "Expected '[' to begin array pattern");
        List<IPatternAST> patterns = [];

        if (!Check(TokenType.RBracket))
        {
            do patterns.Add(ParsePattern());
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RBracket, "Expected ']' to end array pattern");
        return new ArrPattern(patterns);
    }
}