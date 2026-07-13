using System.Diagnostics.CodeAnalysis;
using Cera.Compiler.Logging;
using Cera.Compiler.Parser;
using Cera.Compiler.Lexer;

namespace Cera.Compiler.Analyzer;

public partial class Analyzer(ProgramNode root, Diagnostics diag)
{
    private readonly Environment globalEnv = new();
    public Environment? currentEnv = Environment.None();

    public Environment Analyze()
    {
        currentEnv = globalEnv;

        diag.DetailLog("Analyzing AST");

        /// --- S1, type/function population ---
        InitializeIntrinsics();
        foreach (var t in root.Types) RegisterType(t);
        foreach (var f in root.Functions) RegisterFunction(f);

        diag.EndSection(Diagnostics.TimerScope.SubTask, "Registered base types and functions");

        // --- S2, traversal/type checking
        foreach (var f in root.Functions) AnalyzeFunction(f);

        return globalEnv;
    }

    private void RegisterType(TypeDeclNode type)
    {
        string tName = type.Identifier.Lexeme;

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

        if (globalEnv.Resolve(fName) != null) FatalError($"Function '{fName}' is already defined", func.Identifier);

        List<Token> gens = func.GenericTypeParams?.Identifiers ?? [];
        HashSet<string> seenGen = [];

        foreach (var gt in gens)
            if (!seenGen.Add(gt.Lexeme)) FatalError($"Duplicate generic parameter '{gt.Lexeme}' in function '{fName}'", gt);

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
            if (gens.Count > 0) FatalError("The program entry point cannot have generic type parameters.", func.Identifier); 
            ITypeAST expectedEntryType = new FuncType(
                new ArrType(new ListType(new BaseType(intrT["char"]))),
                new BaseType(intrT["int"])
            );
            Unify(expectedEntryType, fullSignature, func.Identifier);
        }

        globalEnv.Define(fName, new FuncSymbol(func.Identifier, fullSignature, func.Parameters.Count, gens));
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