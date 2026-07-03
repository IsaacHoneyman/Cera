using Cera.Compiler.Parser;
using Cera.Compiler.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Cera.Compiler.Backend;

public partial class Emitter(ProgramNode root, Diagnostics diag)
{
    private readonly Module module = new("CeraModule");

    private Chunk currentChunk = null!;
    private List<string> locals = [];

    public Module Compile()
    {
        diag.DetailLog("Creating Bytecode");

        foreach (var func in root.Functions) CompileFunction(func);

        if (module.EntryPoint == null)
            diag.LogWarning("No 'entry' function found, This module can only be imported as a library");

        return module;
    }

    private void CompileFunction(FuncDeclNode func)
    {
        currentChunk = new Chunk();
        locals = [];

        foreach (var param in func.Parameters) 
            locals.Add(param.Identifier.Lexeme);

        EmitExpression(func.Body);
        currentChunk.WriteByte(OpCode.RETURN, func.Identifier.Line);
        module.DefineFunction(new CompiledFunction(func.Identifier.Lexeme, func.Parameters.Count, currentChunk));
    }

    [DoesNotReturn]
    private void FatalError(string message)
    {
        throw new EmitterException(message);
    }
}