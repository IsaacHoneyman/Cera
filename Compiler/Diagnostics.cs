using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using Cera.Compiler.Parser;

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

    private readonly string dumpFilePath;
    private readonly StringBuilder sb = new();

    private readonly Stopwatch[] sw = [new(), new(), new()]; // global, task, sub-task

    public Diagnostics(bool dumpToFile, bool detailedDiag, bool tokenDump, bool astDump)
    {
        this.dumpToFile = dumpToFile;
        this.detailedDiag = detailedDiag;

        this.tokenDump = tokenDump;
        this.astDump = astDump;

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

        DetailLog($"{preMessage} In {Math.Round((double)time / TimeSpan.TicksPerSecond, 2)}s. {postMessage}");
        if (scope == TimerScope.Task) DetailLog("");
    }

    // --- Dumps ---

    public bool TryTokenDump(List<Token> tokens)
    {
        if (!tokenDump) return false;

        string lastFile = "\0";
        Log("", true);
        foreach (var t in tokens)
        {
            if (t.File != lastFile)
            {
                Log($"--- {t.File} ---", true);
                lastFile = t.File;
            }
            Log(t.ToString(), true);
        }
        Log("", true);
        return true;
    }

    // --- AST Nonsense ---

    public bool TryASTDump(INodeAST root)
    {
        if (!astDump) return false;
        Log("", true);
        Log(DumpNode(root, 0), true);

        return true;
    }

    private static string DumpNode(INodeAST node, int indent = 0)
    {
        string pad = new(' ', indent * 2);
        
        return node switch
        {
            // --- Top Level ---
            ProgramNode p => $"{pad}Program: Funcs({p.Functions.Count}), Types({p.Types.Count})\n" +
                             string.Join("", p.Types.Select(t => DumpNode(t, indent + 1))) +
                             string.Join("", p.Functions.Select(f => DumpNode(f, indent + 1))),
                             
            FuncDeclNode f => $"{pad}Func: Id({f.Identifier.Lexeme}), Gen({f.GenericTypeParams != null}), Params({f.Parameters.Count}), Returns({TypeString(f.ReturnType)})\n" +
                              string.Join("", f.Parameters.Select(p => DumpNode(p, indent + 1))) +
                              $"{DumpNode(f.Body, indent + 1)}",  

            TypeDeclNode d => $"{pad}Type: Id({d.Identifier.Lexeme}), Gen({d.GenericTypeParams != null}), Constructors({d.Constructors.Count})\n" +
                              string.Join("", d.Constructors.Select(c => DumpNode(c, indent + 1))),
                              
            ParamNode p => $"{pad}Param: Id({p.Identifier.Lexeme}), Type({TypeString(p.DeclaredType)})\n",

            ConDeclNode c => $"{pad}ConstructorDecl: Name({c.ConstructorName.Lexeme}), Type({(c.PayloadType != null ? TypeString(c.PayloadType) : "None")})\n",

            // --- Statements & Blocks ---
            ExprBlock b => $"{pad}ExprBlock:\n" +
                           string.Join("", b.Statements.Select(s => DumpNode(s, indent + 1))) +
                           $"{pad}  Return:\n{DumpNode(b.ReturnExpression, indent + 2)}",
                           
            VarDeclStmt v => $"{pad}Var: Id({v.Identifier.Lexeme}), Type({(v.DeclaredType != null ? TypeString(v.DeclaredType) : "None")})\n" +
                             (v.DeclaredType != null ? DumpNode(v.DeclaredType, indent + 1) : "") + // Note: You might want to remove this line to avoid double-printing the type, since it's already in the header above.
                             DumpNode(v.Initializer, indent + 1),
                             
            ExprStmt s => $"{pad}ExprStmt:\n{DumpNode(s.Expression, indent + 1)}",

            // --- Expressions ---
            BinaryExpr b => $"{pad}BinaryExpr: {b.Operator.Lexeme}\n" +
                            DumpNode(b.Left, indent + 1) + 
                            DumpNode(b.Right, indent + 1),
                            
            UnaryExpr u => $"{pad}UnaryExpr: {u.Operator.Lexeme}\n" +
                           DumpNode(u.Right, indent + 1),

            CallExpr c => $"{pad}Call: Args({c.Arguments.Count})\n" +
                          $"{pad}  Callee:\n{DumpNode(c.Callee, indent + 2)}" +
                          $"{pad}  Args:\n" + string.Join("", c.Arguments.Select(a => DumpNode(a, indent + 2))),

            TernaryExpr t => $"{pad}TernaryExpr:\n" +
                             $"{pad}  Condition:\n{DumpNode(t.Condition, indent + 2)}" +
                             $"{pad}  True:\n{DumpNode(t.TrueBranch, indent + 2)}" +
                             $"{pad}  False:\n{DumpNode(t.FalseBranch, indent + 2)}",

            IfExpr i => $"{pad}IfExpr:\n" +
                        $"{pad}  Condition:\n{DumpNode(i.Condition, indent + 2)}" +
                        $"{pad}  TrueBlock:\n{DumpNode(i.TrueBlock, indent + 2)}" +
                        string.Join("", i.ElseIfs.Select(e => $"{pad}  ElseIf Condition:\n{DumpNode(e.Condition, indent + 2)}{pad}  ElseIf Block:\n{DumpNode(e.Block, indent + 2)}")) +
                        (i.ElseBlock != null ? $"{pad}  ElseBlock:\n{DumpNode(i.ElseBlock, indent + 2)}" : ""),

            SwitchExpr s => $"{pad}SwitchExpr: Cases({s.Cases.Count})\n" +
                            $"{pad}  Target:\n{DumpNode(s.TargetExpression, indent + 2)}" +
                            string.Join("", s.Cases.Select(c => DumpNode(c, indent + 1))),

            PatternMatchNode p => $"{pad}PatternMatch:\n" +
                                  $"{pad}  Pattern:\n{DumpNode(p.Pattern, indent + 2)}" +
                                  $"{pad}  Result:\n{DumpNode(p.ResultExpression, indent + 2)}",

            LambdaExpr l => $"{pad}LambdaExpr: Params({l.Parameters.Count}), Returns({TypeString(l.ReturnType)})\n" +
                            string.Join("", l.Parameters.Select(p => DumpNode(p, indent + 1))) +
                            $"{pad}  Body:\n{DumpNode(l.Body, indent + 2)}",

            ConExpr c => $"{pad}ConstructorExpr: Name({c.ConstructorName.Lexeme}), Payloads({c.Payloads.Count})\n" +
                         string.Join("", c.Payloads.Select(p => DumpNode(p, indent + 1))),

            ListLitExpr l => $"{pad}ListLit: Elements({l.Elements.Count})\n" + 
                             string.Join("", l.Elements.Select(e => DumpNode(e, indent + 1))),

            ArrLitExpr a => $"{pad}ArrLit: Elements({a.Elements.Count})\n" + 
                            string.Join("", a.Elements.Select(e => DumpNode(e, indent + 1))),

            TupleLitExpr t => $"{pad}TupleLit: Elements({t.Elements.Count})\n" + 
                              string.Join("", t.Elements.Select(e => DumpNode(e, indent + 1))),

            LiteralExpr l => $"{pad}Literal: {l.Value.Lexeme}\n",
            
            IdentifierExpr i => $"{pad}Identifier: {i.Identifier.Lexeme}\n",

            // --- Types ---
            ITypeAST it => $"{pad}{TypeString(it)}\n",

            // --- Patterns ---
            LiteralPattern lp => $"{pad}LiteralPattern: {lp.Value.Lexeme}\n",

            IdPattern ip => $"{pad}IdPattern: {ip.Identifier.Lexeme}\n",

            TuplePattern tp => $"{pad}TuplePattern: Elements({tp.Patterns.Count})\n" + 
                               string.Join("", tp.Patterns.Select(p => DumpNode(p, indent + 1))),

            ListPattern lp => $"{pad}ListPattern: Elements({lp.Patterns.Count})\n" + 
                              string.Join("", lp.Patterns.Select(p => DumpNode(p, indent + 1))),

            ArrPattern ap => $"{pad}ArrPattern: Elements({ap.Patterns.Count})\n" + 
                             string.Join("", ap.Patterns.Select(p => DumpNode(p, indent + 1))),

            ConsPattern cp => $"{pad}ConsPattern:\n" +
                              $"{pad}  Head:\n{DumpNode(cp.Head, indent + 2)}" +
                              $"{pad}  Tail:\n{DumpNode(cp.Tail, indent + 2)}",

            ConPattern cp => $"{pad}ConstructorPattern: Name({cp.ConstructorName.Lexeme}), Payloads({cp.PayloadPatterns.Count})\n" + 
                             string.Join("", cp.PayloadPatterns.Select(p => DumpNode(p, indent + 1))),

            // --- Fallback ---
            _ => $"{pad}Unimplemented Node: {node.GetType().Name}\n"
        };
    }

    private static string TypeString(ITypeAST type)
    {
        return type switch
        {
            BaseType t => t.TypeName.Lexeme,
            FuncType ft => $"{TypeString(ft.ParameterType)} -> {TypeString(ft.ReturnType)}",
            ListType lt => $"{TypeString(lt.InnerType)} list",
            ArrType at => $"{TypeString(at.InnerType)} arr",
            TupleType tt => $"({string.Join(" * ", tt.Types.Select(TypeString))})",
            GenericType gt => $"{gt.BaseName.Lexeme}<{string.Join(", ", gt.TypeArguments.Select(TypeString))}>",

            _ => $"Unimplemented Type"
        };
    }
}