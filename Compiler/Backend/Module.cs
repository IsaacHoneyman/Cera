namespace Cera.Compiler.Backend;

public class CompiledFunction(string name, int arity, Chunk body, int index)
{
    public string Name { get; } = name;
    public int Arity { get; } = arity;
    public Chunk Body { get; } = body;
    public int Index { get; } = index;
}

public class Module(string name)
{
    public string ModuleName { get; } = name;
    public List<CompiledFunction> Functions { get; private set; } = [];
    public CompiledFunction? EntryPoint => Functions.FirstOrDefault(f => f.Name == "entry");
    public void DefineFunction(CompiledFunction function) { Functions.Add(function); }

    public List<CompiledFunction> GetSortedFunctions() { return [.. Functions.OrderBy(f => f.Index)]; }

}