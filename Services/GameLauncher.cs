using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Lutris.Models;
using Microsoft.UI.Dispatching;

namespace Lutris.Services;

public readonly record struct LaunchResult(bool Ok, string? Error)
{
    public static LaunchResult Success => new(true, null);
    public static LaunchResult Fail(string error) => new(false, error);
}

/// <summary>
/// Starts games and reports when they exit so play time can be recorded.
/// Events are raised on the UI thread.
/// </summary>
public sealed class GameLauncher
{
    private readonly DispatcherQueue _dispatcher;
    private readonly HashSet<long> _running = new();

    public GameLauncher(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public event Action<long>? GameStarted;

    public event Action<long, TimeSpan>? GameExited;

    public bool IsRunning(long gameId) => _running.Contains(gameId);

    public LaunchResult Launch(Game game)
    {
        if (!File.Exists(game.ExePath))
        {
            return LaunchResult.Fail($"The executable was not found: {game.ExePath}");
        }

        try
        {
            var info = new ProcessStartInfo(game.ExePath)
            {
                WorkingDirectory = Path.GetDirectoryName(game.ExePath) ?? string.Empty,
                UseShellExecute = true,
            };
            var process = Process.Start(info);
            if (process is null)
            {
                // Started through a shortcut or an already-running host; nothing to track.
                return LaunchResult.Success;
            }

            var startedAt = DateTimeOffset.UtcNow;
            _running.Add(game.Id);
            GameStarted?.Invoke(game.Id);

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                var played = DateTimeOffset.UtcNow - startedAt;
                process.Dispose();
                _dispatcher.TryEnqueue(() =>
                {
                    _running.Remove(game.Id);
                    GameExited?.Invoke(game.Id, played);
                });
            };
            return LaunchResult.Success;
        }
        catch (Win32Exception ex)
        {
            return LaunchResult.Fail(ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return LaunchResult.Fail(ex.Message);
        }
    }
}
