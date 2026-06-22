using MasterSTI.Shared.DTOs.Dashboard;
using MediatR;

namespace MasterSTI.Api.Features.Documents.List;

public record ListDocumentsQuery(
    string? Status,
    string? Search,
    string? Level,
    string? Period,
    int Page,
    int PageSize) : IRequest<PagedResultDto<DocumentListItemDto>>;
