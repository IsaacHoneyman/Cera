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
        throw new NotImplementedException();
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

        List<IStmtAST> stmts = [];
        IExprAST? returnExpr = null;

        while (!Check(TokenType.RBrace) && !IsAtEnd())
        {
            if (Check(TokenType.Var)) stmts.Add(ParseVarDecl());
            else
            {
                var expr = ParseExpression();
                Consume(TokenType.Semicolon, "Expected ';' after expression");

                if (Check(TokenType.RBrace))
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
            throw ThrowableFatalError($"Expected expression, found '{token.Lexeme}'", token);

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
        throw new NotImplementedException();
    }

    private IExprAST ParseIdentifier()
    {
        throw new NotImplementedException();
    }

    private IExprAST ParseGroupingOrTuple()
    {
        throw new NotImplementedException();
    }

    private IExprAST ParseListLit()
    {
        throw new NotImplementedException();
    }

    private IExprAST ParseArrLit()
    {
        throw new NotImplementedException();
    }

    private IExprAST ParseIfExpr()
    {
        throw new NotImplementedException();
    }

    private IExprAST ParseSwitchExpr()
    {
        throw new NotImplementedException();
    }

    private IExprAST ParseLambda()
    {
        throw new NotImplementedException();
    }

    private IExprAST ParseUnary()
    {
        throw new NotImplementedException();
    }

    private IExprAST ParseBinary(IExprAST left)
    {
        throw new NotImplementedException();
    }

    private IExprAST ParseTernary(IExprAST left)
    {
        throw new NotImplementedException();
    }


    private IExprAST ParseCall(IExprAST left)
    {
        throw new NotImplementedException();
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
        
        List<ITypeAST> types = [ParseType()];
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

        List<ITypeAST> types = [ParseType()];

        if (Match(TokenType.RPar)) return types[0];

        do types.Add(ParseType());
        while (Match(TokenType.Comma));

        Consume(TokenType.RPar, "Expected ')'");

        return new TupleType(types);
    }
}