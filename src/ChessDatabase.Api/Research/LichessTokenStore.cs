using System.Security.Cryptography;
using System.Text;
using ChessDatabase.Api.Collection;

namespace ChessDatabase.Api.Research;

/// <summary>
/// The Owner's optional Lichess personal API token. Kept out of the Collection (so backups never
/// carry it) in a file beside it, encrypted for the current Windows user (DPAPI): another account or
/// PC cannot read it. Never sent to the UI.
/// </summary>
public sealed class LichessTokenStore(IConfiguration configuration)
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("ChessDatabase Lichess token");
    private readonly Lock _lock = new();

    /// <summary>The file: Lichess:TokenPath, or "lichess-token.bin" beside the Collection's database.</summary>
    public string Path => configuration["Lichess:TokenPath"] is { Length: > 0 } path
        ? System.IO.Path.GetFullPath(path)
        : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(CollectionSetup.DatabasePath(configuration))!, "lichess-token.bin");

    public bool HasToken => Read() is not null;

    /// <summary>The token, or null if none is stored or it can't be read (e.g. copied from another account).</summary>
    public string? Read()
    {
        lock (_lock)
        {
            if (!File.Exists(Path)) return null;
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path), Purpose, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
    }

    /// <summary>Stores the token; an empty one removes it.</summary>
    public void Save(string? token)
    {
        token = token?.Trim();
        lock (_lock)
        {
            if (string.IsNullOrEmpty(token))
            {
                File.Delete(Path);
                return;
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllBytes(Path, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Purpose, DataProtectionScope.CurrentUser));
        }
    }
}
