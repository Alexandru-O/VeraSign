using Android.Content.PM;
using Android.Runtime;
using Android.Security.Keystore;
using Java.Security;
using Java.Security.Spec;
using Microsoft.IdentityModel.Tokens;
using MasterSTI.Wallet.Services;
#if !DEBUG
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;
#endif

namespace MasterSTI.Wallet.Platforms.Android.Services;

/// <summary>
/// AndroidKeyStore-backed EC P-256 device key service.
/// The private key never leaves the secure hardware boundary.
/// </summary>
internal sealed class AndroidDeviceKeyService : IDeviceKeyService
{
    private const string KeyAlias = "mastersti.wallet.signing";
    private const string KeystoreProvider = "AndroidKeyStore";
    private const string SignatureAlgorithm = "SHA256withECDSA";
    private const string KeyAlgorithm = "EC";

    // eIDAS Art. 26 / ARF §6.5.3 "sole control" — Release builds must gate every
    // signing operation on a fresh biometric. Debug builds keep the gate off so
    // emulators without enrolled fingerprints stay ergonomic for local dev.
#if DEBUG
    private const bool RequireUserAuthentication = false;
#else
    private const bool RequireUserAuthentication = true;
#endif

    // --- IDeviceKeyService ---------------------------------------------------

    public Task<JsonWebKey> GenerateOrLoadPublicJwkAsync()
    {
        EnsureKeyExists();

        var ks = LoadKeyStore();
        var cert = ks.GetCertificate(KeyAlias)
                   ?? throw new InvalidOperationException("Certificate not found after key generation.");

        // AndroidKeyStore returns an AndroidKeyStoreECPublicKey whose interface map
        // isn't surfaced through plain C# `as` — Mono.Android needs JavaCast<> to
        // resolve the cross-Java interface at the JNI layer.
        var publicKey = cert.PublicKey
                       ?? throw new InvalidOperationException("Certificate has no public key.");
        if (publicKey.Algorithm != "EC")
            throw new InvalidOperationException($"Key in keystore is not EC (algorithm={publicKey.Algorithm}).");
        var ecPublicKey = publicKey.JavaCast<Java.Security.Interfaces.IECPublicKey>()
                         ?? throw new InvalidOperationException("EC public key JavaCast returned null.");

        var point = ecPublicKey.GetW()
                   ?? throw new InvalidOperationException("EC public key W point is null.");

        var affineX = point.AffineX ?? throw new InvalidOperationException("EC point AffineX is null.");
        var affineY = point.AffineY ?? throw new InvalidOperationException("EC point AffineY is null.");
        // BigInteger.ToByteArray() returns byte[]? in the Mono.Android binding but is never null for a valid key.
        var xBytes = affineX.ToByteArray() ?? throw new InvalidOperationException("AffineX.ToByteArray() returned null.");
        var yBytes = affineY.ToByteArray() ?? throw new InvalidOperationException("AffineY.ToByteArray() returned null.");
        var x = ToBase64Url(PadTo32(xBytes));
        var y = ToBase64Url(PadTo32(yBytes));

        var jwk = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.EllipticCurve,
            Crv = JsonWebKeyECTypes.P256,
            X = x,
            Y = y,
            Use = "sig",
            Alg = SecurityAlgorithms.EcdsaSha256,
            KeyId = ComputeThumbprint(x, y),
        };

