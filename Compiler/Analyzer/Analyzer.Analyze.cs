using Cera.Compiler.Lexer;
using Cera.Compiler.Parser;
using Cera.Compiler.Logging;

namespace Cera.Compiler.Analyzer;

public partial class Analyzer
{
    private void AnalyzeFunction(FuncDeclNode func)
    {
        currentEnv = new(currentEnv);

        if (func.GenericTypeParams != null)
        {
            foreach (var gen in func.GenericTypeParams.Identifiers)
            {
                if (currentEnv.Resolve(gen.Lexeme) != null)
                    FatalError($"Duplicate generic parameter '{gen.Lexeme}' in function '{func.Identifier.Lexeme}'", gen);
                currentEnv.Define(gen.Lexeme, new GenericParamSymbol(gen));
            }
        }

        HashSet<string> paramNames = [];
        foreach (var param in func.Parameters)
        {
            var pName = param.Identifier.Lexeme;
            if (!paramNames.Add(pName))
                FatalError($"Duplicate parameter name '{pName}' in function '{func.Identifier.Lexeme}'", param.Identifier);
            ValidateTypeExists(param.DeclaredType, GetCurrentGenericScope());
            currentEnv.Define(pName, new VarSymbol(param.Identifier, param.DeclaredType));
        }

        ITypeAST actualBodyType = AnalyzeExpression(func.Body);
        ValidateTypeExists(func.ReturnType, GetCurrentGenericScope());
        Unify(func.ReturnType, actualBodyType, func.Identifier);
        currentEnv = currentEnv.Parent;
    }

    private ITypeAST AnalyzeExpression(IExprAST expr)
    {
        return expr switch
        {
            ExprBlock block => AnalyzeExpressionBlock(block),
            LiteralExpr lit => AnalyzeLiteral(lit),
            IdentifierExpr id => AnalyzeIdentifier(id),
            LambdaExpr lambda => AnalyzeLambda(lambda),
            CallExpr call => AnalyzeCall(call),
            BinaryExpr binOp => AnalyzeBinaryExpression(binOp),
            UnaryExpr unOp => AnalyzeUnaryExpression(unOp),
            TernaryExpr ternary => AnalyzeTernaryExpression(ternary),
            IfExpr ifExpr => AnalyzeIfExpression(ifExpr),
            SwitchExpr switchExpr => AnalyzeSwitchExpression(switchExpr),
            ConExpr conExpr => AnalyzeConstructor(conExpr),
            ListLitExpr listLit => AnalyzeListLiteral(listLit),
            ArrLitExpr arrLit => AnalyzeArrayLiteral(arrLit),
            TupleLitExpr tupleLit => AnalyzeTupleLiteral(tupleLit),
            _ => throw new NotImplementedException($"Analysis not implemented for '{expr.GetType().Name}'")
        };
    }

    private ITypeAST AnalyzeExpressionBlock(ExprBlock block)
    {
        currentEnv = new Environment(currentEnv);

        foreach (var stmt in block.Statements)
        {
            if (stmt is VarDeclStmt varDecl)
            {
                if (varDecl.Pattern is IdPattern idPat)
                {
                    // --- Branch 1: Simple Identifier (Preserves Lambda Recursion) ---
                    if (varDecl.Initializer is LambdaExpr lambda)
                    {
                        ITypeAST freshType = GenerateTypeVariable();
                        currentEnv.Define(idPat.Identifier.Lexeme, new VarSymbol(idPat.Identifier, freshType));
                    }

                    var initType = AnalyzeExpression(varDecl.Initializer);
                    if (varDecl.DeclaredType != null)
                    {
                        ValidateTypeExists(varDecl.DeclaredType, GetCurrentGenericScope());
                        Unify(varDecl.DeclaredType, initType, varDecl.Operator);
                        initType = varDecl.DeclaredType;
                    }

                    currentEnv.Define(idPat.Identifier.Lexeme, new VarSymbol(idPat.Identifier, initType));
                }
                else
                {
                    // --- Branch 2: Structural Destructuring (Tuple/Constructor Unpacking) ---
                    var initType = AnalyzeExpression(varDecl.Initializer);
                    if (varDecl.DeclaredType != null)
                    {
                        ValidateTypeExists(varDecl.DeclaredType, GetCurrentGenericScope());
                        Unify(varDecl.DeclaredType, initType, varDecl.Operator);
                        initType = varDecl.DeclaredType;
                    }

                    ITypeAST patternType = AnalyzePattern(varDecl.Pattern);
                    Unify(patternType, initType, varDecl.Operator);
                }
            }
            else if (stmt is ExprStmt exprStmt) AnalyzeExpression(exprStmt.Expression);
        }

        var blockType = AnalyzeExpression(block.ReturnExpression);
        currentEnv = currentEnv.Parent;
        return blockType;
    }

