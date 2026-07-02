using Cera.Compiler.Logging;
using Cera.Compiler.Lexer;
using Cera.Compiler.Parser;

namespace Cera.Compiler;

public class Compiler
{
    private readonly string filePath;
    private readonly Diagnostics diag;

    private Compiler(string filePath, Diagnostics diag)
    {
        this.filePath = filePath;
        this.diag = diag;

        Compile();
    }

    private void Compile()
    {
        diag.Open();

        // Lexer

        ImportResolver ir = new(diag);
        List<Lexer.Token> tokens = [];

        try { tokens = ir.ResolveAllImports(filePath); }
        catch (LexerException)
        {
            diag.Close(); 
            Environment.Exit(1); 
        }
        catch (ImportResolverException)
        {
            diag.Close();
            Environment.Exit(2);
        }


        diag.TryTokenDump(tokens);
        diag.EndSection(Diagnostics.TimerScope.Task, "Lexing completed", $"total tokens lexed: {tokens.Count}");

        // Parser

        Parser.Parser p = new(tokens, diag);
        ProgramNode? ast = null;

        try { ast = p.Parse(); }
        catch (ParserException)
        {
            diag.Close();
            Environment.Exit(3);
        }

        diag.TryASTDump(ast);
        diag.EndSection(Diagnostics.TimerScope.Task, "Parsing completed");

        Analyzer.Analyzer a = new(ast, diag);
        Analyzer.Environment? e = null;

        try { e = a.Analyze(); }
        catch (AnalyzerException)
        {
            diag.Close();
            Environment.Exit(4);
        }

        diag.TryAnalyzerDump(e);
        diag.EndSection(Diagnostics.TimerScope.Task, "Analysis completed");

        diag.EndSection(Diagnostics.TimerScope.Global, "Compilation completed");
        diag.Close();
    }

    private static bool[] GenerateDiagnosticsArgs(string[] args)
    {
        bool[] diagArgs = new bool[5];
        diagArgs[1] = args.Contains("--verbose") || args.Contains("-v");
        diagArgs[2] = args.Contains("--tokens") || args.Contains("-t");
        diagArgs[3] = args.Contains("--ast") || args.Contains("-a");
        diagArgs[4] = args.Contains("--analyzer") || args.Contains("-s");

        diagArgs[0] = args.Contains("--dump") || args.Contains("-d") 
            || diagArgs[2] || diagArgs[3] || diagArgs[4];

        return diagArgs;
    }

    public static void Main(string[] args)
    {
        Diagnostics diag = new(GenerateDiagnosticsArgs(args));

        string? filePath = args.FirstOrDefault(a => !a.StartsWith('-'));

        if (string.IsNullOrWhiteSpace(filePath))
        {
            diag.LogError("No file path specified. Usage: compiler 'filepath' [flags]");
            return;
        }

        _ = new Compiler(filePath, diag);
    }
}