        return Task.FromResult(jwk);
    }

    public async Task<byte[]> SignAsync(byte[] data)
    {
        // Biometric gate — no-op in Debug (RequireUserAuthentication=false), real
        // Plugin.Fingerprint prompt in Release. The AndroidKeyStore also enforces
        // sole-control at Signature.initSign(key) time under Release (see
        // SetUserAuthenticationParameters in GenerateKeyPair); the prompt here is
        // the friendly UX layer.
        await EnsureBiometricIfRequiredAsync();

        var ks = LoadKeyStore();
        var hasAlias = ks.ContainsAlias(KeyAlias);

        // ks.GetEntry(...) as KeyStore.PrivateKeyEntry returns the canonical
        // Java entry type; reaching into PrivateKey avoids the Mono.Android
        // JNI interface-cast quirk that makes `ks.GetKey(...) as IPrivateKey`
        // resolve to null even for a freshly-generated key.
        IPrivateKey? privateKey = null;
        try
        {
            var entry = ks.GetEntry(KeyAlias, null) as KeyStore.PrivateKeyEntry;
            privateKey = entry?.PrivateKey;
        }
        catch
        {
            // Fall through to GetKey fallback.
        }

        if (privateKey is null)
        {
            // Fallback: try GetKey directly (works on older Android binding releases).
            try
            {
                privateKey = ks.GetKey(KeyAlias, null) as IPrivateKey;
            }
            catch
            {
                // Fall through to orphan handling.
            }
        }

        if (privateKey is null)
        {
            // Genuine orphan: AndroidKeyStore retained the entry but the key
            // material is gone. Drop the stale alias so the next enrollment
            // regenerates cleanly, then signal the caller to wipe the SD-JWT
            // and force re-enrollment.
            if (hasAlias)
            {
                try { ks.DeleteEntry(KeyAlias); } catch { /* best-effort */ }
            }
            throw new WalletKeyOrphanedException(
                "AndroidKeyStore alias present but private key missing — wallet needs re-enrollment.");
        }

        // Fully qualify to avoid ambiguity with Android.Content.PM.Signature
        var sig = Java.Security.Signature.GetInstance(SignatureAlgorithm)
                 ?? throw new InvalidOperationException($"Signature algorithm '{SignatureAlgorithm}' not available.");

        sig.InitSign(privateKey);
        sig.Update(data);
        var der = sig.Sign()!;

        return DerToRawEcdsaSignature(der);
    }

    public async Task<string> GetKeyIdAsync()
    {
        var jwk = await GenerateOrLoadPublicJwkAsync();
        return jwk.KeyId;
    }

    public Task<bool> IsHardwareBackedAsync()
    {
        var context = global::Android.App.Application.Context;
        var pm = context.PackageManager;

        // FeatureStrongboxKeystore requires API 28; guard with version check to
        // avoid CA1416 warnings at runtime on API 26/27 devices.
        var hasStrongBox = pm is not null &&
                           OperatingSystem.IsAndroidVersionAtLeast(28) &&
                           pm.HasSystemFeature(PackageManager.FeatureStrongboxKeystore);

        return Task.FromResult(hasStrongBox);
    }

    // --- Key management -------------------------------------------------------

    private static void EnsureKeyExists()
    {
        var ks = LoadKeyStore();
        if (ks.ContainsAlias(KeyAlias))
        {
            // Always delete + regenerate. AndroidKeyStore entries can survive app
            // uninstall on some Android versions and end up unusable from the new
            // UID, so we force a fresh keypair every time enrollment runs.
            ks.DeleteEntry(KeyAlias);
        }

        GenerateKeyPair();
    }

    private static void GenerateKeyPair()
    {
        var context = global::Android.App.Application.Context;
        var pm = context.PackageManager;

        // StrongBox requires API 28+
        var hasStrongBox = pm is not null &&
                           OperatingSystem.IsAndroidVersionAtLeast(28) &&
                           pm.HasSystemFeature(PackageManager.FeatureStrongboxKeystore);

        // KeyStorePurpose.Sign is the correct C# enum in .NET Android bindings.
        // The fluent builder methods return KeyGenParameterSpec.Builder? in the binding;
        // null-forgiving operators are correct here — the Android runtime guarantees
        // a non-null builder when the parameters are valid.
        var specBuilder =
            (new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Sign)
                .SetAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))!
                .SetDigests(KeyProperties.DigestSha256)!
                .SetUserAuthenticationRequired(RequireUserAuthentication))
            ?? throw new InvalidOperationException("KeyGenParameterSpec.Builder chain returned null.");

#if !DEBUG
        // Release: per-op biometric gate. timeout=0 + AuthBiometricStrong means the
        // Keystore demands a fresh Class-3 biometric for every Signature.initSign(key)
        // — i.e. exactly the "sole control" property eIDAS Art. 26 requires.
        // setUserAuthenticationParameters is API 30+; the SupportedOSPlatformVersion
        // is 26.0, so guard explicitly to keep CA1416 happy and stay safe on legacy
        // devices that fall back to setUserAuthenticationValidityDurationSeconds(-1)
        // semantics (per-op gate is already the default for -1 on those APIs).
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
#pragma warning disable CA1416
            specBuilder = specBuilder.SetUserAuthenticationParameters(
                timeout: 0,
                type: (int)KeyPropertiesAuthType.BiometricStrong)!;
#pragma warning restore CA1416
        }
