using MasterSTI.Shared.DTOs.Documents;
using MediatR;

namespace MasterSTI.Api.Features.SignedDocuments.GetInfo;

public record GetSignedDocumentInfoQuery(Guid SignedDocumentId) : IRequest<GetSignedDocumentInfoResult>;

public enum GetSignedDocumentInfoStatus { Ok, NotFound, Forbidden }

public record GetSignedDocumentInfoResult(
    GetSignedDocumentInfoStatus Status,
    SignedDocumentInfoDto? Info)
{
    public static GetSignedDocumentInfoResult NotFound() => new(GetSignedDocumentInfoStatus.NotFound, null);
    public static GetSignedDocumentInfoResult Forbidden() => new(GetSignedDocumentInfoStatus.Forbidden, null);
    public static GetSignedDocumentInfoResult Ok(SignedDocumentInfoDto info) => new(GetSignedDocumentInfoStatus.Ok, info);
}
