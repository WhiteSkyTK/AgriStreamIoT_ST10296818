namespace AgriStreamIoT.Web.Services.Functions;

/// <summary>Outcome of one call to an HTTP-triggered function, with timing for the UI.</summary>
public sealed record FunctionCallResult<T>(
    bool Success,
    T? Value,
    int? StatusCode,
    string? Error,
    double ElapsedMs,
    string Endpoint,
    string? ServedBy)
{
    public static FunctionCallResult<T> NotConfigured(string endpoint) =>
        new(false, default, null, "AzureFunctions:BaseUrl is not set.", 0, endpoint, null);
}
