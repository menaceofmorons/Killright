using System.Diagnostics.CodeAnalysis;

namespace KillRight.DuckDbMigration;

public sealed record MigrationOptions(string SourcePath, string TargetPath)
{
    public const string Usage = "Usage: KillRight.DuckDbMigration [--source <KillRight.duckdb>] [--target <KillRight.db>]";

    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillRight");

    public static bool TryParse(string[] args, [NotNullWhen(true)] out MigrationOptions? options, out string error)
    {
        var source = Path.Combine(DefaultDirectory, "KillRight.duckdb");
        var target = Path.Combine(DefaultDirectory, "KillRight.db");

        options = null;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];

            if (name is not ("--source" or "--target"))
            {
                error = $"Unknown argument '{name}'. {Usage}";
                return false;
            }

            if (index + 1 >= args.Length)
            {
                error = $"Missing value for '{name}'. {Usage}";
                return false;
            }

            var value = args[++index];

            if (name == "--source")
                source = value;
            else
                target = value;
        }

        options = new MigrationOptions(source, target);

        return true;
    }
}
