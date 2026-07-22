using Cera.Compiler.Parser;
using Cera.Compiler.Logging;
using System.Diagnostics.CodeAnalysis;
using Cera.Compiler.Lexer;

namespace Cera.Compiler.Backend;

public partial class Emitter(
    Dictionary<string, FileAST> parsedFiles, 
    Dictionary<string, Analyzer.Environment> localEnvs, 
    Dictionary<INodeAST, string> res, 
    Diagnostics diag)
{
    private readonly Module module = new("CeraModule");
    private Analyzer.Environment? currentEnv;

    private readonly Dictionary<string, int> globalFunctionIndices = [];
    private int nextFunctionIndex = 0;

    private readonly Dictionary<string, FuncDeclNode> inlineFunctions = [];
    private readonly HashSet<string> curInlining = [];

    private readonly Dictionary<string, IExprAST> globalVariables = [];

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
        public List<(string Name, bool IsLocal, int Index)> Upvalues { get; } = [];
    }

    private FuncState state = new(diag);
    private Chunk CurrentChunk => state.Chunk;
    private List<string> Locals => state.Locals;

    public Module Compile()
    {
        diag.DetailLog("Creating Bytecode");

        // 1. Flat Pass: Allocate all indices, inline functions, variables, and ADT tags
        foreach (var file in parsedFiles.Values)
        {
            foreach (var func in file.Functions)
            {
                string fName = res.TryGetValue(func, out string? r) ? r : func.Identifier.Lexeme;
                globalFunctionIndices[fName] = nextFunctionIndex++;

                if (func.IsInline) inlineFunctions[fName] = func;
            }

            foreach (var topVar in file.TopVariables)
            {
                string vName = res.TryGetValue(topVar, out string? r) ? r : topVar.Identifier.Lexeme;
                globalVariables[vName] = topVar.Initializer;
            }
            
            foreach (var typeDecl in file.Types)
            {
                foreach (var constructor in typeDecl.Constructors) 
                {
                    string cName = constructor.ConstructorName.Lexeme;
                    if (typeDecl.IsHidden) cName = $"_hidden_{constructor.ConstructorName.File ?? "unknown"}_{cName}";
                    
                    if (!constructorTags.ContainsKey(cName))
                    {
                        if (nextConstructorTag == byte.MaxValue)
                            FatalError("Too many ADT constructors across the module. Limit is 255", typeDecl.Identifier);
                            
                        constructorTags[cName] = nextConstructorTag++;
                    }
                }
            }
        }

        // 2. Flat Pass: Compile all functions within their specific local environments
        foreach (var file in parsedFiles.Values)
        {
            currentEnv = localEnvs[file.FilePath];
            foreach (var func in file.Functions) 
            {
                CompileFunction(func);
            }
        }

        if (module.EntryPoint == null)
            diag.LogWarning("No 'entry' function found. This module can only be imported as a library");

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
        if (currentState.Enclosing == null) return -1;

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
        string cName = identifier.Lexeme;
        string mangledName = $"_hidden_{identifier.File ?? "unknown"}_{cName}";
        
        if (constructorTags.TryGetValue(mangledName, out byte tagIdMangled)) return tagIdMangled;
        if (constructorTags.TryGetValue(cName, out byte tagId)) return tagId;
        
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