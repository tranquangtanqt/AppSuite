using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Rdbms.HtmlGenerator.Services;

/// <summary>Thrown when opening the connection fails (vs. a later schema query failing), so the UI can tell
/// "could not connect" apart from "query timed out" without parsing driver-specific messages.
/// <see cref="Exception.InnerException"/> is the original driver exception.</summary>
public sealed class DatabaseConnectException(Exception inner) : Exception(inner.Message, inner)
{
    public static async Task OpenAsync(DbConnection connection, Action<string> log, CancellationToken ct)
    {
        try
        {
            await connection.OpenAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DatabaseConnectException(ex);
        }

        log("Da ket noi, dang doc schema...");
    }
}
