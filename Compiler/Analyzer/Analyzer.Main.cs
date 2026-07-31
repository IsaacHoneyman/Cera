// In Analyzer.Main.cs
using System.Diagnostics.CodeAnalysis;
using Cera.Compiler.Logging;
using Cera.Compiler.Parser;
using Cera.Compiler.Lexer;

namespace Cera.Compiler.Analyzer;

public partial class Analyzer(Dictionary<string, FileAST> parsedFiles, Diagnostics diag)
{
    private readonly Environment globalEnv = new(); // intrinsics
    private readonly Dictionary<string, Environment> exportEnvs = [];
    private readonly Dictionary<string, Environment> localEnvs = [];

    public Environment? currentEnv = Environment.None();
    public readonly Dictionary<INodeAST, string> resolvedNames = [];

    public (Dictionary<string, Environment>, Dictionary<INodeAST, string>) Analyze()
    {
        diag.DetailLog("Analyzing AST DAG");

        InitializeIntrinsics();

        HashSet<string> visited = [];
        HashSet<string> visiting = [];

        foreach (var fileAst in parsedFiles.Values)
            if (!visited.Contains(fileAst.FilePath))
                AnalyzeFileRecursive(fileAst, visited, visiting);


        return (localEnvs, resolvedNames);
    }

    private void AnalyzeFileRecursive(FileAST fileAst, HashSet<string> visited, HashSet<string> visiting)
    {
        string path = fileAst.FilePath;
        if (visiting.Contains(path)) FatalError("Cyclic import dependency detected", Token.None());
        if (visited.Contains(path)) return;

        visiting.Add(path);

        Environment exportEnv = new(globalEnv);
        Environment localEnv = new(exportEnv);

        exportEnvs[path] = exportEnv;
        localEnvs[path] = localEnv;

        foreach (var import in fileAst.Imports)
        {
            string resolved = ResolveImportPath(path, import.PathLiteral.Lexeme.Trim('"'));

            if (!parsedFiles.TryGetValue(resolved, out FileAST? depAst))
                FatalError($"Could not resolve import '{import.PathLiteral.Lexeme}'", import.PathLiteral);

            AnalyzeFileRecursive(depAst, visited, visiting);

            Environment depExport = exportEnvs[resolved];

            localEnv.MergeFrom(depExport);

            if (!import.IsHidden)
            {
                exportEnv.MergeFrom(depExport); // export if public
            }
        }

        currentEnv = localEnv;

        foreach (var t in fileAst.Types) RegisterType(t, exportEnv, localEnv);
        foreach (var f in fileAst.Functions) RegisterFunction(f, exportEnv, localEnv);
        foreach (var e in fileAst.ExternFunctions) RegisterExtern(e, exportEnv, localEnv);
        foreach (var v in fileAst.TopVariables) RegisterTopVar(v, exportEnv, localEnv);

        foreach (var f in fileAst.Functions) AnalyzeFunction(f);

        visiting.Remove(path);
        visited.Add(path);
    }

    private string ResolveImportPath(string currentFilePath, string importName)
    {
        string currentDir = Path.GetDirectoryName(currentFilePath) ?? "";
        string localPath = Path.GetFullPath(Path.Combine(currentDir, importName));
        if (parsedFiles.ContainsKey(localPath)) return localPath;

        string? globalLibPath = System.Environment.GetEnvironmentVariable("CERA_LIB_PATH");
        if (!string.IsNullOrWhiteSpace(globalLibPath))
        {
            string globalPath = Path.GetFullPath(Path.Combine(globalLibPath, importName));
            if (parsedFiles.ContainsKey(globalPath)) return globalPath;
        }

        return localPath;
    }

