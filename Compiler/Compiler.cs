namespace Cera.Compiler;

public class Compiler
{
    public static void Main(string[] args)
    {
        Diagnostics diag = new(args.Contains("--dump"), args.Contains("--detail"));

        string? filePath = args.FirstOrDefault(a => !a.StartsWith('-'));

        if (string.IsNullOrWhiteSpace(filePath))
        {
            diag.LogError("No File Path Specified. Usage: compiler <filepath> [flags]");
            return;
        }

        ImportResolver ir = new(diag);

        List<Token> tokens = [];

        try { tokens = ir.ResolveAllImports(filePath); }
        catch { Environment.Exit(1); }
    }
}