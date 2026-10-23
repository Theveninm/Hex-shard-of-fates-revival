using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HexRevival.Server;

public sealed class Account
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>
    /// Base64 key derived by the client: PBKDF2-HMAC-SHA1(password, salt = player id, Accounts.Iterations, 32 bytes).
    /// The password itself never leaves the player's PC; logins prove the key with an HMAC of a random nonce.
    /// </summary>
    public string Key { get; set; } = "";
    public DateTime Created { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>Accounts kept in one JSON file (enough for a community server; swap for SQLite if it grows).</summary>
public sealed partial class Accounts
{
    /// <summary>Must match the client. SHA1 because the game's .NET 3.5 only offers PBKDF2 with SHA1.</summary>
    public const int Iterations = 20000;

    private readonly string _path;
    private readonly Dictionary<string, Account> _byId = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public Accounts(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "accounts.json");
        if (File.Exists(_path))
            foreach (var a in JsonSerializer.Deserialize<List<Account>>(File.ReadAllText(_path)) ?? [])
                _byId[a.Id] = a;
    }

    public int Count { get { lock (_lock) return _byId.Count; } }

    [GeneratedRegex("^[0-9a-f]{32}$")] private static partial Regex IdPattern();
    [GeneratedRegex(@"^[\p{L}\p{N}_\-]+( [\p{L}\p{N}_\-]+)*$")] private static partial Regex NamePattern();

    public static bool ValidId(string? id) => id != null && IdPattern().IsMatch(id);

    /// <summary>3 to 20 letters, digits, '_' or '-', single spaces between words.</summary>
    public static bool ValidName(string? name) => name != null && name.Length is >= 3 and <= 20 && NamePattern().IsMatch(name);

    public Account? Find(string id) { lock (_lock) return _byId.GetValueOrDefault(id); }

    private bool NameTaken(string name, string exceptId) =>
        _byId.Values.Any(a => a.Id != exceptId && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Null on success, otherwise an error code for the client.</summary>
    public string? Register(string id, string name, byte[] key)
    {
        lock (_lock)
        {
            if (_byId.ContainsKey(id)) return "id_taken";
            if (NameTaken(name, id)) return "name_taken";
            var now = DateTime.UtcNow;
            _byId[id] = new Account { Id = id, Name = name, Key = Convert.ToBase64String(key), Created = now, LastSeen = now };
            Save();
            return null;
        }
    }

    /// <summary>The login proof is HMAC-SHA256(key, nonce); compared in constant time.</summary>
    public static bool CheckProof(Account a, byte[] nonce, byte[] proof)
    {
        byte[] expected = HMACSHA256.HashData(Convert.FromBase64String(a.Key), nonce);
        return CryptographicOperations.FixedTimeEquals(expected, proof);
    }

    /// <summary>Records the login and applies a new name when it is free. Returns false when the name is taken.</summary>
    public bool Touch(Account a, string? newName)
    {
        lock (_lock)
        {
            bool renamed = true;
            if (newName != null && newName != a.Name)
            {
                if (NameTaken(newName, a.Id)) renamed = false;
                else a.Name = newName;
            }
            a.LastSeen = DateTime.UtcNow;
            Save();
            return renamed;
        }
    }

    private void Save()
    {
        // Write then rename, so a crash never leaves a half-written file.
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_byId.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}
