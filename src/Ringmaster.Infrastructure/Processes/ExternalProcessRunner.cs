using System.Diagnostics;
using System.Text;

namespace Ringmaster.Infrastructure.Processes;

public sealed class ExternalProcessRunner(TimeProvider timeProvider) : IExternalProcessRunner
{
    public async Task<ExternalProcessResult> RunAsync(ExternalProcessSpec spec, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.WorkingDirectory);
        string resolvedFileName = ResolveFileName(spec.FileName, spec.WorkingDirectory);

        ProcessStartInfo startInfo = new()
        {
            FileName = resolvedFileName,
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardInput = spec.StandardInputText is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach ((string key, string value) in spec.EnvironmentVariables)
        {
            startInfo.Environment[key] = value;
        }

        using Process process = new()
        {
            StartInfo = startInfo,
        };

        DateTimeOffset startedAtUtc = timeProvider.GetUtcNow();
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{resolvedFileName}'.");
        }

        if (spec.StandardInputText is not null)
        {
            await process.StandardInput.WriteAsync(spec.StandardInputText.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }

        Task<string> stdoutTask = PumpAsync(process.StandardOutput, spec.StdoutPath, cancellationToken);
        Task<string> stderrTask = PumpAsync(process.StandardError, spec.StderrPath, cancellationToken);

        bool timedOut = false;

        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(spec.Timeout);
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        DateTimeOffset completedAtUtc = timeProvider.GetUtcNow();

        return new ExternalProcessResult
        {
            FileName = spec.FileName,
            Arguments = spec.Arguments,
            WorkingDirectory = spec.WorkingDirectory,
            EnvironmentVariableNames = spec.EnvironmentVariables.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
            Timeout = spec.Timeout,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            ExitCode = process.ExitCode,
            TimedOut = timedOut,
            Stdout = stdout,
            Stderr = stderr,
            StdoutPath = spec.StdoutPath,
            StderrPath = spec.StderrPath,
            ProcessId = process.Id,
        };
    }

    private static string ResolveFileName(string fileName, string workingDirectory)
    {
        if (Path.IsPathRooted(fileName))
        {
            return fileName;
        }

        bool containsDirectorySeparator = fileName.Contains(Path.DirectorySeparatorChar)
            || fileName.Contains(Path.AltDirectorySeparatorChar);

        return containsDirectorySeparator
            ? Path.GetFullPath(Path.Combine(workingDirectory, fileName))
            : fileName;
    }

    private static async Task<string> PumpAsync(StreamReader reader, string? outputPath, CancellationToken cancellationToken)
    {
        StringBuilder buffer = new();
        StreamWriter? writer = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                string directory = Path.GetDirectoryName(outputPath)
                    ?? throw new InvalidOperationException($"Unable to determine the directory for '{outputPath}'.");
                Directory.CreateDirectory(directory);
                writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            char[] chunk = new char[4096];
            while (true)
            {
                int read = await reader.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                buffer.Append(chunk, 0, read);

                if (writer is not null)
                {
                    await writer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                    await writer.FlushAsync(cancellationToken);
                }
            }

            return buffer.ToString();
        }
        finally
        {
            if (writer is not null)
            {
                await writer.DisposeAsync();
            }
        }
    }
}
