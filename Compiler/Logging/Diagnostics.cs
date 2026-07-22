using System.Diagnostics;
using System.Text;
using Cera.Compiler.Parser;
using Cera.Compiler.Analyzer;
using Cera.Compiler.Lexer;
using System.Reflection;

namespace Cera.Compiler.Logging;

/// <summary> Feedback class for the compiler. </summary>
public class Diagnostics
{
    public enum TimerScope
    {
        Global = 0,
        Task = 1,
        SubTask = 2,
    };

    private readonly bool dumpToFile;
    private readonly bool detailedDiag;

    private readonly bool tokenDump;
    private readonly bool astDump;
    private readonly bool analyzerDump;
    private readonly bool emitterDump;

    private readonly string dumpFilePath;
    private readonly StringBuilder sb = new();

    private readonly Stopwatch[] sw = [new(), new(), new()]; // global, task, sub-task

    public Diagnostics(bool[] args)
    {
        dumpToFile = args[0];
        detailedDiag = args[1];

        tokenDump = args[2];
        astDump = args[3];
        analyzerDump = args[4];
        emitterDump = args[5];

        dumpFilePath = $"Out/Dump/Cera_Compiler_Dump_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
    }

    public void Close()
    {
        if (dumpToFile) File.AppendAllText(dumpFilePath, sb.ToString());
    }

    public void Log(string message, bool hide = false)
    {
        Console.ForegroundColor = ConsoleColor.White;
        if (!hide) Console.WriteLine(message);
        if (dumpToFile) sb.Append($"{message}\n");
    }

    public void DetailLog(string message, bool hide = false)
    {
        if (!detailedDiag) return;
        Log(message, hide);
    }

