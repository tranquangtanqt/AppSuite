namespace Rdbms.HtmlGenerator.Models;

/// <summary>Result of the "Thu ket noi" button: the connection opened, the server reported
/// <paramref name="ServerVersion"/>, and <paramref name="TableCount"/> tables/views fall inside
/// <paramref name="Scope"/> (what "1. Doc Database" would read) - 0 usually means a wrong Schema.</summary>
public sealed record ConnectionTestResult(string ServerVersion, int TableCount, string Scope);
