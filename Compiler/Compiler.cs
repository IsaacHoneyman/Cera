using Cera.Compiler.Exceptions;
using Cera.Compiler.Parser;

namespace Cera.Compiler;

public class Compiler
{
    private string filePath;
    private Diagnostics diag;

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

        Lexer.ImportResolver ir = new(diag);
        List<Token> tokens = [];

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
        INodeAST? ast = null;

        try { ast = p.Parse(); }
        catch (ParserException)
        {
            diag.Close();
            Environment.Exit(3);
        }

        diag.TryASTDump(ast);
        diag.EndSection(Diagnostics.TimerScope.Task, "Parsing completed");

        diag.EndSection(Diagnostics.TimerScope.Global, "Compilation completed");
        diag.Close();
    }

    public static void Main(string[] args)
    {
        Diagnostics diag = new(
            args.Contains("--dump") || args.Contains("--du"), 
            args.Contains("--detail") || args.Contains("--de"),
            args.Contains("--tokens") || args.Contains("--t"),
            args.Contains("--ast") || args.Contains("--a")
            );

        string? filePath = args.FirstOrDefault(a => !a.StartsWith('-'));

        if (string.IsNullOrWhiteSpace(filePath))
        {
            diag.LogError("No file path specified. Usage: compiler <filepath> [flags]");
            return;
        }
        
        new Compiler(filePath, diag);
    }
}