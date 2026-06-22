using MasterSTI.Shared.DTOs.Wallet;
using MediatR;

namespace MasterSTI.Api.Features.Wallet.InboxItem.Get;

public sealed record GetInboxItemQuery(Guid RecipientId) : IRequest<WalletInboxItemMetaDto>;
