using MasterSTI.Shared.DTOs.Wallet;
using MediatR;

namespace MasterSTI.Api.Features.Wallet.History.List;

public sealed record ListWalletHistoryQuery() : IRequest<List<WalletHistoryItemDto>>;
