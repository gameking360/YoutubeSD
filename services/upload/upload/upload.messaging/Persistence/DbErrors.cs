using MySql.Data.MySqlClient;

namespace Upload.Messaging.Persistence;

public static class DbErrors
{
    private const int DuplicateEntry = 1062;

    /// <summary>Violação de chave única: no processed_messages, significa mensagem repetida.</summary>
    public static bool IsDuplicateKey(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException { Number: DuplicateEntry })
            {
                return true;
            }
        }

        return false;
    }
}