    private ITypeAST AnalyzeLiteral(LiteralExpr lit)
    {
        return lit.Value.Tag switch
        {
            TokenType.IntLiteral => new BaseType(intrT["int"]),
            TokenType.FloatLiteral => new BaseType(intrT["float"]),
            TokenType.True => new BaseType(intrT["bool"]),
            TokenType.False => new BaseType(intrT["bool"]),
            TokenType.CharLiteral => new BaseType(intrT["char"]),
            TokenType.StringLiteral => new ListType(new BaseType(intrT["char"])),
            TokenType.Unit => new BaseType(intrT["unit"]),
            _ => FatalErrorReturn($"Unknown literal type '{lit.Value.Lexeme}' : {lit.Value.Tag}", lit.Value),
        };
    }

    private ITypeAST AnalyzeIdentifier(IdentifierExpr id)
    {
        string name = id.Identifier.Lexeme;
        string mangledName = $"_hidden_{id.Identifier.File ?? "unknown"}_{name}";

        Symbol? sym = currentEnv?.Resolve(mangledName) ?? currentEnv?.Resolve(name);
        if (sym == null) FatalError($"Undefined identifier '{name}'", id.Identifier);

        resolvedNames[id] = currentEnv?.Resolve(mangledName) != null ? mangledName : name;

        return sym switch
        {
            VarSymbol vSym => vSym.Type ?? FatalErrorReturn($"Variable '{name}' lacks a resolved type", id.Identifier),
            FuncSymbol fSym => Instantiate(
                fSym.Type ?? FatalErrorReturn($"Function '{name}' lacks a resolved type signature", id.Identifier),
                fSym.GenericParams
            ),
            _ => FatalErrorReturn($"Symbol '{name}' cannot be evaluated as an expression", id.Identifier)
        };
    }

    private FuncType AnalyzeLambda(LambdaExpr lambda)
    {
        currentEnv = new Environment(currentEnv);

        List<ITypeAST> paramTypes = [];
        foreach (var param in lambda.Parameters)
        {
            ValidateTypeExists(param.DeclaredType, GetCurrentGenericScope());
            currentEnv.Define(param.Identifier.Lexeme, new VarSymbol(param.Identifier, param.DeclaredType));
            paramTypes.Add(param.DeclaredType);
        }

        ITypeAST bodyType = AnalyzeExpression(lambda.Body);

        ValidateTypeExists(lambda.ReturnType, GetCurrentGenericScope());
        Unify(lambda.ReturnType, bodyType, lambda.Parameters.FirstOrDefault()?.Identifier ?? intrT["unit"]);

        currentEnv = currentEnv.Parent;

        return new FuncType(
            paramTypes.Count == 1 ? paramTypes[0] : new TupleType(paramTypes),
            lambda.ReturnType
        );
    }

    private ITypeAST AnalyzeCall(CallExpr call)
    {
        ITypeAST calleeType = AnalyzeExpression(call.Callee);

        List<ITypeAST> argTypes = [];
        foreach (var arg in call.Arguments)
        {
            argTypes.Add(AnalyzeExpression(arg));
        }

        ITypeAST expectedReturnType = GenerateTypeVariable();
        ITypeAST expectedParamType;
        if (argTypes.Count == 0) expectedParamType = new BaseType(intrT["unit"]);
        else if (argTypes.Count == 1) expectedParamType = argTypes[0];
        else expectedParamType = new TupleType(argTypes);
        ITypeAST expectedSignature = new FuncType(expectedParamType, expectedReturnType);

        Unify(calleeType, expectedSignature, (call.Callee as IdentifierExpr)?.Identifier ?? intrT["unit"]);
        return ApplySubstitutions(expectedReturnType);
    }