    public void LogWarning(string warning)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[Warning] " + warning);
        Console.ForegroundColor = ConsoleColor.White;
        if (dumpToFile) sb.Append($"[Warning] {warning}\n");
    }

    public void LogError(string error)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[Error] " + error);
        Console.ForegroundColor = ConsoleColor.White;
        if (dumpToFile) sb.Append($"[Error] {error}\n");
    }

    // --- Timers ---

    public void Open()
    {
        foreach (var s in sw) s.Start();
    }

    public void EndSection(TimerScope scope, string preMessage, string postMessage = "")
    {
        long ms = sw[(int)scope].ElapsedMilliseconds;
        sw[(int)scope].Restart();

        if (scope == TimerScope.Task) sw[(int)TimerScope.SubTask].Restart();

        string timeStr = ms < 1000 ? $"{ms}ms" : $"{Math.Round(ms / 1000.0, 2)}s";

        if (scope == TimerScope.Global) 
            Log($"{preMessage} In {timeStr}. {postMessage}");
        else
            DetailLog($"{preMessage} In {timeStr}. {postMessage}");
        
        if (scope == TimerScope.Task) DetailLog("");
    }

    // --- Helper ---

    public static string TypeString(ITypeAST type)
    {
        return type switch
        {
            BaseType t => t.TypeName.Lexeme,
            FuncType ft => $"{TypeString(ft.ParameterType)} -> {TypeString(ft.ReturnType)}",
            ListType lt => $"{TypeString(lt.InnerType)} list",
            ArrType at => $"{TypeString(at.InnerType)} arr",
            TupleType tt => $"({string.Join(" * ", tt.Types.Select(TypeString))})",
            GenericType gt => $"{gt.BaseName.Lexeme}<{string.Join(", ", gt.TypeArguments.Select(TypeString))}>",
            Analyzer.Analyzer.TypeVar tv => $"Type({tv.Id})",
            _ => $"Unimplemented Type - {type.GetType().Name}"
        };
    }

    public static Token? GetLeadToken(INodeAST node)
    {
        return node switch
        {
            LiteralExpr lit => lit.Value,
            IdentifierExpr id => id.Identifier,
            BinaryExpr bin => bin.Operator,
            UnaryExpr un => un.Operator,
            IfExpr ifExpr => ifExpr.Operator,
            SwitchExpr sw => sw.Operator,
            ListLitExpr list => list.Operator,
            ArrLitExpr arr => arr.Operator,
            TupleLitExpr tup => tup.Operator,
            ConExpr con => con.ConstructorName,
            
            // Dig down into the callee to find the function's name token
            CallExpr call => GetLeadToken(call.Callee), 
            
            // Grab the first parameter's token, if it has one
            LambdaExpr lam => lam.Parameters.FirstOrDefault()?.Identifier, 
            
            // Look at the return expression of the block
            ExprBlock block => GetLeadToken(block.ReturnExpression),
            
            // Patterns
            LiteralPattern lp => lp.Value,
            IdPattern ip => ip.Identifier,
            ConPattern cp => cp.ConstructorName,
            ConsPattern cp => cp.Operator,
            
            _ => null
        };
    }

    // --- Dumps ---

    public bool TryTokenDump(List<Token> tokens)
    {
        if (!tokenDump) return false;

        Log("", true);
        Log("=== Lexical Analyzer: Token Stream Dump ===", true);

        string lastFile = "\0";
        foreach (var t in tokens)
        {
            if (t.File != lastFile)
            {
                Log($"\n--- File: {t.File} ---", true);
                Log(string.Format("{0,-20} | {1,-20} | {2,-5} | {3,-5}", "Lexeme", "Token Tag", "Line", "Col"), true);
                Log(new string('-', 60), true);
                lastFile = t.File;
            }

            string safeLexeme = t.Lexeme.Replace("\n", "\\n").Replace("\r", "\\r");
            if (safeLexeme.Length > 18) safeLexeme = safeLexeme[..15] + "...";

            Log(string.Format("{0,-20} | {1,-20} | {2,-5} | {3,-5}",
                $"'{safeLexeme}'", t.Tag, t.Line, t.Column), true);
        }

        Log("=================================================", true);
        Log("", true);
        return true;
    }

    // --- AST Nonsense ---

    public bool TryASTDump(INodeAST root)
    {
        if (!astDump) return false;

        Log("", true);
        Log("=== Syntax Analyzer: Abstract Syntax Tree Dump ===", true);
        Log(DumpNode(root, 0), true);
        Log("==================================================", true);
        Log("", true);

        return true;
    }

    private static string DumpNode(INodeAST node, int indent = 0)
    {
        string pad = new(' ', indent * 2);
        string childPad = new(' ', (indent + 1) * 2);

        return node switch
        {
            // --- Top Level ---
            ProgramNode p => $"{pad}Program [Functions: {p.Functions.Count}, Types: {p.Types.Count}]\n" +
                             string.Join("", p.Types.Select(t => DumpNode(t, indent + 1))) +
                             string.Join("", p.Functions.Select(f => DumpNode(f, indent + 1))),

            FuncDeclNode f => $"{pad}Function: {f.Identifier.Lexeme} -> {TypeString(f.ReturnType)}\n" +
                              (f.GenericTypeParams != null ? $"{childPad}Generics: <{string.Join(", ", f.GenericTypeParams.Identifiers.Select(id => id.Lexeme))}>\n" : "") +
                              $"{childPad}Parameters:\n" +
                              (f.Parameters.Count > 0 ? string.Join("", f.Parameters.Select(p => DumpNode(p, indent + 2))) : $"{childPad}  (None)\n") +
                              $"{childPad}Body:\n{DumpNode(f.Body, indent + 2)}",

            TypeDeclNode d => $"{pad}Type (ADT): {d.Identifier.Lexeme}\n" +
                              (d.GenericTypeParams != null ? $"{childPad}Generics: <{string.Join(", ", d.GenericTypeParams.Identifiers.Select(id => id.Lexeme))}>\n" : "") +
                              $"{childPad}Constructors:\n" +
                              string.Join("", d.Constructors.Select(c => DumpNode(c, indent + 2))),

            ParamNode p => $"{pad}{p.Identifier.Lexeme} : {TypeString(p.DeclaredType)}\n",

            ConDeclNode c => $"{pad}{c.ConstructorName.Lexeme}{(c.PayloadType != null ? $" : {TypeString(c.PayloadType)}" : "")}\n",

            // --- Statements & Blocks ---
            ExprBlock b => $"{pad}ExprBlock:\n" +
                           string.Join("", b.Statements.Select(s => DumpNode(s, indent + 1))) +
                           $"{childPad}Return:\n{DumpNode(b.ReturnExpression, indent + 2)}",

            VarDeclStmt v => $"{pad}Var Bind: {(v.Pattern is IdPattern idp ? idp.Identifier.Lexeme : "Pattern")}{(v.DeclaredType != null ? $" : {TypeString(v.DeclaredType)}" : "")}\n" +
                             $"{childPad}Value:\n{DumpNode(v.Initializer, indent + 2)}",

            ExprStmt s => $"{pad}Expr Statement:\n{DumpNode(s.Expression, indent + 1)}",

            // --- Expressions ---
            BinaryExpr b => $"{pad}BinaryOp ({b.Operator.Lexeme})\n" +
                            $"{childPad}Left:\n{DumpNode(b.Left, indent + 2)}" +
                            $"{childPad}Right:\n{DumpNode(b.Right, indent + 2)}",

            UnaryExpr u => $"{pad}UnaryOp ({u.Operator.Lexeme})\n" +
                           $"{childPad}Operand:\n{DumpNode(u.Right, indent + 2)}",

            CallExpr c => $"{pad}Call:\n" +
                          $"{childPad}Callee:\n{DumpNode(c.Callee, indent + 2)}" +
                          $"{childPad}Arguments:\n" +
                          (c.Arguments.Count > 0 ? string.Join("", c.Arguments.Select(a => DumpNode(a, indent + 2))) : $"{childPad}  (None)\n"),

            TernaryExpr t => $"{pad}Ternary:\n" +
                             $"{childPad}Condition:\n{DumpNode(t.Condition, indent + 2)}" +
                             $"{childPad}True:\n{DumpNode(t.TrueBranch, indent + 2)}" +
                             $"{childPad}False:\n{DumpNode(t.FalseBranch, indent + 2)}",

            IfExpr i => $"{pad}If Expr:\n" +
                        $"{childPad}Condition:\n{DumpNode(i.Condition, indent + 2)}" +
                        $"{childPad}True Block:\n{DumpNode(i.TrueBlock, indent + 2)}" +
                        string.Join("", i.ElseIfs.Select(e => $"{childPad}ElseIf Condition:\n{DumpNode(e.Condition, indent + 2)}{childPad}ElseIf Block:\n{DumpNode(e.Block, indent + 2)}")) +
                        (i.ElseBlock != null ? $"{childPad}Else Block:\n{DumpNode(i.ElseBlock, indent + 2)}" : ""),

            SwitchExpr s => $"{pad}Switch Expr:\n" +
                            $"{childPad}Target:\n{DumpNode(s.TargetExpression, indent + 2)}" +
                            $"{childPad}Cases:\n" +
                            string.Join("", s.Cases.Select(c => DumpNode(c, indent + 2))),

            PatternMatchNode p => $"{pad}Case:\n" +
                                  $"{childPad}Pattern:\n{DumpNode(p.Pattern, indent + 2)}" +
                                  $"{childPad}Result:\n{DumpNode(p.ResultExpression, indent + 2)}",

            LambdaExpr l => $"{pad}Lambda: -> {TypeString(l.ReturnType)}\n" +
                            $"{childPad}Parameters:\n" +
                            (l.Parameters.Count > 0 ? string.Join("", l.Parameters.Select(p => DumpNode(p, indent + 2))) : $"{childPad}  (None)\n") +
                            $"{childPad}Body:\n{DumpNode(l.Body, indent + 2)}",

            ConExpr c => $"{pad}Constructor: {c.ConstructorName.Lexeme}\n" +
                         (c.Payloads.Count > 0 ? $"{childPad}Payloads:\n" + string.Join("", c.Payloads.Select(p => DumpNode(p, indent + 2))) : ""),

            ListLitExpr l => $"{pad}List Literal [{l.Elements.Count} elems]\n" +
                             string.Join("", l.Elements.Select(e => DumpNode(e, indent + 1))),

            ArrLitExpr a => $"{pad}Array Literal [{a.Elements.Count} elems]\n" +
                            string.Join("", a.Elements.Select(e => DumpNode(e, indent + 1))),

            TupleLitExpr t => $"{pad}Tuple Literal ({t.Elements.Count} elems)\n" +
                              string.Join("", t.Elements.Select(e => DumpNode(e, indent + 1))),

            LiteralExpr l => $"{pad}Literal: {l.Value.Lexeme}\n",

            IdentifierExpr i => $"{pad}Identifier: {i.Identifier.Lexeme}\n",

            // --- Types ---
            ITypeAST it => $"{pad}{TypeString(it)}\n",

            // --- Patterns ---
            LiteralPattern lp => $"{pad}Literal Pattern: {lp.Value.Lexeme}\n",

            IdPattern ip => $"{pad}Id Pattern: {ip.Identifier.Lexeme}\n",

            TuplePattern tp => $"{pad}Tuple Pattern\n" +
                               string.Join("", tp.Patterns.Select(p => DumpNode(p, indent + 1))),

            ListPattern lp => $"{pad}List Pattern\n" +
                              string.Join("", lp.Patterns.Select(p => DumpNode(p, indent + 1))),

            ArrPattern ap => $"{pad}Array Pattern\n" +
                             string.Join("", ap.Patterns.Select(p => DumpNode(p, indent + 1))),

            ConsPattern cp => $"{pad}Cons Pattern (::)\n" +
                              $"{childPad}Head:\n{DumpNode(cp.Head, indent + 2)}" +
                              $"{childPad}Tail:\n{DumpNode(cp.Tail, indent + 2)}",

            ConPattern cp => $"{pad}Constructor Pattern: {cp.ConstructorName.Lexeme}\n" +
                             (cp.PayloadPatterns.Count > 0 ? $"{childPad}Payloads:\n" + string.Join("", cp.PayloadPatterns.Select(p => DumpNode(p, indent + 2))) : ""),

            // --- Fallback ---
            _ => $"{pad}Unimplemented Node: {node.GetType().Name}\n"
        };
    }

    /// --- Analyser Nonsense ---

    public bool TryAnalyzerDump(Analyzer.Environment global)
    {
        if (!analyzerDump) return false;

        Log("", true);
        Log("=== Semantic Analyzer: Global Environment Dump ===", true);

        // Sort symbols alphabetically by identifier for a cleaner debug read
        var sortedSymbols = global.GetLocalSymbols().OrderBy(s => s.DeclToken.Lexeme);

        foreach (var symbol in sortedSymbols)
        {
            Log(DumpSymbol(symbol, 1), true);
        }

        Log("==================================================", true);
        Log("", true);

        return true;
    }

    private static string DumpSymbol(Symbol symbol, int indent = 0)
    {
        string pad = new(' ', indent * 2);
        string typeStr = symbol.Type != null ? TypeString(symbol.Type) : "None";

        return symbol switch
        {
            FuncSymbol f => $"{pad}Function: {f.DeclToken.Lexeme}\n" +
                            $"{pad}  Signature : {typeStr}\n" +
                            $"{pad}  Arity     : {f.Arity}\n" +
                            $"{pad}  Generics  : [{(f.GenericParams.Count > 0 ? string.Join(", ", f.GenericParams.Select(g => g.Lexeme)) : "None")}]\n" +
                            $"{pad}  Intrinsic : {f.NativeId != null}\n",

            TypeSymbol t => $"{pad}Type (ADT): {t.DeclToken.Lexeme}\n" +
                            $"{pad}  Signature : {typeStr}\n" +
                            $"{pad}  Generics  : [{(t.GenericParams.Count > 0 ? string.Join(", ", t.GenericParams.Select(g => g.Lexeme)) : "None")}]\n",

            ConstructorSymbol c => $"{pad}Constructor: {c.DeclToken.Lexeme}\n" +
                                   $"{pad}  Parent ADT: {TypeString(c.ParentType)}\n" +
                                   $"{pad}  Payload   : {(c.PayloadType != null ? TypeString(c.PayloadType) : "None")}\n",

            VarSymbol v => $"{pad}Variable: {v.DeclToken.Lexeme}\n" +
                           $"{pad}  Type      : {typeStr}\n",

            GenericParamSymbol g => $"{pad}Generic Param: {g.DeclToken.Lexeme}\n",

            _ => $"{pad}Unknown Symbol: {symbol.DeclToken.Lexeme}\n"
        };
    }

    // --- Emitter Nonsense ---

    // --- Emitter Nonsense ---

    public bool TryEmitterDump(Backend.Module m)
    {
        if (!emitterDump) return false;

        Log("", true);
        Log("=== Backend: Bytecode Disassembly Dump ===", true);

        foreach (var func in m.GetSortedFunctions())
        {
            Log($"--- Function: {func.Name} (Index: {func.Index}, Arity: {func.Arity}) ---", true);
            
            // 1. Dump Constant Pool
            if (func.Body.Constants.Count > 0)
            {
                Log("  Constants:", true);
                for (int i = 0; i < func.Body.Constants.Count; i++)
                {
                    var c = func.Body.Constants[i];
                    string valStr = c.Tag switch {
                        Backend.CeraValue.ValueTag.Int => c.IntValue.ToString(),
                        Backend.CeraValue.ValueTag.Float => c.FloatValue.ToString(),
                        Backend.CeraValue.ValueTag.Bool => c.IntValue == 1 ? "true" : "false",
                        Backend.CeraValue.ValueTag.Char => $"'{char.ConvertFromUtf32((int)c.IntValue)}'",
                        Backend.CeraValue.ValueTag.Unit => "()",
                        Backend.CeraValue.ValueTag.String => $"\"{c.StringValue}\"",
                        _ => "?"
                    };
                    
                    // Pad the Tag to 8 characters so the values align beautifully
                    Log($"    {i:D4} : {c.Tag,-8} {valStr}", true);
                }
                Log("", true);
            }

            // 2. Dump Instructions
            Log("  Code:", true);
            var code = func.Body.Code;
            for (int offset = 0; offset < code.Count; )
            {
                offset = DisassembleInstruction(func.Body, offset);
            }
            Log("", true);
        }

        Log("==========================================", true);
        Log("", true);

        return true;
    }

    private int DisassembleInstruction(Backend.Chunk chunk, int offset)
    {
        var code = chunk.Code;
        var lines = chunk.Lines;
        
        // Both strings are exactly 4 characters wide to guarantee a perfect vertical column
        string lineStr = (offset > 0 && lines[offset] == lines[offset - 1]) 
            ? "   |" 
            : $"{lines[offset],4}";

        byte instruction = code[offset];
        Backend.OpCode op = (Backend.OpCode)instruction;
        
        // Pad the opcode to exactly 22 characters left-justified
        string prefix = $"{offset:D4} {lineStr} {op.ToString(),-22}";

        switch (op)
        {
            // --- 1-Byte Operand Instructions ---
            case Backend.OpCode.LOAD_CONST:
            case Backend.OpCode.PUSH_BYTE:
            case Backend.OpCode.LOAD_LOCAL:
            case Backend.OpCode.STORE_LOCAL:
            case Backend.OpCode.LOAD_UPVALUE:
            case Backend.OpCode.LOAD_FUNCTION:
            case Backend.OpCode.CALL:
            case Backend.OpCode.TAIL_CALL:
            case Backend.OpCode.ALLOC_CON:
            case Backend.OpCode.ALLOC_TUPLE:
            case Backend.OpCode.ALLOC_ARRAY:
            case Backend.OpCode.MATCH_TAG:
                byte operand = code[offset + 1];
                Log($"{prefix} {operand}", true);
                return offset + 2;

            // --- 2-Byte Operand Instructions (Little-Endian) ---
            case Backend.OpCode.LOAD_CONST_LONG:
            case Backend.OpCode.LOAD_FUNCTION_LONG:
            case Backend.OpCode.ALLOC_ARRAY_LONG:
                ushort longOperand = (ushort)(code[offset + 1] | (code[offset + 2] << 8));
                Log($"{prefix} {longOperand}", true);
                return offset + 3;

            // --- 2-Byte Offset Instructions (Big-Endian from Chunk.PatchJump) ---
            case Backend.OpCode.JUMP:
            case Backend.OpCode.JUMP_IF_FALSE:
            case Backend.OpCode.JUMP_IF_TRUE:
                ushort jumpOffset = (ushort)((code[offset + 1] << 8) | code[offset + 2]);
                // Jump targets are relative to the IP *after* the jump instruction is read
                Log($"{prefix} {jumpOffset} (to {offset + 3 + jumpOffset:D4})", true);
                return offset + 3;

            // --- 4-Byte Operand Instructions (Little-Endian UTF-32) ---
            case Backend.OpCode.PUSH_CHAR:
                int charVal = code[offset + 1] | (code[offset + 2] << 8) | (code[offset + 3] << 16) | (code[offset + 4] << 24);
                Log($"{prefix} '{char.ConvertFromUtf32(charVal)}'", true);
                return offset + 5;

            // --- Multi-Operand Intrinsic Calls ---
            case Backend.OpCode.CALL_INTRINSIC:
                byte intrinsicId = code[offset + 1];
                byte argCount = code[offset + 2];
                Log($"{prefix} id: {intrinsicId}, args: {argCount}", true);
                return offset + 3;

            // --- Variable Length Instructions (Closures) ---
            // --- Variable Length Instructions (Closures) ---
            case Backend.OpCode.MAKE_CLOSURE:
                byte upvalueCount = code[offset + 1];
                Log($"{prefix} {upvalueCount} upvalues", true);
                
                int currOffset = offset + 2;
                for (int i = 0; i < upvalueCount; i++)
                {
                    byte isLocal = code[currOffset];
                    byte index = code[currOffset + 1];
                    
                    // Extract the ternary logic outside the string interpolation
                    string captureType = isLocal == 1 ? "local" : "upvalue";
                    
                    // Use exactly 22 spaces to align the 'capture' text perfectly under the opcode column
                    Log($"{currOffset:D4}    |                        capture {captureType} {index}", true);
                    
                    currOffset += 2;
                }
                return currOffset;

            // --- 0-Operand Instructions (ALU, PUSH_0, POP, RETURN, UNPACK_*, etc.) ---
            default:
                // .TrimEnd() prevents parameterless instructions from having massive trailing whitespace
                Log(prefix.TrimEnd(), true);
                return offset + 1;
        }
    }


}