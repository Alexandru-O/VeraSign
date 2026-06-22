using MasterSTI.Shared.DTOs.Wallet;
using MediatR;

namespace MasterSTI.Api.Features.Wallet.Inbox;

public sealed record ListInboxQuery() : IRequest<WalletInboxResponse>;