    private ITypeAST AnalyzeUnaryExpression(UnaryExpr unary)
    {
        ITypeAST rightType = AnalyzeExpression(unary.Right);
        switch (unary.Operator.Tag)
        {
            case TokenType.Not: // !
                Unify(new BaseType(intrT["bool"]), rightType, unary.Operator);
                return rightType;
            case TokenType.BitNot: // ~
                Unify(new BaseType(intrT["int"]), rightType, unary.Operator);
                return rightType;
            case TokenType.Minus: // -
                return rightType;
            default:
                return FatalErrorReturn($"Unknown unary operator '{unary.Operator.Lexeme}'", unary.Operator);
        }
    }

    private ITypeAST AnalyzeBinaryExpression(BinaryExpr binary)
    {
        ITypeAST leftType = AnalyzeExpression(binary.Left);
        ITypeAST rightType = AnalyzeExpression(binary.Right);

        if (binary.Operator.Tag == TokenType.ColonColon) // special case T, T list
        {
            ITypeAST expectedRight = new ListType(leftType);
            Unify(expectedRight, rightType, binary.Operator);
            return rightType; // Evaluates to the list type
        }

        Unify(leftType, rightType, binary.Operator);

        return binary.Operator.Tag switch
        {
            TokenType.Plus or TokenType.Minus or TokenType.Star or
            TokenType.Slash or TokenType.Mod => EnsureNumeric(leftType, binary.Operator),
            TokenType.BitAnd or TokenType.Pipe or TokenType.BitXor or
            TokenType.LShift or TokenType.RShift =>
                EnsureAndReturnBase(leftType, "int", binary.Operator),
            TokenType.Lesser or
            TokenType.Greater or TokenType.LesserEqual or TokenType.GreaterEqual =>
                EnsureNumericRelational(leftType, binary.Operator),
            TokenType.EqualEqual or TokenType.NotEqual =>
                new BaseType(intrT["bool"]),
            TokenType.And or TokenType.Or =>
                EnsureAndReturnBase(leftType, "bool", binary.Operator),
            _ => FatalErrorReturn($"Unknown binary operator '{binary.Operator.Lexeme}'", binary.Operator)
        };
    }

    private ITypeAST AnalyzeTernaryExpression(TernaryExpr ternary)
    {
        ITypeAST condType = AnalyzeExpression(ternary.Condition);
        Unify(new BaseType(intrT["bool"]), condType, ternary.Operator);

        ITypeAST trueType = AnalyzeExpression(ternary.TrueBranch);
        ITypeAST falseType = AnalyzeExpression(ternary.FalseBranch);

        Unify(trueType, falseType, ternary.Operator);

        return trueType;
    }

    private ITypeAST AnalyzeIfExpression(IfExpr ifExpr)
    {
        ITypeAST condType = AnalyzeExpression(ifExpr.Condition);

        Unify(new BaseType(intrT["bool"]), condType, ifExpr.Operator);

        ITypeAST resultType = AnalyzeExpressionBlock(ifExpr.TrueBlock);

        foreach (var (Condition, Block) in ifExpr.ElseIfs)
        {
            ITypeAST elseIfCond = AnalyzeExpression(Condition);
            Unify(new BaseType(intrT["bool"]), elseIfCond, ifExpr.Operator);

            ITypeAST elseIfBlockType = AnalyzeExpressionBlock(Block);
            Unify(resultType, elseIfBlockType, ifExpr.Operator);
        }

        if (ifExpr.ElseBlock != null)
        {
            ITypeAST elseBlockType = AnalyzeExpressionBlock(ifExpr.ElseBlock);
            Unify(resultType, elseBlockType, ifExpr.Operator);
        }
        else
        {
            Unify(new BaseType(intrT["unit"]), resultType, ifExpr.Operator);
        }

        return resultType;
    }

