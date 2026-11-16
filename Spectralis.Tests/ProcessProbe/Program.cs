namespace Spectralis.ProcessProbe;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        switch (args[0])
        {
            case "echo": Console.Write(args[1]); return 0;
            case "exit": return int.Parse(args[1]);
            case "hang": await Task.Delay(TimeSpan.FromMinutes(1)); return 0;
            case "cat": Console.Write(File.ReadAllText(args[1])); return 0;
            case "literal":
                if (args.Length != 2) return 99;
                Console.Write("<" + args[1] + ">");
                return 0;
            default: return 1;
        }
    }
}
