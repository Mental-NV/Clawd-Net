using ClawdNet.App;

var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0";
var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var dataRoot = string.IsNullOrWhiteSpace(localAppData)
    ? Path.Combine(AppContext.BaseDirectory, ".clawdnet")
    : Path.Combine(localAppData, "ClawdNet");
await using var host = new AppHost(version, dataRoot);
var result = await host.RunAsync(args, CancellationToken.None);

if (!string.IsNullOrWhiteSpace(result.StdOut))
{
    Console.Out.WriteLine(result.StdOut);
}

if (!string.IsNullOrWhiteSpace(result.StdErr))
{
    Console.Error.WriteLine(result.StdErr);
}

Environment.ExitCode = result.ExitCode;