    private void RegisterExtern(ExternDeclNode ext, Environment exportEnv, Environment localEnv)
    {
        string fName = ext.Identifier.Lexeme;

        if (ext.IsHidden) fName = $"_hidden_{ext.Identifier.File ?? "unknown"}_{fName}";
        resolvedNames[ext] = fName;

        if (currentEnv!.Resolve(fName) != null) FatalError($"External function '{fName}' is already defined", ext.Identifier);

        if (!ext.IsHidden)
        {
            ValidatePublicVisibility(ext.ReturnType, ext.Identifier);
            foreach (var param in ext.Parameters) ValidatePublicVisibility(param.DeclaredType, param.Identifier);
        }

        HashSet<string> emptyGens = [];
        ValidateTypeExists(ext.ReturnType, emptyGens);

        List<ITypeAST> paramTypes = [];
        foreach (var param in ext.Parameters)
        {
            ValidateTypeExists(param.DeclaredType, emptyGens);
            paramTypes.Add(param.DeclaredType);

            if (param.Initializer != null)
            {
                ITypeAST initType = AnalyzeExpression(param.Initializer);
                Unify(param.DeclaredType, initType, param.Identifier);
            }
        }

        ITypeAST fullSignature;
        if (paramTypes.Count == 0) fullSignature = new FuncType(new BaseType(intrT["unit"]), ext.ReturnType);
        else if (paramTypes.Count == 1) fullSignature = new FuncType(paramTypes[0], ext.ReturnType);
        else fullSignature = new FuncType(new TupleType(paramTypes), ext.ReturnType);

        string libPath = ext.PathLiteral.Lexeme.Trim('"');
        var sym = new ExternSymbol(ext.Identifier, fullSignature, ext.Parameters.Count, ext.Parameters, libPath);

        if (ext.IsHidden) localEnv.Define(fName, sym);
        else exportEnv.Define(fName, sym);
    }

    private void RegisterTopVar(TopVarDeclNode varDecl, Environment exportEnv, Environment localEnv)
    {
        string vName = varDecl.Identifier.Lexeme;

        if (varDecl.IsHidden) vName = $"_hidden_{varDecl.Identifier.File ?? "unknown"}_{vName}";
        resolvedNames[varDecl] = vName;

        if (currentEnv!.Resolve(vName) != null)
            FatalError($"Global variable '{vName}' is already defined", varDecl.Identifier);

        ITypeAST initType = AnalyzeExpression(varDecl.Initializer);

        if (varDecl.DeclaredType != null)
        {
            ValidateTypeExists(varDecl.DeclaredType, []);
            Unify(varDecl.DeclaredType, initType, varDecl.Identifier);
            initType = varDecl.DeclaredType;
        }

        if (varDecl.IsHidden) localEnv.Define(vName, new VarSymbol(varDecl.Identifier, initType));
        else exportEnv.Define(vName, new VarSymbol(varDecl.Identifier, initType));
    }

    private void RegisterType(TypeDeclNode type, Environment exportEnv, Environment localEnv)
    {
        string tName = type.Identifier.Lexeme;
        if (type.IsHidden) tName = $"_hidden_{type.Identifier.File ?? "unknown"}_{tName}";

        resolvedNames[type] = tName;

        if (currentEnv!.Resolve(tName) != null) FatalError($"Type '{tName}' is already defined", type.Identifier);

        List<Token> gens = type.GenericTypeParams?.Identifiers ?? [];
        HashSet<string> seenGen = [];

        foreach (var gt in gens)
            if (!seenGen.Add(gt.Lexeme)) FatalError($"Duplicate generic parameter '{gt.Lexeme}' in type {tName}", gt);

        ITypeAST selfType = gens.Count > 0
            ? new GenericType(type.Identifier, gens.Select(g => (ITypeAST)new BaseType(g)).ToList())
            : new BaseType(type.Identifier);

        if (type.IsHidden) localEnv.Define(tName, new TypeSymbol(type.Identifier, selfType, gens));
        else exportEnv.Define(tName, new TypeSymbol(type.Identifier, selfType, gens));

        foreach (var con in type.Constructors)
        {
            string cName = con.ConstructorName.Lexeme;
            if (type.IsHidden) cName = $"_hidden_{con.ConstructorName.File ?? "unknown"}_{cName}";

            if (currentEnv!.Resolve(cName) != null)
            {
                FatalError($"Constructor '{cName}' is already defined", con.ConstructorName);
            }

            if (type.IsHidden) localEnv.Define(cName, new ConstructorSymbol(con.ConstructorName, selfType, con.PayloadType));
            else exportEnv.Define(cName, new ConstructorSymbol(con.ConstructorName, selfType, con.PayloadType));
        }
    }

