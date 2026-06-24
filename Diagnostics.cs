/// <summary> Feedback class for the compiler. </summary>
public class Diagnostics
{
    private readonly bool createDump;
    private readonly string dumpFilePath;

    public Diagnostics(bool createDump)
    {
        this.createDump = createDump;
        dumpFilePath = $"Out/Dump/Cera_Dump_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
        if (createDump)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dumpFilePath) ?? "");
        }

    }

    public void Log(string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message);
        if (createDump) File.AppendAllText(dumpFilePath, $"{message}\n");
    }

    public void LogWarning(string warning)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[Warning] " + warning);
        Console.ForegroundColor = ConsoleColor.White;
        if (createDump) File.AppendAllText(dumpFilePath, $"[Warning] {warning}\n");
    }

    public void LogError(string error)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[Error] " + error);
        Console.ForegroundColor = ConsoleColor.White;
        if (createDump) File.AppendAllText(dumpFilePath, $"[Error] {error}\n");

    }
}