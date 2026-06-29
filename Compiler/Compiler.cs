using Cera.Compiler.Exceptions;

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


        diag.EndSection(Diagnostics.TimerScope.Task, "Lexing completed", $"total tokens lexed: {tokens.Count}");
        diag.TryTokenDump(tokens);

        // Parser

        Parser.Parser p = new(tokens, diag);

        try { p.Parse(); }
        catch (ParserException)
        {
            diag.Close();
            Environment.Exit(3);
        }

        diag.EndSection(Diagnostics.TimerScope.Global, "Compilation completed");
        diag.Close();
    }

    public static void Main(string[] args)
    {
        Diagnostics diag = new(
            args.Contains("--dump") || args.Contains("--du"), 
            args.Contains("--detail") || args.Contains("--de"),
            args.Contains("--tokens") || args.Contains("--t"),
            args.Contains("--ast")
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