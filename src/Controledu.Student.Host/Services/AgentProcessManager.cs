using Controledu.Student.Host.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Controledu.Student.Host.Services;

/// <summary>
/// Launches and monitors the student background agent process.
/// </summary>
public interface IAgentProcessManager
{
    /// <summary>
    /// True when managed process is running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts agent process if not running.
    /// </summary>
    Task<bool> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops managed agent process if running.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

internal sealed class AgentProcessManager(
    IOptions<StudentHostOptions> options,
    ILogger<AgentProcessManager> logger) : IAgentProcessManager, IDisposable
{
    private readonly object _sync = new();
    private readonly TimeSpan _restartDelay = TimeSpan.FromSeconds(2);
    private Process? _process;
    private bool _autoRestart = true;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _autoRestart = true;

            if (_process is { HasExited: false })
            {
                return Task.FromResult(true);
            }

            var resolved = ResolveAgentLaunchInfo();
            if (resolved is null)
            {
                return Task.FromResult(false);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = resolved.Value.FileName,
                Arguments = resolved.Value.Arguments,
                WorkingDirectory = resolved.Value.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                _process = Process.Start(startInfo);
                if (_process is null)
                {
                    logger.LogWarning("Failed to start Student.Agent process");
                    return Task.FromResult(false);
                }

                _process.EnableRaisingEvents = true;
                _process.Exited += OnProcessExited;

                logger.LogInformation("Student.Agent started using {FileName} {Arguments}", startInfo.FileName, startInfo.Arguments);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to launch Student.Agent");
                _process = null;
                return Task.FromResult(false);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process;

        lock (_sync)
        {
            _autoRestart = false;
            process = _process;
            _process = null;
        }

        if (process is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            process.Exited -= OnProcessExited;
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to stop Student.Agent process");
        }
        finally
        {
            process.Dispose();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Process? process;
        lock (_sync)
        {
            _autoRestart = false;
            process = _process;
            _process = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            process.Exited -= OnProcessExited;
            process.Dispose();
        }
        catch
        {
            // Ignore disposal errors on application exit.
        }
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        var exited = sender as Process;
        var code = -1;
        if (exited is not null)
        {
            try
            {
                code = exited.ExitCode;
            }
            catch
            {
                code = -1;
            }
        }

        bool shouldRestart;
        lock (_sync)
        {
            if (ReferenceEquals(_process, exited))
            {
                _process = null;
            }

            shouldRestart = _autoRestart;
        }

        logger.LogInformation("Student.Agent process exited with code {Code}", code);

        try
        {
            exited?.Dispose();
        }
        catch
        {
            // Ignore dispose errors for exited process handle.
        }

        if (!shouldRestart)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_restartDelay);
                await StartAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to restart Student.Agent after unexpected exit");
            }
        });
    }

    private (string FileName, string Arguments, string WorkingDirectory)? ResolveAgentLaunchInfo()
    {
        var configured = options.Value.AgentExecutablePath;
        var absolute = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);

        if (File.Exists(absolute))
        {
            return (absolute, string.Empty, Path.GetDirectoryName(absolute) ?? AppContext.BaseDirectory);
        }

        var maybeDll = Path.ChangeExtension(absolute, ".dll");
        if (File.Exists(maybeDll))
        {
            return ("dotnet", $"\"{maybeDll}\"", Path.GetDirectoryName(maybeDll) ?? AppContext.BaseDirectory);
        }

        logger.LogWarning("Student.Agent executable was not found. Checked: {Exe} and {Dll}", absolute, maybeDll);
        return null;
    }
}
