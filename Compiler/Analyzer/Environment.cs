namespace Cera.Compiler.Analyzer;

public class Environment(Environment? parent = null)
{
    private readonly Dictionary<string, Symbol> locals = [];

    private readonly Environment? parent = parent;
    public Environment? Parent => parent;

    public void Define(string name, Symbol symbol)
    {
        locals[name] = symbol;
    }

    public Symbol? Resolve(string name)
    {
        if (locals.TryGetValue(name, out var symbol)) return symbol;
        if (parent != null) return parent.Resolve(name);
        return null;
    }

    public IEnumerable<Symbol> GetLocalSymbols() { return locals.Values; }

    public static Environment None() { return new Environment(); }
    
}