    private ITypeAST AnalyzeSwitchExpression(SwitchExpr switchExpr)
    {
        ITypeAST targetType = AnalyzeExpression(switchExpr.TargetExpression);
        ITypeAST resultType = GenerateTypeVariable();

        foreach (var matchCase in switchExpr.Cases)
        {
            currentEnv = new Environment(currentEnv);
            ITypeAST patternType = AnalyzePattern(matchCase.Pattern);
            Unify(targetType, patternType, switchExpr.Operator);
            ITypeAST branchType = AnalyzeExpression(matchCase.ResultExpression);
            Unify(resultType, branchType, switchExpr.Operator);
            currentEnv = currentEnv.Parent;
        }

        return ApplySubstitutions(resultType);
    }

    private ITypeAST AnalyzePattern(IPatternAST pattern)
    {
        return pattern switch
        {
            IdPattern id => BindPatternVariable(id.Identifier),

            LiteralPattern lit => lit.Value.Tag switch
            {
                TokenType.IntLiteral => new BaseType(intrT["int"]),
                TokenType.FloatLiteral => new BaseType(intrT["float"]),
                TokenType.False => new BaseType(intrT["bool"]),
                TokenType.True => new BaseType(intrT["bool"]),
                TokenType.CharLiteral => new BaseType(intrT["char"]),
                TokenType.StringLiteral => new ListType(new BaseType(intrT["char"])),
                TokenType.WildCard => GenerateTypeVariable(),
                TokenType.Unit => new BaseType(intrT["unit"]),
                _ => FatalErrorReturn($"Unknown literal pattern '{lit.Value.Tag}'", lit.Value)
            },

            TuplePattern tup => new TupleType([.. tup.Patterns.Select(AnalyzePattern)]),

            ListPattern lst => AnalyzeListPattern(lst),

            ArrPattern arr => AnalyzeArrPattern(arr),

            ConsPattern cons => AnalyzeConsPattern(cons),

            ConPattern con => AnalyzeConstructorPattern(con),

            _ => throw new NotImplementedException($"Pattern analysis not implemented for '{pattern.GetType().Name}'")
        };
    }

    private ITypeAST BindPatternVariable(Token identifier)
    {
        ITypeAST freshVar = GenerateTypeVariable();
        currentEnv!.Define(identifier.Lexeme, new VarSymbol(identifier, freshVar));
        return freshVar;
    }

    private ArrType AnalyzeArrPattern(ArrPattern arr)
    {
        if (arr.Patterns.Count == 0) return new ArrType(GenerateTypeVariable());

        ITypeAST elementType = AnalyzePattern(arr.Patterns[0]);
        for (int i = 1; i < arr.Patterns.Count; i++)
        {
            Unify(elementType, AnalyzePattern(arr.Patterns[i]), arr.Operator);
        }
        return new ArrType(elementType);
    }

    private ListType AnalyzeListPattern(ListPattern lst)
    {
        if (lst.Patterns.Count == 0) return new ListType(GenerateTypeVariable());

        ITypeAST elementType = AnalyzePattern(lst.Patterns[0]);
        for (int i = 1; i < lst.Patterns.Count; i++)
        {
            Unify(elementType, AnalyzePattern(lst.Patterns[i]), lst.Operator);
        }
        return new ListType(elementType);
    }

    private ITypeAST AnalyzeConsPattern(ConsPattern cons)
    {
        ITypeAST headType = AnalyzePattern(cons.Head);
        ITypeAST tailType = AnalyzePattern(cons.Tail);

        Unify(new ListType(headType), tailType, cons.Operator);
        return tailType;
    }