    private void RegisterFunction(FuncDeclNode func, Environment exportEnv, Environment localEnv)
    {
        string fName = func.Identifier.Lexeme;

        if (fName == "entry")
        {
            if (func.IsHidden) FatalError("The program entry point cannot be marked 'hidden'", func.Identifier);
            if (func.IsInline) FatalError("The program entry point cannot be marked 'inline'", func.Identifier);
            if (func.GenericTypeParams?.Identifiers.Count > 0) FatalError("The program entry point cannot have generic type parameters", func.Identifier);
        }

        if (func.IsHidden) fName = $"_hidden_{func.Identifier.File ?? "unknown"}_{fName}";
        resolvedNames[func] = fName;

        if (currentEnv!.Resolve(fName) != null) FatalError($"Function '{fName}' is already defined", func.Identifier);

        List<Token> gens = func.GenericTypeParams?.Identifiers ?? [];
        HashSet<string> seenGen = [];

        foreach (var gt in gens)
            if (!seenGen.Add(gt.Lexeme)) FatalError($"Duplicate generic parameter '{gt.Lexeme}' in function '{fName}'", gt);

        if (!func.IsHidden)
        {
            ValidatePublicVisibility(func.ReturnType, func.Identifier);
            foreach (var param in func.Parameters) ValidatePublicVisibility(param.DeclaredType, param.Identifier);
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
            ITypeAST expectedEntryType = new FuncType(new ArrType(new ListType(new BaseType(intrT["char"]))), new BaseType(intrT["int"]));
            Unify(expectedEntryType, fullSignature, func.Identifier);
        }

        if (func.IsHidden)
            localEnv.Define(fName, new FuncSymbol(func.Identifier, fullSignature, func.Parameters.Count, gens, func.Parameters));
        else
            exportEnv.Define(fName, new FuncSymbol(func.Identifier, fullSignature, func.Parameters.Count, gens, func.Parameters));
    }

    private void ValidatePublicVisibility(ITypeAST typeNode, Token errorToken)
    {
        switch (typeNode)
        {
            case BaseType b:
                string rawName = b.TypeName.Lexeme;
                string mangledName = $"_hidden_{b.TypeName.File ?? "unknown"}_{rawName}";

                // Change: Check currentEnv instead of globalEnv
                if (currentEnv!.Resolve(mangledName) is TypeSymbol)
                    FatalError($"Cannot expose hidden type '{rawName}' in a non-hidden function signature.", errorToken);
                break;
            case ListType l: ValidatePublicVisibility(l.InnerType, errorToken); break;
            case ArrType a: ValidatePublicVisibility(a.InnerType, errorToken); break;
            case FuncType f:
                ValidatePublicVisibility(f.ParameterType, errorToken);
                ValidatePublicVisibility(f.ReturnType, errorToken);
                break;
            case TupleType t:
                foreach (var inner in t.Types) ValidatePublicVisibility(inner, errorToken);
                break;
            case GenericType gt:
                string gBaseName = gt.BaseName.Lexeme;
                string gMangledName = $"_hidden_{gt.BaseName.File ?? "unknown"}_{gBaseName}";
                if (currentEnv!.Resolve(gMangledName) is TypeSymbol)
                    FatalError($"Cannot expose hidden generic type '{gBaseName}' in a non-hidden function signature.", errorToken);
                foreach (var arg in gt.TypeArguments) ValidatePublicVisibility(arg, errorToken);
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