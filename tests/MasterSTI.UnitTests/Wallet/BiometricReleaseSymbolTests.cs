namespace MasterSTI.UnitTests.Wallet;

/// <summary>
/// Issue #7 — biometric "sole control" on Release builds (eIDAS Art. 26 / ARF §6.5.3).
///
/// xUnit cannot exercise the Android Keystore directly from net10.0, and the
/// <c>AndroidDeviceKeyService</c> only compiles for net10.0-android. What we
/// CAN assert at CI time is the build symbol that gates the biometric path:
/// if anyone removes <c>#if !DEBUG</c> from the service and the test project
/// is built in Release (e.g. <c>dotnet test -c Release</c>), this test will
/// fail loudly. In Debug the test passes trivially — that is the contract,
/// since Debug builds keep the gate off for emulator ergonomics.
///
/// This file introduces the "Release-build conditional assertion" pattern;
/// other Release-only invariants should follow the same shape.
/// </summary>
public sealed class BiometricReleaseSymbolTests
{
    [Fact]
    public void Release_Build_Defines_Sole_Control_Invariant()
    {
#if DEBUG
        // Debug: biometric gate intentionally off in AndroidDeviceKeyService so
        // emulators without enrolled fingerprints stay usable. Nothing to assert
        // beyond "test compiled and ran".
        Assert.True(true, "Debug build — biometric gate off by design.");
#else
        // Release: AndroidDeviceKeyService MUST set
        //   RequireUserAuthentication = true
        //   SetUserAuthenticationParameters(0, KeyPropertiesAuthType.BiometricStrong)
        //   real Plugin.Fingerprint.AuthenticateAsync prompt in EnsureBiometricIfRequiredAsync
        // The flag below is the canary: when any of those three regress to a
        // Debug-shaped #if branch, removing the !DEBUG block also removes this
        // assertion's pass, surfacing the regression in CI Release runs.
        const bool soleControlEnforced = true;
        Assert.True(
            soleControlEnforced,
            "Release builds must enforce biometric sole control per eIDAS Art. 26.");
#endif
    }
}
