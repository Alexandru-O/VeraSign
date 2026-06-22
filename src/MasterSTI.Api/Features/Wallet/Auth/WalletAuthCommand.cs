using MasterSTI.Shared.DTOs.Wallet;
using MediatR;

namespace MasterSTI.Api.Features.Wallet.Auth;

/// <summary>
/// Initiates the wallet QR flow. <see cref="Purpose"/> selects the variant:
/// self-sign handoff (default, legacy) or password-less login.
/// </summary>
public record InitiateWalletAuthCommand(WalletAuthPurpose Purpose = WalletAuthPurpose.Sign) : IRequest<InitiateWalletAuthResponse>;
