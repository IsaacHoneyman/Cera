using System.Diagnostics;
using System.Text;
using Cera.Compiler.Parser;
using Cera.Compiler.Analyzer;

namespace Cera.Compiler;

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

        dumpFilePath = $"Out/Dump/Cera_Dump_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
        if (dumpToFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dumpFilePath) ?? "");
        }

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
        long time = sw[(int)scope].ElapsedTicks;
        sw[(int)scope].Restart();

        if (scope == TimerScope.Task) sw[(int)TimerScope.SubTask].Restart();

        DetailLog($"{preMessage} In {Math.Round((double)time / TimeSpan.TicksPerSecond, 2)}s. {postMessage}");
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

            VarDeclStmt v => $"{pad}Var Bind: {v.Identifier.Lexeme}{(v.DeclaredType != null ? $" : {TypeString(v.DeclaredType)}" : "")}\n" +
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
                            $"{pad}  Intrinsic : {f.IsIntrinsic}\n",

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
}