using Npgsql;

namespace VoltGrid.Gateway;

public static class DatabaseErrors
{
    public static bool IsUnavailable(Exception ex) =>
        ex is NpgsqlException || ex.InnerException is NpgsqlException;
}