#endif

        if (hasStrongBox)
        {
            try
            {
                // SetIsStrongBoxBacked is API 28+; hasStrongBox already gates this block
                // on OperatingSystem.IsAndroidVersionAtLeast(28), but the CA1416 analyser
                // doesn't track that through the bool, so suppress explicitly.
#pragma warning disable CA1416
                specBuilder = specBuilder.SetIsStrongBoxBacked(true)!;
#pragma warning restore CA1416
            }
            catch (StrongBoxUnavailableException)
            {
                // Device advertises StrongBox but it is currently unavailable;
                // fall back to TEE-backed key silently.
            }
        }

        var kpg = KeyPairGenerator.GetInstance(KeyAlgorithm, KeystoreProvider)
                 ?? throw new InvalidOperationException("Cannot get KeyPairGenerator for AndroidKeyStore.");

        kpg.Initialize(specBuilder.Build() ?? throw new InvalidOperationException("KeyGenParameterSpec.Build() returned null."));
        kpg.GenerateKeyPair();
    }

    private static KeyStore LoadKeyStore()
    {
        var ks = KeyStore.GetInstance(KeystoreProvider)
                ?? throw new InvalidOperationException("Cannot get AndroidKeyStore.");
        ks.Load(null);
        return ks;
    }

    // --- Signature conversion: DER → raw r||s (JOSE ES256) -------------------

    /// <summary>
    /// Converts a DER-encoded ECDSA signature (produced by the Android Keystore) to
    /// the raw 64-byte r||s format required by JWS ES256.
    ///
    /// DER layout:  0x30 &lt;len&gt; 0x02 &lt;rLen&gt; &lt;r&gt; 0x02 &lt;sLen&gt; &lt;s&gt;
    /// </summary>
    private static byte[] DerToRawEcdsaSignature(byte[] der)
    {
        var offset = 0;
        if (der[offset++] != 0x30)
            throw new InvalidOperationException("DER signature does not start with SEQUENCE tag.");

        // Skip length field (1 or 2 bytes; long-form starts with 0x80|len)
        if ((der[offset] & 0x80) != 0)
            offset += (der[offset] & 0x7F) + 1;
        else
            offset++;

        var r = ReadDerInteger(der, ref offset);
        var s = ReadDerInteger(der, ref offset);

        var result = new byte[64];
        CopyTrimmedBigEndian(r, result, 0, 32);
        CopyTrimmedBigEndian(s, result, 32, 32);
        return result;
    }

    private static byte[] ReadDerInteger(byte[] buf, ref int offset)
    {
        if (buf[offset++] != 0x02)
            throw new InvalidOperationException("Expected INTEGER tag (0x02) in DER signature.");

        var len = buf[offset++];
        var value = new byte[len];
        Array.Copy(buf, offset, value, 0, len);
        offset += len;
        return value;
    }

    /// <summary>
    /// Copies the big-endian integer value into a fixed-width field, stripping any
    /// leading 0x00 padding byte that DER adds when the high bit is set.
    /// </summary>
    private static void CopyTrimmedBigEndian(byte[] src, byte[] dst, int dstOffset, int fieldSize)
    {
        // DER integers may have a leading 0x00; strip it.
        var start = (src.Length > fieldSize && src[0] == 0x00) ? 1 : 0;
        var length = src.Length - start;
        var copyOffset = dstOffset + (fieldSize - length);
        Array.Copy(src, start, dst, copyOffset, length);
    }

    // --- JWK helpers ----------------------------------------------------------

    /// <summary>
    /// Ensures the byte array represents a 32-byte big-endian integer,
    /// trimming leading zeros or padding with zeros as needed.
    /// </summary>
    private static byte[] PadTo32(byte[] input)
    {
        if (input.Length == 32)
            return input;

        if (input.Length > 32)
        {
            // BigInteger.ToByteArray may prepend a zero sign byte
            var start = input.Length - 32;
            var trimmed = new byte[32];
            Array.Copy(input, start, trimmed, 0, 32);
            return trimmed;
        }

        var padded = new byte[32];
        Array.Copy(input, 0, padded, 32 - input.Length, input.Length);
        return padded;
    }

    private static string ToBase64Url(byte[] bytes) =>
        Base64UrlEncoder.Encode(bytes);

    /// <summary>
    /// Computes a stable key ID using the JWK SHA-256 thumbprint algorithm (RFC 7638).
    /// Lexicographically ordered required members: crv, kty, x, y.
    /// </summary>
    private static string ComputeThumbprint(string x, string y)
    {
        // RFC 7638 canonical JSON: members in lexicographic order, no whitespace
        var json = $@"{{""crv"":""P-256"",""kty"":""EC"",""x"":""{x}"",""y"":""{y}""}}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Base64UrlEncoder.Encode(hash);
    }

    // --- Biometric ------------------------------------------------------------

#if DEBUG
    private static Task EnsureBiometricIfRequiredAsync()
    {
        // Debug: RequireUserAuthentication is false, so the Keystore key was created
        // without setUserAuthenticationRequired(true). No prompt needed — signing
        // works straight through, keeping emulators without enrolled fingerprints usable.
        return Task.CompletedTask;
    }
#else
    private static async Task EnsureBiometricIfRequiredAsync()
    {
        // Release: the Keystore itself enforces a fresh Class-3 biometric at
        // Signature.initSign(key) time (see SetUserAuthenticationParameters above),
        // so omitting the Plugin.Fingerprint prompt would still be safe — the OS
        // would reject the signing attempt. We surface a prompt here purely for UX:
        // it lets us throw a clean UnauthorizedAccessException with a localized
        // message before the Keystore raises a less helpful UserNotAuthenticatedException.
        var request = new AuthenticationRequestConfiguration(
            "Confirmă semnătura",
            "Folosește biometria pentru a debloca cheia din enclavă.");
        var result = await CrossFingerprint.Current.AuthenticateAsync(request);
        if (!result.Authenticated)
            throw new UnauthorizedAccessException(
                "Biometric authentication failed or was cancelled.");
    }
#endif
}
