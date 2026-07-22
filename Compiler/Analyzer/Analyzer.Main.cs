using System.Diagnostics.CodeAnalysis;
using Cera.Compiler.Logging;
using Cera.Compiler.Parser;
using Cera.Compiler.Lexer;

namespace Cera.Compiler.Analyzer;

public partial class Analyzer(ProgramNode root, Diagnostics diag)
{
    private readonly Environment globalEnv = new();
    public Environment? currentEnv = Environment.None();

    public readonly Dictionary<INodeAST, string> resolvedNames = [];

    public (Environment, Dictionary<INodeAST, string>) Analyze()
    {
        currentEnv = globalEnv;

        diag.DetailLog("Analyzing AST");

        /// --- S1, type/function population ---
        InitializeIntrinsics();
        foreach (var t in root.Types) RegisterType(t);
        foreach (var f in root.Functions) RegisterFunction(f);
        foreach (var v in root.TopVars) RegisterTopVar(v);

        diag.EndSection(Diagnostics.TimerScope.SubTask, "Registered base types and functions");

        // --- S2, traversal/type checking
        foreach (var f in root.Functions) AnalyzeFunction(f);

        return (globalEnv, resolvedNames);
    }

    private void RegisterTopVar(TopVarDeclNode varDecl)
    {
        string vName = varDecl.Identifier.Lexeme;

        if (varDecl.IsHidden) 
            vName = $"_hidden_{varDecl.Identifier.File ?? "unknown"}_{vName}";
            
        resolvedNames[varDecl] = vName; 

        if (globalEnv.Resolve(vName) != null) 
            FatalError($"Global variable '{vName}' is already defined", varDecl.Identifier);

        ITypeAST initType = AnalyzeExpression(varDecl.Initializer);

        if (varDecl.DeclaredType != null)
        {
            ValidateTypeExists(varDecl.DeclaredType, []);
            Unify(varDecl.DeclaredType, initType, varDecl.Identifier);
            initType = varDecl.DeclaredType;
        }

        globalEnv.Define(vName, new VarSymbol(varDecl.Identifier, initType));
    }

    private void RegisterType(TypeDeclNode type)
    {
        string tName = type.Identifier.Lexeme;
        
        if (type.IsHidden) 
            tName = $"_hidden_{type.Identifier.File ?? "unknown"}_{tName}";
            
        resolvedNames[type] = tName;

        if (globalEnv.Resolve(tName) != null) FatalError($"Type '{tName}' is already defined", type.Identifier);

        List<Token> gens = type.GenericTypeParams?.Identifiers ?? [];
        HashSet<string> seenGen = [];

        foreach (var gt in gens)
            if (!seenGen.Add(gt.Lexeme)) FatalError($"Duplicate generic parameter '{gt.Lexeme}' in type {tName}", gt);

        ITypeAST selfType = gens.Count > 0
            ? new GenericType(type.Identifier, gens.Select(g => (ITypeAST)new BaseType(g)).ToList())
            : new BaseType(type.Identifier);

        globalEnv.Define(tName, new TypeSymbol(type.Identifier, selfType, gens));

        foreach (var con in type.Constructors)
        {
            string cName = con.ConstructorName.Lexeme;
            
            if (type.IsHidden)
                cName = $"_hidden_{con.ConstructorName.File ?? "unknown"}_{cName}";

            if (globalEnv.Resolve(cName) != null)
            {
                FatalError($"Constructor '{cName}' is already defined", con.ConstructorName);
            }

            globalEnv.Define(cName, new ConstructorSymbol(con.ConstructorName, selfType, con.PayloadType));
        }
    }

