namespace ChessDatabase.Api;

public static class ApiResults
{
    /// <summary>A 400 validation problem for one field.</summary>
    public static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
