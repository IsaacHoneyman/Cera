using System.Diagnostics.CodeAnalysis;
using Cera.Compiler.Lexer;
using Cera.Compiler.Logging;
using Cera.Compiler.Parser;

namespace Cera.Compiler;

public class CompilerContext(Diagnostics diag)
{
    public Dictionary<string, FileAST> ParsedFiles { get; } = [];

    public FileAST ResolveAndParse(string entryPath) { return ParseRecursive(Path.GetFullPath(entryPath)); }

    private FileAST ParseRecursive(string absPath)
    {
        if (ParsedFiles.TryGetValue(absPath, out FileAST? cachedAST)) 
            return cachedAST; // we've already parsed this another import

        if (!File.Exists(absPath)) 
            FatalError($"File '{absPath}' does not exist");
        
        var file = Path.GetFileNameWithoutExtension(absPath);
        var content = File.ReadAllText(absPath);

        Lexer.Lexer lex = new(content, file, diag);
        var tokens = lex.Lex();

        Parser.Parser parser = new(tokens, diag);
        FileAST ast = parser.ParseFile(absPath);
        ParsedFiles[absPath] = ast;

        foreach (var import in ast.Imports)
        {
            string importName = import.PathLiteral.Lexeme.Trim('"');
            string currentDir = Path.GetDirectoryName(absPath) ?? "";            
            string resolvedPath = ResolveFilePath(currentDir, importName);
            ParseRecursive(resolvedPath);
        }

        return ast;
    }

    private string ResolveFilePath(string currentDirectory, string importName)
    {
        string localPath = Path.GetFullPath(Path.Combine(currentDirectory, importName));
        if (File.Exists(localPath)) return localPath;

        string? globalLibPath = Environment.GetEnvironmentVariable("CERA_LIB_PATH");
        if (!string.IsNullOrWhiteSpace(globalLibPath))
        {
            string globalPath = Path.GetFullPath(Path.Combine(globalLibPath, importName));
            if (File.Exists(globalPath)) return globalPath;
        }

        return localPath;
    }

    [DoesNotReturn]
    private void FatalError(string message)
    {
        ContextException e = new(message, Token.None());
        diag.LogError(e.Message);
        throw e;
    }
}