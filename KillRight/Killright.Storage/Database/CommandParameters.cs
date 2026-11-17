using Microsoft.Data.Sqlite;

namespace Killright.Storage.Database;

internal sealed class CommandParameters
{
    private readonly SqliteCommand _command;
    private int _count;

    public CommandParameters(SqliteCommand command)
    {
        _command = command;
    }

    public string Add(object value)
    {
        var name = $"@p{_count++}";
        _command.Parameters.AddWithValue(name, value);

        return name;
    }

    public string Row(params object[] values)
    {
        return $"({string.Join(", ", values.Select(Add))})";
    }
}
