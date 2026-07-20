using Cera.Compiler.Parser;
using Cera.Compiler.Logging;
using System.Diagnostics.CodeAnalysis;
using Cera.Compiler.Lexer;

namespace Cera.Compiler.Backend;

public partial class Emitter(
ProgramNode root, Analyzer.Environment env, Dictionary<INodeAST, string> res, Diagnostics diag)
{
    private readonly Module module = new("CeraModule");

    private readonly Dictionary<string, int> globalFunctionIndices = [];
    private int nextFunctionIndex = 0;

    private readonly Dictionary<string, byte> constructorTags = new()
    {
        // 0x00 - 0x0F reserved for core language structures
        { "Nil", 0x00 },   // Used under the hood for []
        { "Cons", 0x01 },  // Used under the hood for ::
        { "None", 0x02 },  // Option intrinsic
        { "Some", 0x03 },
        { "Error", 0x04 }, // Result intrinsic
        { "Ok", 0x05 }
    };

    private byte nextConstructorTag = 0x10;

    private class FuncState(Diagnostics diag)
    {
        public FuncState? Enclosing { get; init; }
        public Chunk Chunk { get; } = new(diag);
        public List<string> Locals { get; } = [];

        // Tracks what we capture: Name, IsLocal (true if from parent's locals, false if from parent's upvalues), Index
        public List<(string Name, bool IsLocal, int Index)> Upvalues { get; } = [];
    }

    private FuncState state = new(diag);
    private Chunk CurrentChunk => state.Chunk;
    private List<string> Locals => state.Locals;

    public Module Compile()
    {
        diag.DetailLog("Creating Bytecode");

        foreach (var func in root.Functions)
        {
            string fName = res.TryGetValue(func, out string? r) ? r : func.Identifier.Lexeme;
            globalFunctionIndices[fName] = nextFunctionIndex++;
        }
        foreach (var typeDecl in root.Types)
        {
            foreach (var constructor in typeDecl.Constructors) 
            {
                string cName = constructor.ConstructorName.Lexeme;
                
                if (!constructorTags.ContainsKey(cName))
                {
                    if (nextConstructorTag == byte.MaxValue)
                        FatalError("Too many ADT constructors across the module. Limit is 255", typeDecl.Identifier);
                        
                    constructorTags[cName] = nextConstructorTag++;
                }
            }
        }        
        
        foreach (var func in root.Functions) CompileFunction(func);

        if (module.EntryPoint == null)
            diag.LogWarning("No 'entry' function found, This module can only be imported as a library");

        return module;
    }

    private void CompileFunction(FuncDeclNode func)
    {
        state = new FuncState(diag) { Enclosing = null };
        Locals.Add("<closure_reserved>");

        foreach (var param in func.Parameters)
            Locals.Add(param.Identifier.Lexeme);

        EmitExpression(func.Body, true);
        CurrentChunk.WriteByte(OpCode.RETURN, func.Identifier.Line);

        string fName = res.TryGetValue(func, out string? r) ? r : func.Identifier.Lexeme;
        int reservedIndex = globalFunctionIndices[fName];
        
        module.DefineFunction(new CompiledFunction(func.Identifier.Lexeme, func.Parameters.Count, CurrentChunk, reservedIndex));
    }

    private int ResolveUpvalue(FuncState currentState, string name)
    {
        if (currentState.Enclosing == null) return -1; // Hit the global scope, not found

        int localIdx = currentState.Enclosing.Locals.LastIndexOf(name);
        if (localIdx != -1)
        {
            return AddUpvalue(currentState, name, true, localIdx);
        }

        int upvalueIdx = ResolveUpvalue(currentState.Enclosing, name);
        if (upvalueIdx != -1)
        {
            return AddUpvalue(currentState, name, false, upvalueIdx);
        }

        return -1;
    }

    private int AddUpvalue(FuncState currentState, string name, bool isLocal, int index)
    {
        for (int i = 0; i < currentState.Upvalues.Count; i++)
        {
            if (currentState.Upvalues[i].Name == name) return i;
        }

        currentState.Upvalues.Add((name, isLocal, index));
        return currentState.Upvalues.Count - 1;
    }
    
    private int GetGlobalFunctionIndex(string fName, Token errorToken)
    {
        if (globalFunctionIndices.TryGetValue(fName, out int index))
            return index;
            
        FatalError($"Global function '{fName}' has no assigned index", errorToken);
        return -1;
    }

    private byte GetConstructorTagIndex(Token identifier)
    {
        if (constructorTags.TryGetValue(identifier.Lexeme, out byte tagId)) return tagId;
        throw ThrowableFatalError($"Emitter Error: Unknown ADT constructor '{identifier.Lexeme}' encountered during emission", identifier);
    }

    [DoesNotReturn]
    private void FatalError(string message, Token? token)
    {
        throw ThrowableFatalError(message, token);
    }
    
    private EmitterException ThrowableFatalError(string message, Token? token)
    {
        EmitterException e = new(message, token ?? Token.None());
        diag.LogError(e.Message);
        return e;
    }
}