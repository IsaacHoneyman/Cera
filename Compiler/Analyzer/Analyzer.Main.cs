using System.Diagnostics.CodeAnalysis;
using Cera.Compiler.Exceptions;
using Cera.Compiler.Parser;

namespace Cera.Compiler.Analyzer;

public partial class Analyzer(ProgramNode root, Diagnostics diag)
{
    private readonly Environment globalEnv = new();
    public Environment? currentEnv = Environment.None();

    public Environment Analyze()
    {
        currentEnv = globalEnv;

        /// --- S1, type/function population ---
        InitializeIntrinsics();
        foreach (var t in root.Types) RegisterType(t);
        foreach (var f in root.Functions) RegisterFunction(f);

        diag.EndSection(Diagnostics.TimerScope.SubTask, "Registered base types and functions");

        // --- S2, traversal/type checking
        foreach (var f in root.Functions) AnalyzeFunction(f);

        return globalEnv;
    }

    private void AnalyzeFunction(FuncDeclNode func)
    {
        currentEnv = new Environment(currentEnv);
        currentEnv = currentEnv.Parent;
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
        foreach (var param in func.Parameters)
            ValidateTypeExists(param.DeclaredType, seenGen);

        globalEnv.Define(fName, new FuncSymbol(func.Identifier, func.ReturnType, func.Parameters.Count, gens));
    }

    private void ValidateTypeExists(ITypeAST typeNode, HashSet<string> validGenerics)
    {
        switch (typeNode)
        {
            case BaseType b:
                string name = b.TypeName.Lexeme;
                if (!validGenerics.Contains(name) && globalEnv.Resolve(name) is not TypeSymbol)
                {
                    FatalError($"Semantic Error: Undefined type '{name}'", b.TypeName);
                }
                break;
            case ListType l:
                ValidateTypeExists(l.InnerType, validGenerics);
                break;
            case ArrType a:
                ValidateTypeExists(a.InnerType, validGenerics);
                break;
            case FuncType f:
                ValidateTypeExists(f.ParameterType, validGenerics);
                ValidateTypeExists(f.ReturnType, validGenerics);
                break;
            case TupleType t:
                foreach (var innerType in t.Types)
                    ValidateTypeExists(innerType, validGenerics);
                break;
            case GenericType gt:
                string baseName = gt.BaseName.Lexeme;
                if (globalEnv.Resolve(baseName) is not TypeSymbol) 
                    FatalError($"Undefined generic base type '{baseName}'", gt.BaseName);
                foreach (var typeArg in gt.TypeArguments)
                    ValidateTypeExists(typeArg, validGenerics);
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
}