    private ITypeAST AnalyzeConstructorPattern(ConPattern con)
    {
        string cName = con.ConstructorName.Lexeme;
        string mangledName = $"_hidden_{con.ConstructorName.File ?? "unknown"}_{cName}";
        
        Symbol? sym = currentEnv!.Resolve(mangledName) ?? currentEnv!.Resolve(cName);
        
        if (sym is not ConstructorSymbol cSym)
            return FatalErrorReturn($"Undefined constructor '{cName}'", con.ConstructorName);

        List<Token> adtGenerics = [];
        if (cSym.ParentType is GenericType gt && currentEnv!.Resolve(gt.BaseName.Lexeme) is TypeSymbol tSym)
            adtGenerics = tSym.GenericParams;

        Dictionary<string, ITypeAST> subs = [];
        foreach (var gen in adtGenerics) subs[gen.Lexeme] = GenerateTypeVariable();

        ITypeAST instantiatedParent = Replace(cSym.ParentType, subs);
        ITypeAST? instantiatedPayload = cSym.PayloadType != null ? Replace(cSym.PayloadType, subs) : null;

        if (instantiatedPayload != null)
        {
            ITypeAST actualPayloadType = con.PayloadPatterns.Count > 1
                ? new TupleType([.. con.PayloadPatterns.Select(AnalyzePattern)])
                : AnalyzePattern(con.PayloadPatterns[0]);

            Unify(instantiatedPayload, actualPayloadType, con.ConstructorName);
        }
        else if (con.PayloadPatterns.Count > 0)
        {
            FatalError($"Constructor '{cName}' does not take a payload, but the pattern provided one", con.ConstructorName);
        }

        return instantiatedParent;
    }

    private ITypeAST AnalyzeConstructor(ConExpr con)
    {
        string cName = con.ConstructorName.Lexeme;
        string mangledName = $"_hidden_{con.ConstructorName.File ?? "unknown"}_{cName}";
        Symbol? sym = currentEnv!.Resolve(mangledName) ?? currentEnv!.Resolve(cName);
        if (sym is not ConstructorSymbol cSym)
            return FatalErrorReturn($"Undefined constructor '{cName}'", con.ConstructorName);

        List<Token> gens = [];
        if (cSym.ParentType is GenericType gt && currentEnv!.Resolve(gt.BaseName.Lexeme) is TypeSymbol tSym)
            gens = tSym.GenericParams;

        Dictionary<string, ITypeAST> subs = [];
        foreach (var gen in gens) subs[gen.Lexeme] = GenerateTypeVariable();

        ITypeAST instantiatedParent = Replace(cSym.ParentType, subs);
        ITypeAST? instantiatedPayload = cSym.PayloadType != null ? Replace(cSym.PayloadType, subs) : null;

        if (instantiatedPayload != null)
        {
            if (con.Payloads.Count == 0)
                FatalError($"Constructor '{cName}' expects a payload of type '{Diagnostics.TypeString(instantiatedPayload)}'", con.ConstructorName);

            ITypeAST actualPayloadType = con.Payloads.Count > 1
                ? new TupleType([.. con.Payloads.Select(AnalyzeExpression)]) : AnalyzeExpression(con.Payloads[0]);

            Unify(instantiatedPayload, actualPayloadType, con.ConstructorName);
        }
        else if (con.Payloads.Count > 0)
            FatalError($"Constructor '{cName}' does not take any arguments", con.ConstructorName);

        return instantiatedParent;
    }

    private ListType AnalyzeListLiteral(ListLitExpr list)
    {
        if (list.Elements.Count == 0) return new ListType(GenerateTypeVariable());

        var elementType = AnalyzeExpression(list.Elements[0]);

        for (int i = 1; i < list.Elements.Count; i++)
        {
            ITypeAST nextElementType = AnalyzeExpression(list.Elements[i]);
            Unify(elementType, nextElementType, list.Operator);
        }

        return new ListType(elementType);
    }

    private ArrType AnalyzeArrayLiteral(ArrLitExpr arr)
    {
        if (arr.Elements.Count == 0)
            return new ArrType(GenerateTypeVariable());

        var elementType = AnalyzeExpression(arr.Elements[0]);

        for (int i = 1; i < arr.Elements.Count; i++)
        {
            ITypeAST nextElementType = AnalyzeExpression(arr.Elements[i]);
            Unify(elementType, nextElementType, arr.Operator);
        }

        return new ArrType(elementType);
    }

    private TupleType AnalyzeTupleLiteral(TupleLitExpr tup)
    {
        List<ITypeAST> elementTypes = [];
        foreach (var expr in tup.Elements)
        {
            elementTypes.Add(AnalyzeExpression(expr));
        }
        return new TupleType(elementTypes);
    }
}