using Microsoft.Data.Sqlite;

namespace Killright.Storage.Database;

internal sealed class CommandParameters
{
    private readonly SqliteCommand _command;

    public CommandParameters(SqliteCommand command)
    {
        _command = command;
    }

    public static string Bind(SqliteCommand command, object value)
    {
        return new CommandParameters(command).Add(value);
    }

    public static string BindList(SqliteCommand command, IEnumerable<long> values)
    {
        var parameters = new CommandParameters(command);

        return string.Join(", ", values.Select(value => parameters.Add(value)));
    }

    public string Add(object value)
    {
        var name = $"@p{_command.Parameters.Count}";
        _command.Parameters.AddWithValue(name, value);

        return name;
    }

    public string Row(params object[] values)
    {
        return $"({string.Join(", ", values.Select(Add))})";
    }
}
