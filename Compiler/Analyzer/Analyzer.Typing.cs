namespace Cera.Compiler.Analyzer;

using Cera.Compiler.Parser;

public partial class Analyzer
{
    // --- Hindley-Milner ---

    public record TypeVar(int Id) : ITypeAST;

    private int typeVarCounter = 0;
    private readonly Dictionary<int, ITypeAST> substitutions = [];

    private TypeVar GenerateTypeVariable() { return new TypeVar(typeVarCounter++); }

    private void Unify(ITypeAST expected, ITypeAST actual, Token contextToken)
    {
        expected = ApplySubstitutions(expected);
        actual = ApplySubstitutions(actual);

        if (expected is TypeVar tvE && actual is TypeVar tvA && tvE.Id == tvA.Id) return;
        if (expected is TypeVar tvExpected) { BindTypeVar(tvExpected.Id, actual, contextToken); return; }
        if (actual is TypeVar tvActual) { BindTypeVar(tvActual.Id, expected, contextToken); return; }

        switch (expected)
        {
            case BaseType bE when actual is BaseType bA:
                if (bE.TypeName.Lexeme != bA.TypeName.Lexeme)
                    FatalError($"Type mismatch: expected '{bE.TypeName.Lexeme}' got '{bA.TypeName.Lexeme}'", contextToken);
                break;
            case FuncType fE when actual is FuncType fA:
                Unify(fE.ParameterType, fA.ParameterType, contextToken);
                Unify(fE.ReturnType, fA.ReturnType, contextToken);
                break;
            case ListType lE when actual is ListType lA:
                Unify(lE.InnerType, lA.InnerType, contextToken);
                break;
            case ArrType aE when actual is ArrType aA:
                Unify(aE.InnerType, aA.InnerType, contextToken);
                break;
            case TupleType tE when actual is TupleType tA:
                if (tE.Types.Count != tA.Types.Count)
                    FatalError($"Tuple arity mismatch: expected {tE.Types.Count} elements, got {tA.Types.Count}", contextToken);
                for (int i = 0; i < tE.Types.Count; i++)
                    Unify(tE.Types[i], tA.Types[i], contextToken);
                break;
            case GenericType gE when actual is GenericType gA:
                if (gE.BaseName.Lexeme != gA.BaseName.Lexeme)
                    FatalError($"Type mismatch: expected '{gE.BaseName.Lexeme}', got '{gA.BaseName.Lexeme}'", contextToken);
                if (gE.TypeArguments.Count != gA.TypeArguments.Count)
                    FatalError($"Generic arity mismatch for '{gE.BaseName.Lexeme}'", contextToken);
                for (int i = 0; i < gE.TypeArguments.Count; i++)
                    Unify(gE.TypeArguments[i], gA.TypeArguments[i], contextToken);
                break;
            default:
                FatalError($"Cannot unify type '{Diagnostics.TypeString(expected)}' with '{Diagnostics.TypeString(actual)}'", contextToken);
                break;
        }
    }

    private void BindTypeVar(int id, ITypeAST type, Token contextToken)
    {
        if (OccursIn(id, type))
            FatalError($"Recursive type detected. Can't bind T_{id} to a type containing itself.", contextToken);
        substitutions[id] = type;
    }

    private bool OccursIn(int id, ITypeAST type)
    {
        type = ApplySubstitutions(type);

        return type switch
        {
            TypeVar tv => tv.Id == id,
            FuncType f => OccursIn(id, f.ParameterType) || OccursIn(id, f.ReturnType),
            ListType l => OccursIn(id, l.InnerType),
            ArrType a => OccursIn(id, a.InnerType),
            TupleType t => t.Types.Any(inner => OccursIn(id, inner)),
            GenericType g => g.TypeArguments.Any(inner => OccursIn(id, inner)),
            _ => false
        };
    }

    private ITypeAST ApplySubstitutions(ITypeAST type)
    {
        return type switch
        {
            TypeVar tv => substitutions.TryGetValue(tv.Id, out var resolvedType) ?
                ApplySubstitutions(resolvedType) : tv,
            FuncType f => new FuncType(ApplySubstitutions(f.ParameterType), ApplySubstitutions(f.ReturnType)),
            ListType l => new ListType(ApplySubstitutions(l.InnerType)),
            ArrType a => new ArrType(ApplySubstitutions(a.InnerType)),
            TupleType t => new TupleType([.. t.Types.Select(ApplySubstitutions)]),
            GenericType g => new GenericType(g.BaseName, [.. g.TypeArguments.Select(ApplySubstitutions)]),

            _ => type // base type unchanged
        };
    }

    private ITypeAST Instantiate(ITypeAST type, List<Token>? genericParams)
    {
        if (genericParams == null || genericParams.Count == 0) return type;

        Dictionary<string, ITypeAST> subs = [];
        foreach (var genToken in genericParams) // maps gens e.g. g, v to vars
        {
            subs[genToken.Lexeme] = GenerateTypeVariable();
        }

        return Replace(type, subs);
    }

    // --- Other typing tools ---

    private ITypeAST Replace(ITypeAST currentType, Dictionary<string, ITypeAST> mapping)
    {
        return currentType switch
        {
            BaseType b => mapping.TryGetValue(b.TypeName.Lexeme, out var freshVar)
                ? freshVar : b,
            FuncType f => new FuncType(Replace(f.ParameterType, mapping), Replace(f.ReturnType, mapping)),
            ListType l => new ListType(Replace(l.InnerType, mapping)),
            ArrType a => new ArrType(Replace(a.InnerType, mapping)),
            TupleType t => new TupleType([.. t.Types.Select(t => Replace(t, mapping))]),
            GenericType g => new GenericType(g.BaseName, [.. g.TypeArguments.Select(t => Replace(t, mapping))]),
            TypeVar tv => tv, // should not occur
            _ => currentType
        };
    }

    private HashSet<string> GetCurrentGenericScope()
    {
        HashSet<string> validGenerics = [];
        Environment? env = currentEnv;

        while (env != null)
        {
            foreach (var symbol in env.GetLocalSymbols())
            {
                if (symbol is GenericParamSymbol genSym)
                {
                    validGenerics.Add(genSym.DeclToken.Lexeme);
                }
            }
            env = env.Parent;
        }

        return validGenerics;
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

    private ITypeAST EnsureAndReturnBase(ITypeAST actual, string expectedIntrKey, Token op)
    {
        Unify(new BaseType(intrT[expectedIntrKey]), actual, op);
        return actual;
    }

    private ITypeAST EnsureNumericRelational(ITypeAST type, Token op)
    {
        EnsureNumeric(type, op);
        return new BaseType(intrT["bool"]);
    }

    private ITypeAST EnsureNumeric(ITypeAST type, Token op)
    {
        type = ApplySubstitutions(type);
        if (type is BaseType b && (b.TypeName.Lexeme == "int" || b.TypeName.Lexeme == "float"))
            return type;

        if (type is TypeVar tv)
        {
            BindTypeVar(tv.Id, new BaseType(intrT["int"]), op);
            return new BaseType(intrT["int"]);
        }

        return FatalErrorReturn($"Operator '{op.Lexeme}' requires numeric operands (int or float), got '{Diagnostics.TypeString(type)}'", op);
    }
}