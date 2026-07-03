namespace Cera.Compiler.Backend;

public class CompiledFunction(string name, int arity, Chunk body)
{
    public string Name { get; } = name;
    public int Arity { get; } = arity;
    public Chunk Body { get; } = body;
}

public class Module(string name)
{
    public string ModuleName { get; } = name;
    public Dictionary<string, CompiledFunction> Functions { get; private set; } = [];

    public CompiledFunction? EntryPoint => Functions.TryGetValue("entry", out var entry) ? entry : null;

    public void DefineFunction(CompiledFunction function) { Functions[function.Name] = function; }
}