    private void RegisterFunction(FuncDeclNode func)
    {
        string fName = func.Identifier.Lexeme;

        if (fName == "entry")
        {
            if (func.IsHidden)
                FatalError("The program entry point cannot be marked 'hidden'", func.Identifier);
            if (func.IsInline)
                FatalError("The program entry point cannot be marked 'inline'", func.Identifier);
            if (func.GenericTypeParams?.Identifiers.Count > 0) 
                FatalError("The program entry point cannot have generic type parameters", func.Identifier); 
        }

        if (func.IsHidden) fName = $"_hidden_{func.Identifier.File ?? "unknown"}_{fName}";
        resolvedNames[func] = fName;

        if (globalEnv.Resolve(fName) != null) FatalError($"Function '{fName}' is already defined", func.Identifier);

        List<Token> gens = func.GenericTypeParams?.Identifiers ?? [];
        HashSet<string> seenGen = [];

        foreach (var gt in gens)
            if (!seenGen.Add(gt.Lexeme)) FatalError($"Duplicate generic parameter '{gt.Lexeme}' in function '{fName}'", gt);

        if (!func.IsHidden)
        {
            ValidatePublicVisibility(func.ReturnType, func.Identifier);
            foreach (var param in func.Parameters)
            {
                ValidatePublicVisibility(param.DeclaredType, param.Identifier);
            }
        }

        ValidateTypeExists(func.ReturnType, seenGen);

        List<ITypeAST> paramTypes = [];
        foreach (var param in func.Parameters)
        {
            ValidateTypeExists(param.DeclaredType, seenGen);
            paramTypes.Add(param.DeclaredType);
        }
        ITypeAST fullSignature;
        if (paramTypes.Count == 0) fullSignature = new FuncType(new BaseType(intrT["unit"]), func.ReturnType);
        else if (paramTypes.Count == 1) fullSignature = new FuncType(paramTypes[0], func.ReturnType);
        else fullSignature = new FuncType(new TupleType(paramTypes), func.ReturnType);

        if (fName == "entry")
        {
            ITypeAST expectedEntryType = new FuncType(
                new ArrType(new ListType(new BaseType(intrT["char"]))),
                new BaseType(intrT["int"])
            );
            Unify(expectedEntryType, fullSignature, func.Identifier);
        }

        globalEnv.Define(fName, new FuncSymbol(func.Identifier, fullSignature, func.Parameters.Count, gens));
    }

    private void ValidatePublicVisibility(ITypeAST typeNode, Token errorToken)
    {
        switch (typeNode)
        {
            case BaseType b:
                string rawName = b.TypeName.Lexeme;
                string mangledName = $"_hidden_{b.TypeName.File ?? "unknown"}_{rawName}";

                if (globalEnv.Resolve(mangledName) is TypeSymbol)
                {
                    FatalError($"Cannot expose hidden type '{rawName}' in a non-hidden function signature.", errorToken);
                }
                break;

            case ListType l:
                ValidatePublicVisibility(l.InnerType, errorToken);
                break;

            case ArrType a:
                ValidatePublicVisibility(a.InnerType, errorToken);
                break;

            case FuncType f:
                ValidatePublicVisibility(f.ParameterType, errorToken);
                ValidatePublicVisibility(f.ReturnType, errorToken);
                break;

            case TupleType t:
                foreach (var inner in t.Types)
                    ValidatePublicVisibility(inner, errorToken);
                break;

            case GenericType gt:
                string gBaseName = gt.BaseName.Lexeme;
                string gMangledName = $"_hidden_{gt.BaseName.File ?? "unknown"}_{gBaseName}";

                if (globalEnv.Resolve(gMangledName) is TypeSymbol)
                {
                    FatalError($"Cannot expose hidden generic type '{gBaseName}' in a non-hidden function signature.", errorToken);
                }

                foreach (var arg in gt.TypeArguments)
                    ValidatePublicVisibility(arg, errorToken);
                break;
        }
    }

    [DoesNotReturn]
    private void FatalError(string message, Token token)
    {
        AnalyzerException e = new(message, token);
        diag.LogError(e.Message);
        throw e;
    }

    [DoesNotReturn]
    private ITypeAST FatalErrorReturn(string message, Token token)
    {
        FatalError(message, token);
        return null!;
    }
}