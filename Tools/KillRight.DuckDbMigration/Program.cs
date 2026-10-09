using KillRight.DuckDbMigration;

if (!MigrationOptions.TryParse(args, out var options, out var error))
{
    Console.WriteLine(error);
    return 1;
}

return MigrationRunner.Run(options, Console.Out);
