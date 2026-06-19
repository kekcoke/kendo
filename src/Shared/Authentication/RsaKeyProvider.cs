using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Kendo.Shared.Authentication;

/// <summary>
/// Manages RSA-2048 keypair for JWT signing and verification.
/// Generates keypair on first boot, persists to disk, and supports 90-day rotation.
/// </summary>
public class RsaKeyProvider
{
    private readonly string _privateKeyPath;
    private readonly int _rotationDays;
    private RsaSecurityKey? _currentSigningKey;
    private RsaSecurityKey? _currentPublicKey;
    private string? _currentKeyId;
    private DateTime _lastRotation;
    private readonly object _lock = new();

    public RsaKeyProvider(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
    {
        var jwtOptions = options.Value;
        _privateKeyPath = jwtOptions.PrivateKeyPath;
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

    public void RotateKey()
    {
        lock (_lock)
        {
            GenerateNewKeypair();
            PersistPrivateKey();
            _lastRotation = DateTime.UtcNow;
        }
    }

    private void LoadOrGenerateKeypair()
    {
        if (File.Exists(_privateKeyPath))
        {
            LoadExistingKeypair();
        }
        else
        {
            GenerateNewKeypair();
            PersistPrivateKey();
            _lastRotation = DateTime.UtcNow;
        }
    }

    private void LoadExistingKeypair()
    {
        var pem = File.ReadAllText(_privateKeyPath);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        InitializeFromRsa(rsa);
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
            File.WriteAllText(_privateKeyPath, pem);
        }
    }
}
