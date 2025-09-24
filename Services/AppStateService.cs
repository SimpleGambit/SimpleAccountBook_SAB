using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SimpleAccountBook.Models;

namespace SimpleAccountBook.Services;

public class AppStateService
{
    private readonly string _stateDirectory;
    private readonly string _stateFilePath;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppStateService()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        _stateDirectory = Path.Combine(baseDirectory, "Save");
        Directory.CreateDirectory(_stateDirectory);
        _stateFilePath = Path.Combine(_stateDirectory, "default.sab");
        _serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public string DefaultSaveDirectory => _stateDirectory;

    public string DefaultStateFilePath => _stateFilePath;

    public async Task SaveAsync(AppState state, string? filePath = null)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var targetPath = PrepareTargetPath(filePath);
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var json = JsonSerializer.Serialize(state, _serializerOptions);
            await File.WriteAllTextAsync(targetPath, json).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[AppStateService] 상태 저장 실패: {ex}");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppState?> LoadAsync(string? filePath = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var targetPath = PrepareTargetPath(filePath);

            if (!File.Exists(targetPath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(targetPath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AppState>(json, _serializerOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"[AppStateService] 상태 불러오기 실패: {ex}");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool HasSavedState => File.Exists(_stateFilePath);
    private string PrepareTargetPath(string? filePath)
    {
        var targetPath = string.IsNullOrWhiteSpace(filePath) ? _stateFilePath : filePath;
        if (!string.Equals(Path.GetExtension(targetPath), ".sab", StringComparison.OrdinalIgnoreCase))
        {
            targetPath = Path.ChangeExtension(targetPath, ".sab");
        }

        return targetPath;
    }
}