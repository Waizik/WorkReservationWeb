using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WorkReservationWeb.Browser.Tests;

public sealed class LocalAppHostFixture : IAsyncLifetime
{
    private static readonly Uri FunctionsDevelopmentUri = new("http://localhost:7287/");
    private static readonly Uri WebDevelopmentUri = new("http://localhost:5273/");
    private Process? functionsProcess;
    private Process? webProcess;
    private Uri? functionsBaseUri;
    private Uri? webBaseUri;
    private readonly StringBuilder functionsOutput = new();
    private readonly StringBuilder webOutput = new();

    public string WebBaseUrl => webBaseUri?.ToString().TrimEnd('/')
        ?? throw new InvalidOperationException("The web host has not been started.");

    public async Task InitializeAsync()
    {
        functionsBaseUri = FunctionsDevelopmentUri;
        webBaseUri = WebDevelopmentUri;

        functionsProcess = StartProcess(
            "dotnet",
            "run --no-launch-profile -- --port 7287",
            Path.Combine(ResolveRepositoryRoot(), "src", "WorkReservationWeb.Functions"),
            environmentVariables: null,
            outputBuffer: functionsOutput);

        await WaitForReadyAsync(functionsProcess, functionsOutput, new Uri(functionsBaseUri, "api/public/services"), CancellationToken.None);

        webProcess = StartProcess(
            "dotnet",
            $"run --no-launch-profile --urls {webBaseUri}",
            Path.Combine(ResolveRepositoryRoot(), "src", "WorkReservationWeb.Web"),
            new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development"
            },
            webOutput);

        await WaitForReadyAsync(webProcess, webOutput, webBaseUri, CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        StopProcess(webProcess);
        StopProcess(functionsProcess);
        return Task.CompletedTask;
    }

    private static Process StartProcess(
        string fileName,
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables,
        StringBuilder outputBuffer)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        if (environmentVariables is not null)
        {
            foreach (var environmentVariable in environmentVariables)
            {
                process.StartInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
            }
        }

        process.OutputDataReceived += (_, eventArgs) => AppendOutput(outputBuffer, eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => AppendOutput(outputBuffer, eventArgs.Data);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName} {arguments}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitForReadyAsync(Process process, StringBuilder outputBuffer, Uri url, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(90);

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Process exited before '{url}' became ready. Output:{Environment.NewLine}{outputBuffer}");
            }

            try
            {
                using var response = await httpClient.GetAsync(url, cancellationToken);
                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(1000, cancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for '{url}'. Output:{Environment.NewLine}{outputBuffer}");
    }

    private static string ResolveRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            var solutionPath = Path.Combine(currentDirectory.FullName, "src", "WorkReservationWeb.slnx");
            if (File.Exists(solutionPath))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root for browser tests.");
    }

    private static void AppendOutput(StringBuilder outputBuffer, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (outputBuffer)
        {
            outputBuffer.AppendLine(line);
        }
    }

    private static void StopProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}