using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Wallet.Status;

public record GetWalletStatusQuery() : IRequest<WalletStatusDto>;
