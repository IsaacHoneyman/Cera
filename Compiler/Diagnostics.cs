using System.Diagnostics;
using System.Text;
using Cera.Compiler.Parser;

namespace Cera.Compiler;

/// <summary> Feedback class for the compiler. </summary>
public class Diagnostics
{
    public enum TimerScope {
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
        foreach (var t in tokens)
        {
            if (t.File != lastFile)
            {
                Log($"--- {t.File} ---", true);
                lastFile = t.File;
            }
            Log(t.ToString(), true);
        }
        return true;
    }

    public bool TryASTDump(List<INodeAST> nodes)
    {
        if (!astDump) return false;
        foreach (var n in nodes) Log("Not implemented.", true);
        return true;
    }
}