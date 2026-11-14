using System.Security.Cryptography;

namespace SprocketExpedition.Core.Persistence;

internal static class RunStoreNames
{
    public const int SchemaVersion = 1;

    public const string RunsDirectory = "runs";
    public const string BlueprintsDirectory = "blueprints";
    public const string CheckpointFileName = "checkpoint.json";
    public const string PreviousCheckpointFileName = "checkpoint.previous.json";
    public const string ActiveIndexFileName = "active-run.json";
    public const string RecordFileName = "record.json";
    public const string BlueprintExtension = ".bin";
    public const string TempPrefix = ".tmp-";

    public const string CheckpointKind = "runCheckpoint";
    public const string ActiveIndexKind = "activeRunIndex";
    public const string RecordKind = "runRecord";

    public const int MaxCheckpointBytes = 32 * 1024 * 1024;
    public const long MaxBlueprintBytes = 128L * 1024 * 1024;
    public const int MaxJsonDepth = 64;

    public static string GetRunsDirectory(string root) => Path.Combine(root, RunsDirectory);

    public static string GetRunDirectory(string root, string canonicalRunId) =>
        Path.Combine(GetRunsDirectory(root), canonicalRunId);

    public static string GetBlueprintsDirectory(string runDirectory) =>
        Path.Combine(runDirectory, BlueprintsDirectory);

    public static string GetCheckpointPath(string runDirectory) =>
        Path.Combine(runDirectory, CheckpointFileName);

    public static string GetPreviousCheckpointPath(string runDirectory) =>
        Path.Combine(runDirectory, PreviousCheckpointFileName);

    public static string GetRecordPath(string runDirectory) =>
        Path.Combine(runDirectory, RecordFileName);

    public static string GetBlueprintPath(string runDirectory, string normalizedHash) =>
        Path.Combine(GetBlueprintsDirectory(runDirectory), normalizedHash + BlueprintExtension);
}

internal static class RunStoreIds
{
    // Run ids become directory names, so only a canonical GUID ("D", lowercase) is
    // ever used for a path. The original string is preserved inside the snapshot.
    public static bool TryCanonicalize(string? value, out string canonical, out string? error)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Run id is missing.";
            return false;
        }

        if (!Guid.TryParse(value, out var guid))
        {
            error = $"Run id '{value}' is not a GUID.";
            return false;
        }

        canonical = guid.ToString("D");
        error = null;
        return true;
    }

    public static bool TryNormalizeHash(string? value, out string normalized, out string? error)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(value) || value.Length != 64)
        {
            error = "SHA-256 hash must be 64 hexadecimal characters.";
            return false;
        }

        Span<char> buffer = stackalloc char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isHex = (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F');
            if (!isHex)
            {
                error = "SHA-256 hash contains a non-hexadecimal character.";
                return false;
            }

            buffer[index] = char.ToLowerInvariant(character);
        }

        normalized = new string(buffer);
        error = null;
        return true;
    }
}

internal static class Sha256Hex
{
    public static string Compute(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(bytes, digest);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}

internal static class AtomicFile
{
    public static string NewTempPath(string directory) =>
        Path.Combine(directory, RunStoreNames.TempPrefix + Guid.NewGuid().ToString("N"));

    public static void WriteDurable(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    // Rename within one volume. The caller has already made the temp file durable
    // and verified its exact bytes.
    public static void MoveReplace(string tempPath, string targetPath) =>
        File.Move(tempPath, targetPath, overwrite: true);

    public static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover temp file is inert and ignored by readers.
        }
        catch (UnauthorizedAccessException)
        {
            // A leftover temp file is inert and ignored by readers.
        }
    }
}
