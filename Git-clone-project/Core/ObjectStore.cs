using System.Text;
using System.Text.Json;
using GitClone.Models;

namespace GitClone.Core;

public sealed class ObjectStore : IAsyncDisposable
{
    private readonly string _objectsPath;
    private readonly Dictionary<string, GitObject> _cache = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        IncludeFields = false
    };

    public ObjectStore(string gitDir)
    {
        _objectsPath = Path.Combine(gitDir, "objects");
        Directory.CreateDirectory(_objectsPath);

        // Load existing objects from disk
        LoadExistingObjects();
    }

    private void LoadExistingObjects()
    {
        if (!Directory.Exists(_objectsPath))
            return;

        foreach (var dir in Directory.GetDirectories(_objectsPath))
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                try
                {
                    var data = File.ReadAllBytes(file);
                    var obj = DeserializeObject(data);
                    if (obj != null && !string.IsNullOrEmpty(obj.Hash))
                    {
                        _cache[obj.Hash] = obj;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading object from {file}: {ex.Message}");
                }
            }
        }
    }

    public void StoreObject(GitObject obj)
    {
        if (string.IsNullOrEmpty(obj.Hash) || obj.Hash == "empty" || obj.Hash == "error")
        {
            Console.WriteLine($"Warning: Not storing object with invalid hash: {obj.Hash}");
            return;
        }

        if (_cache.ContainsKey(obj.Hash))
            return;

        _cache[obj.Hash] = obj;

        // Save to disk
        var objDir = Path.Combine(_objectsPath, obj.Hash[..2]);
        var objFile = Path.Combine(objDir, obj.Hash[2..]);

        if (!Directory.Exists(objDir))
            Directory.CreateDirectory(objDir);

        var data = SerializeObject(obj);
        File.WriteAllBytes(objFile, data);
    }

    public async Task StoreObjectAsync(GitObject obj)
    {
        if (_cache.ContainsKey(obj.Hash))
            return;

        _cache[obj.Hash] = obj;

        var objDir = Path.Combine(_objectsPath, obj.Hash[..2]);
        var objFile = Path.Combine(objDir, obj.Hash[2..]);

        if (!Directory.Exists(objDir))
            Directory.CreateDirectory(objDir);

        var data = SerializeObject(obj);
        await File.WriteAllBytesAsync(objFile, data);
    }

    public void Flush()
    {
        // Force all cached objects to be written to disk
        foreach (var obj in _cache)
        {
            if (string.IsNullOrEmpty(obj.Key) || obj.Key == "empty" || obj.Key == "error")
                continue;

            var objDir = Path.Combine(_objectsPath, obj.Key[..2]);
            var objFile = Path.Combine(objDir, obj.Key[2..]);

            if (!File.Exists(objFile))
            {
                try
                {
                    var data = SerializeObject(obj.Value);
                    Directory.CreateDirectory(objDir);
                    File.WriteAllBytes(objFile, data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not flush object {obj.Key}: {ex.Message}");
                }
            }
        }
    }

    public GitObject? GetObject(string hash)
    {
        if (_cache.TryGetValue(hash, out var cached))
            return cached;

        // Try to load from disk
        var objPath = Path.Combine(_objectsPath, hash[..2], hash[2..]);
        if (File.Exists(objPath))
        {
            var data = File.ReadAllBytes(objPath);
            var obj = DeserializeObject(data);
            if (obj is not null)
            {
                _cache[hash] = obj;
                return obj;
            }
        }

        return null;
    }

    public async Task<GitObject?> GetObjectAsync(string hash)
    {
        if (_cache.TryGetValue(hash, out var cached))
            return cached;

        var objPath = Path.Combine(_objectsPath, hash[..2], hash[2..]);
        if (File.Exists(objPath))
        {
            var data = await File.ReadAllBytesAsync(objPath);
            var obj = DeserializeObject(data);
            if (obj is not null)
                _cache[hash] = obj;
            return obj;
        }

        return null;
    }

    public bool ObjectExists(string hash)
    {
        return _cache.ContainsKey(hash) ||
               File.Exists(Path.Combine(_objectsPath, hash[..2], hash[2..]));
    }

    private byte[] SerializeObject(GitObject obj)
    {
        object serializable = obj switch
        {
            Blob blob => new { blob.Type, blob.Hash, blob.Data },
            Tree tree => new { tree.Type, tree.Hash, Entries = tree.Entries.Select(e => new { e.Mode, e.Name, e.Hash, e.Type }) },
            Commit commit => new { commit.Type, commit.Hash, commit.TreeHash, commit.ParentHash, commit.Author, commit.Message, commit.Timestamp },
            _ => obj
        };

        var json = JsonSerializer.Serialize(serializable, _jsonOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    private GitObject? DeserializeObject(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("Type").GetString();

        return type switch
        {
            "blob" => JsonSerializer.Deserialize<Blob>(json, _jsonOptions),
            "tree" => DeserializeTree(json),
            "commit" => JsonSerializer.Deserialize<Commit>(json, _jsonOptions),
            _ => throw new InvalidOperationException($"Unknown object type: {type}")
        };
    }

    private Tree? DeserializeTree(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tree = new Tree
        {
            Hash = root.GetProperty("Hash").GetString() ?? string.Empty,
            Type = root.GetProperty("Type").GetString() ?? string.Empty
        };

        if (root.TryGetProperty("Entries", out var entriesElement))
        {
            foreach (var entryElement in entriesElement.EnumerateArray())
            {
                tree.Entries.Add(new TreeEntry
                {
                    Mode = entryElement.GetProperty("Mode").GetString() ?? string.Empty,
                    Name = entryElement.GetProperty("Name").GetString() ?? string.Empty,
                    Hash = entryElement.GetProperty("Hash").GetString() ?? string.Empty,
                    Type = entryElement.GetProperty("Type").GetString() ?? string.Empty
                });
            }
        }

        return tree;
    }

    public void ClearCache() => _cache.Clear();

    public async ValueTask DisposeAsync()
    {
        ClearCache();
        await Task.CompletedTask;
    }
}