using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Kendo.Shared.Authentication;

/// <summary>
/// Manages RSA-2048 keypair for JWT signing and verification.
/// Supports two-key overlap during rotation — old key remains valid
/// until the overlap window expires. State persisted to disk as
/// jwt-meta.json alongside the private key PEM.
/// </summary>
public class RsaKeyProvider
{
    private readonly string _privateKeyPath;
    private readonly string _metaPath;
    private readonly int _rotationDays;
    private RsaSecurityKey? _currentSigningKey;
    private RsaSecurityKey? _currentPublicKey;
    private string? _currentKeyId;
    private RsaSecurityKey? _previousPublicKey;
    private string? _previousKeyId;
    private DateTime? _previousKeyExpiresAt;
    private DateTime _lastRotation;
    private readonly object _lock = new();

    private const int OverlapDays = 7; // Old key remains valid for 7 days post-rotation

    public RsaKeyProvider(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
    {
        var jwtOptions = options.Value;
        _privateKeyPath = jwtOptions.PrivateKeyPath;
        _metaPath = Path.ChangeExtension(_privateKeyPath, ".meta.json");
        _rotationDays = jwtOptions.RotationDays;
        _lastRotation = DateTime.MinValue;

        LoadOrGenerateKeypair();
    }

    public RsaSecurityKey GetCurrentSigningKey()
    {
        lock (_lock)
        {
            return _currentSigningKey!;
        }
    }

    public RsaSecurityKey GetPublicKey()
    {
        lock (_lock)
        {
            return _currentPublicKey!;
        }
    }

    public string CurrentKeyId()
    {
        lock (_lock)
        {
            return _currentKeyId!;
        }
    }

    public DateTime LastRotation()
    {
        lock (_lock)
        {
            return _lastRotation;
        }
    }

    public bool ShouldRotate()
    {
        lock (_lock)
        {
            return (DateTime.UtcNow - _lastRotation).TotalDays >= _rotationDays;
        }
    }

    /// <summary>
    /// Returns all currently valid public keys (current + previous within overlap window).
    /// </summary>
    public List<(string KeyId, RsaSecurityKey Key)> GetAllValidPublicKeys()
    {
        lock (_lock)
        {
            var keys = new List<(string, RsaSecurityKey)>
            {
                (_currentKeyId!, _currentPublicKey!)
            };

            if (_previousPublicKey != null &&
                _previousKeyExpiresAt.HasValue &&
                DateTime.UtcNow < _previousKeyExpiresAt.Value)
            {
                keys.Add((_previousKeyId!, _previousPublicKey));
            }

            return keys;
        }
    }

    public void RotateKey()
    {
        lock (_lock)
        {
            // Keep current key as previous before generating new one
            if (_currentSigningKey != null)
            {
                _previousPublicKey = _currentPublicKey;
                _previousKeyId = _currentKeyId;
                _previousKeyExpiresAt = DateTime.UtcNow.AddDays(OverlapDays);
            }

            GenerateNewKeypair();
            PersistPrivateKey();
            PersistMeta();
            _lastRotation = DateTime.UtcNow;
        }
    }

    private void LoadOrGenerateKeypair()
    {
        if (File.Exists(_privateKeyPath))
        {
            LoadExistingKeypair();
            LoadMeta();
        }
        else
        {
            GenerateNewKeypair();
            PersistPrivateKey();
            PersistMeta();
            _lastRotation = DateTime.UtcNow;
        }
    }

    private void LoadExistingKeypair()
    {
        var pem = File.ReadAllText(_privateKeyPath);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        InitializeFromRsa(rsa);
        // Use file write time as last rotation timestamp
        _lastRotation = File.GetLastWriteTimeUtc(_privateKeyPath);
    }

    private void LoadMeta()
    {
        if (!File.Exists(_metaPath)) return;

        try
        {
            var json = File.ReadAllText(_metaPath);
            var meta = JsonSerializer.Deserialize<KeyMeta>(json);
            if (meta == null) return;

            _lastRotation = meta.LastRotation;

            // If meta has a previous key ID but no previous public key was loaded,
            // we can't reconstruct it from the PEM (only latest is persisted).
            // This means the overlap window only works within the same process lifetime
            // or across restarts where rotation happened in a prior session.
            if (!string.IsNullOrEmpty(meta.PreviousKeyId) &&
                meta.PreviousKeyExpiresAt.HasValue &&
                DateTime.UtcNow < meta.PreviousKeyExpiresAt.Value)
            {
                // Attempt to load previous key from backup file
                var prevPath = Path.ChangeExtension(_privateKeyPath, ".previous.pem");
                if (File.Exists(prevPath))
                {
                    try
                    {
                        var prevPem = File.ReadAllText(prevPath);
                        var prevRsa = RSA.Create();
                        prevRsa.ImportFromPem(prevPem);
                        _previousKeyId = meta.PreviousKeyId;
                        _previousKeyExpiresAt = meta.PreviousKeyExpiresAt;
                        var publicRsa = RSA.Create();
                        publicRsa.ImportRSAPublicKey(prevRsa.ExportRSAPublicKey(), out _);
                        _previousPublicKey = new RsaSecurityKey(publicRsa) { KeyId = _previousKeyId };
                    }
                    catch
                    {
                        // Previous key file corrupt or missing — safe to ignore
                        _previousKeyId = null;
                        _previousKeyExpiresAt = null;
                        _previousPublicKey = null;
                    }
                }
            }
        }
        catch
        {
            // Meta file corrupt — safe to ignore; rotation will still work
        }
    }

    private void GenerateNewKeypair()
    {
        var rsa = RSA.Create(2048);
        InitializeFromRsa(rsa);
    }

    private void InitializeFromRsa(RSA rsa)
    {
        _currentKeyId = Guid.NewGuid().ToString("N")[..8];
        _currentSigningKey = new RsaSecurityKey(rsa) { KeyId = _currentKeyId };
        // Export public key only for the public key reference
        var publicRsa = RSA.Create();
        publicRsa.ImportRSAPublicKey(rsa.ExportRSAPublicKey(), out _);
        _currentPublicKey = new RsaSecurityKey(publicRsa) { KeyId = _currentKeyId };
    }

    private void PersistPrivateKey()
    {
        var dir = Path.GetDirectoryName(_privateKeyPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var rsa = _currentSigningKey?.Rsa;
        if (rsa != null)
        {
            var pem = rsa.ExportRSAPrivateKeyPem();
            // If there's an existing key, back it up before overwriting
            if (File.Exists(_privateKeyPath))
            {
                var backupPath = Path.ChangeExtension(_privateKeyPath, ".previous.pem");
                File.Copy(_privateKeyPath, backupPath, overwrite: true);
            }
            File.WriteAllText(_privateKeyPath, pem);
        }
    }

    private void PersistMeta()
    {
        var dir = Path.GetDirectoryName(_metaPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var meta = new KeyMeta
        {
            LastRotation = _lastRotation,
            CurrentKeyId = _currentKeyId!,
            PreviousKeyId = _previousKeyId,
            PreviousKeyExpiresAt = _previousKeyExpiresAt
        };

        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_metaPath, json);
    }

    private class KeyMeta
    {
        public DateTime LastRotation { get; set; }
        public string CurrentKeyId { get; set; } = string.Empty;
        public string? PreviousKeyId { get; set; }
        public DateTime? PreviousKeyExpiresAt { get; set; }
    }
}
