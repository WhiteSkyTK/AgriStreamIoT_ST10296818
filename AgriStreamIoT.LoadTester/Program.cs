using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

// AgriStream IoT load tester: simulates many farm users / sensor dashboards hitting the web app at once,
// so App Service CPU climbs and the autoscale rule adds instances. Counts the instances that answered
// (X-Served-By header) so you can see the scale-out happen.
//
//   --url       web app address (required), e.g. https://agristream-web.azurewebsites.net
//   --mode      cpu (moisture outlook, CPU heavy) | users (pages + farm summary) | mixed   [default mixed]
//   --users     virtual users at full load                                             [default 50]
//   --duration  test length in seconds                                                 [default 300]
//   --ramp      seconds to ramp from 1 user to --users                                 [default 60]
//   --out       CSV file with one row per 10-second window                             [default loadtest-results.csv]

var options = Args.Parse(args);
if (options.Url is null)
{
    Console.WriteLine("Usage: dotnet run -c Release -- --url https://<your-app>.azurewebsites.net [--mode cpu|users|mixed] [--users 50] [--duration 300] [--ramp 60]");
    return 1;
}

var cpuPaths = new[] { "/api/analytics/outlook?fieldId=F01", "/api/analytics/outlook?fieldId=F02", "/api/analytics/outlook?fieldId=F03",
                       "/api/analytics/outlook?fieldId=F04", "/api/analytics/outlook?fieldId=F05", "/api/analytics/outlook?fieldId=F06" };
var userPaths = new[] { "/", "/api/farm/summary", "/health", "/api/instance", "/Account/Login" };
var paths = options.Mode switch
{
    "cpu" => cpuPaths,
    "users" => userPaths,
    _ => cpuPaths.Concat(userPaths).ToArray()
};

using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 1000, PooledConnectionLifetime = TimeSpan.FromSeconds(30) })
{
    BaseAddress = new Uri(options.Url.TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromSeconds(60)
};
// App Service ARR affinity would pin this client to one instance; drop the cookie so requests spread out.
http.DefaultRequestHeaders.Add("User-Agent", "AgriStream-LoadTester/1.0");

var samples = new ConcurrentQueue<Sample>();
var instances = new ConcurrentDictionary<string, int>();
var total = Stopwatch.StartNew();
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds));

Console.WriteLine($"AgriStream load test: {options.Mode} mode, up to {options.Users} users for {options.DurationSeconds}s against {http.BaseAddress}");
Console.WriteLine("time   users   req/s   p50 ms   p95 ms   errors   instances");

var workers = new List<Task>();
var reporter = Task.Run(async () =>
{
    var rows = new List<string> { "elapsed_s,users,requests,rps,p50_ms,p95_ms,max_ms,errors,instances_seen" };
    while (!stop.IsCancellationRequested)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(10), stop.Token); } catch (OperationCanceledException) { }
        var window = new List<Sample>();
        while (samples.TryDequeue(out var s)) window.Add(s);
        var ok = window.Where(s => s.Ok).Select(s => s.Ms).OrderBy(x => x).ToList();
        var row = new Window((int)total.Elapsed.TotalSeconds, workers.Count, window.Count, window.Count / 10.0,
            Percentile(ok, 0.50), Percentile(ok, 0.95), ok.Count == 0 ? 0 : ok[^1], window.Count(s => !s.Ok), instances.Count);
        Console.WriteLine($"{row.Elapsed,4}s  {row.Users,5}  {row.Rps,6:F1}  {row.P50,7:F0}  {row.P95,7:F0}  {row.Errors,7}   {row.Instances}");
        rows.Add(string.Join(',', row.Elapsed, row.Users, row.Requests, row.Rps.ToString("F1", CultureInfo.InvariantCulture),
            row.P50.ToString("F0", CultureInfo.InvariantCulture), row.P95.ToString("F0", CultureInfo.InvariantCulture),
            row.Max.ToString("F0", CultureInfo.InvariantCulture), row.Errors, row.Instances));
    }
    await File.WriteAllLinesAsync(options.OutFile, rows);
});

// Ramp up: start one virtual user every (ramp / users) seconds.
var gap = TimeSpan.FromMilliseconds(Math.Max(10, options.RampSeconds * 1000.0 / Math.Max(1, options.Users)));
for (var i = 0; i < options.Users && !stop.IsCancellationRequested; i++)
{
    var seed = i;
    workers.Add(Task.Run(() => VirtualUser(seed, stop.Token)));
    try { await Task.Delay(gap, stop.Token); } catch (OperationCanceledException) { }
}
await Task.WhenAll(workers);
await reporter;

Console.WriteLine();
Console.WriteLine($"Done in {total.Elapsed.TotalSeconds:F0}s. Instances that answered: {instances.Count}");
foreach (var (id, count) in instances.OrderByDescending(x => x.Value)) Console.WriteLine($"  {id,-20} {count,8} requests");
Console.WriteLine($"Per-window results saved to {Path.GetFullPath(options.OutFile)}");
return 0;

async Task VirtualUser(int seed, CancellationToken token)
{
    var random = new Random(seed * 7919 + 17);
    while (!token.IsCancellationRequested)
    {
        var path = paths[random.Next(paths.Length)].TrimStart('/');
        var watch = Stopwatch.StartNew();
        var ok = false;
        try
        {
            using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, token);
            await response.Content.ReadAsByteArrayAsync(token);
            ok = response.IsSuccessStatusCode;
            if (response.Headers.TryGetValues("X-Served-By", out var servedBy))
                instances.AddOrUpdate(servedBy.First(), 1, (_, n) => n + 1);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception) { ok = false; }
        samples.Enqueue(new Sample(watch.Elapsed.TotalMilliseconds, ok));
        // Think time: a person looking at a dashboard, 0.5-1.5 s between requests.
        try { await Task.Delay(500 + random.Next(1000), token); } catch (OperationCanceledException) { return; }
    }
}

static double Percentile(List<double> sorted, double p) =>
    sorted.Count == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];

record Sample(double Ms, bool Ok);
record Window(int Elapsed, int Users, int Requests, double Rps, double P50, double P95, double Max, int Errors, int Instances);

sealed class Args
{
    public string? Url { get; private set; }
    public string Mode { get; private set; } = "mixed";
    public int Users { get; private set; } = 50;
    public int DurationSeconds { get; private set; } = 300;
    public int RampSeconds { get; private set; } = 60;
    public string OutFile { get; private set; } = "loadtest-results.csv";

    public static Args Parse(string[] args)
    {
        var a = new Args();
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            var value = args[i + 1];
            switch (args[i].ToLowerInvariant())
            {
                case "--url": a.Url = value; break;
                case "--mode": a.Mode = value.ToLowerInvariant(); break;
                case "--users": a.Users = Math.Clamp(int.Parse(value), 1, 2000); break;
                case "--duration": a.DurationSeconds = Math.Clamp(int.Parse(value), 10, 7200); break;
                case "--ramp": a.RampSeconds = Math.Clamp(int.Parse(value), 0, 3600); break;
                case "--out": a.OutFile = value; break;
            }
        }
        return a;
    }
}
