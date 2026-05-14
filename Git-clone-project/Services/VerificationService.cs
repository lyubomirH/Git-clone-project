using GitClone.Core;
using GitClone.Models;

namespace GitClone.Services;

public sealed class VerificationService
{
    private readonly ObjectStore _objectStore;

    public VerificationService(ObjectStore objectStore)
    {
        _objectStore = objectStore;
    }

    public bool VerifyObject(string hash)
    {
        var obj = _objectStore.GetObject(hash);
        if (obj is null)
            return false;

        var computedHash = obj.ComputeHash();
        var isValid = computedHash == hash;

        return isValid;
    }

    public async Task<bool> VerifyObjectAsync(string hash)
    {
        var obj = await _objectStore.GetObjectAsync(hash);
        if (obj is null)
            return false;

        var computedHash = obj.ComputeHash();
        var isValid = computedHash == hash;

        return isValid;
    }

    public bool VerifyTree(string treeHash)
    {
        var stack = new Stack<string>();
        stack.Push(treeHash);

        while (stack.Count > 0)
        {
            var currentHash = stack.Pop();
            var tree = _objectStore.GetObject(currentHash) as Tree;

            if (tree is null)
                return false;

            if (!VerifyObject(currentHash))
                return false;

            foreach (var entry in tree.Entries)
            {
                if (entry.Type == "tree")
                {
                    stack.Push(entry.Hash);
                }
                else if (entry.Type == "blob")
                {
                    if (!VerifyObject(entry.Hash))
                        return false;
                }
            }
        }

        return true;
    }

    public async Task<bool> VerifyTreeAsync(string treeHash)
    {
        var stack = new Stack<string>();
        stack.Push(treeHash);

        while (stack.Count > 0)
        {
            var currentHash = stack.Pop();
            var tree = await _objectStore.GetObjectAsync(currentHash) as Tree;

            if (tree is null)
                return false;

            if (!await VerifyObjectAsync(currentHash))
                return false;

            foreach (var entry in tree.Entries)
            {
                if (entry.Type == "tree")
                {
                    stack.Push(entry.Hash);
                }
                else if (entry.Type == "blob")
                {
                    if (!await VerifyObjectAsync(entry.Hash))
                        return false;
                }
            }
        }

        return true;
    }

    public bool VerifyCommit(string commitHash)
    {
        var hash = commitHash;

        while (!string.IsNullOrEmpty(hash))
        {
            var commit = _objectStore.GetObject(hash) as Commit;
            if (commit == null)
                return false;

            if (!VerifyObject(hash))
                return false;

            if (!VerifyTree(commit.TreeHash))
                return false;

            hash = commit.ParentHash ?? string.Empty;
        }

        return true;
    }

    public async Task<bool> VerifyCommitAsync(string commitHash)
    {
        var hash = commitHash;

        while (!string.IsNullOrEmpty(hash))
        {
            var commit = await _objectStore.GetObjectAsync(hash) as Commit;
            if (commit == null)
                return false;

            if (!await VerifyObjectAsync(hash))
                return false;

            if (!await VerifyTreeAsync(commit.TreeHash))
                return false;

            hash = commit.ParentHash ?? string.Empty;
        }

        return true;
    }
}