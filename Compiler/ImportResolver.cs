using Cera.Compiler.Exceptions;

namespace Cera.Compiler;

public class ImportResolver(Diagnostics diag)
{
    private readonly Diagnostics diag = diag; 
    private readonly HashSet<string> visitedFiles = [];

    public List<Token> ResolveAllImports(string filePath)
    {
        return ResolveRecursive(Path.GetFullPath(filePath));
    }

    private List<Token> ResolveRecursive(string absPath)
    {
        if (!visitedFiles.Add(absPath)) return [];

        if (!File.Exists(absPath))
        {
            diag.LogError($"Import Resolver: File \"{absPath}\" Does Not Exist.");    
        }

        string file = Path.GetFileName(absPath); 
        string content = File.ReadAllText(absPath);
        Lexer lexer = new(content, file, diag);
        List<Token> tokens = lexer.Lex();

        List<Token> unifiedStream = [];
        int i = 0;

        while (TryConsumeImport(tokens, i))
        {
            string combinedPath = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(absPath) ?? "", tokens[i + 1].Lexeme.Trim('"')));

            unifiedStream.AddRange(ResolveRecursive(combinedPath));

            i += 3;
        }

        return unifiedStream;
    }

    public bool TryConsumeImport(List<Token> tokens, int index)
    {
        if (index + 3 > tokens.Count || tokens[index].Tag != TokenType.Import) return false;

        if (tokens[index + 1].Tag != TokenType.StringLiteral)
        {
            FatalError("Missing File Name", tokens[index + 1]);
        }

        if (tokens[index + 2].Tag != TokenType.Semicolon)
        {
            FatalError("Statement Missing ;", tokens[index + 2]);
        }

        return true;
    }

    private void FatalError(string message, Token token)
    {
        ImportResolverException e = new(message, token);
        diag.LogError(e.Message);
        throw e;
    }

}