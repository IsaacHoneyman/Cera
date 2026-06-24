public class Compiler
{
    public static void Main(string[] args)
    {
        Diagnostics diag = new(args.Contains("--dump"));

        string? filePath = args.FirstOrDefault(a => !a.StartsWith("-"));

        if (string.IsNullOrWhiteSpace(filePath))
        {
            diag.LogError("No File Path Specified. Usage: compiler <filepath> [flags]");
            return;
        }

        if (!File.Exists(filePath))
        {
            diag.LogError($"File Path: '{filePath}' Is Not A Valid Path!");
            return;
        }

        Lexer lex = new(File.ReadAllText(filePath), diag);

        List<Token> tokens = [];

        try { tokens = lex.Lex(); }
        catch { Environment.Exit(1); }

        foreach (var t in tokens)
        {
            diag.Log(t.ToString());
        }
    }
}