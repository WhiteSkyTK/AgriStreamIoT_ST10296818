using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriStreamIoT.Shared.Messages;
using AgriStreamIoT.Shared.Telemetry;
using AgriStreamIoT.Web.Interfaces;

namespace AgriStreamIoT.Web.Services.Functions;

/// <summary>
/// Typed HttpClient for the Function App. The base URL and function key come from the
/// "AzureFunctions" settings (see FunctionsOptions); the key travels in the x-functions-key header.
/// </summary>
public sealed class FarmFunctionsClient : IFarmFunctionsClient
{
    private readonly HttpClient _http;
    private readonly ILogger<FarmFunctionsClient> _logger;

    public FarmFunctionsClient(HttpClient http, ILogger<FarmFunctionsClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string? BaseUrl => _http.BaseAddress?.ToString();

    public Task<FunctionCallResult<TelemetrySnapshot>> GetFieldTelemetryAsync(string fieldId, int hours, CancellationToken cancellationToken = default) =>
        GetAsync<TelemetrySnapshot>($"telemetry/latest?fieldId={Uri.EscapeDataString(fieldId)}&hours={hours}", cancellationToken);

    public Task<FunctionCallResult<TelemetrySnapshot>> GetFarmTelemetryAsync(IEnumerable<string> fieldIds, CancellationToken cancellationToken = default) =>
        GetAsync<TelemetrySnapshot>($"telemetry/latest?fieldIds={Uri.EscapeDataString(string.Join(',', fieldIds))}", cancellationToken);

    public async Task<FunctionCallResult<TriggerIrrigationResponse>> TriggerIrrigationAsync(TriggerIrrigationRequest request, CancellationToken cancellationToken = default)
    {
        const string endpoint = "irrigation/trigger";
        if (_http.BaseAddress is null) return FunctionCallResult<TriggerIrrigationResponse>.NotConfigured(endpoint);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.PostAsJsonAsync(endpoint, request, FarmJson.Options, cancellationToken);
            return await ReadAsync<TriggerIrrigationResponse>(response, endpoint, stopwatch, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Failure<TriggerIrrigationResponse>(endpoint, stopwatch, ex);
        }
    }

    private async Task<FunctionCallResult<T>> GetAsync<T>(string endpoint, CancellationToken cancellationToken)
    {
        if (_http.BaseAddress is null) return FunctionCallResult<T>.NotConfigured(endpoint);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.GetAsync(endpoint, cancellationToken);
            return await ReadAsync<T>(response, endpoint, stopwatch, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Failure<T>(endpoint, stopwatch, ex);
        }
    }

    private async Task<FunctionCallResult<T>> ReadAsync<T>(HttpResponseMessage response, string endpoint,
        Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var servedBy = response.Headers.TryGetValues("X-Served-By", out var values) ? values.FirstOrDefault() : null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var hint = response.StatusCode == HttpStatusCode.Unauthorized
                ? "401 Unauthorized - set AzureFunctions:Key to the Function App's default host key."
                : $"{(int)response.StatusCode} {response.ReasonPhrase}: {Shorten(body)}";
            _logger.LogWarning("Function call {Endpoint} failed: {Hint}", endpoint, hint);
            return new FunctionCallResult<T>(false, default, (int)response.StatusCode, hint,
                stopwatch.Elapsed.TotalMilliseconds, endpoint, servedBy);
        }

        var value = await response.Content.ReadFromJsonAsync<T>(FarmJson.Options, cancellationToken);
        return new FunctionCallResult<T>(value is not null, value, (int)response.StatusCode,
            value is null ? "Empty response body." : null, stopwatch.Elapsed.TotalMilliseconds, endpoint, servedBy);
    }

    private FunctionCallResult<T> Failure<T>(string endpoint, Stopwatch stopwatch, Exception ex)
    {
        var message = ex is TaskCanceledException
            ? $"Timed out after {stopwatch.Elapsed.TotalSeconds:F0}s."
            : ex.Message;
        _logger.LogWarning(ex, "Function call {Endpoint} failed: {Message}", endpoint, message);
        return new FunctionCallResult<T>(false, default, null,
            $"Could not reach the Function App ({message}) Is it running at {BaseUrl}?",
            stopwatch.Elapsed.TotalMilliseconds, endpoint, null);
    }

    private static string Shorten(string value) => value.Length <= 200 ? value : value[..200] + "...";
}
