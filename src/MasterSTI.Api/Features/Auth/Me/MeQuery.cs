using MasterSTI.Shared.DTOs.Auth;
using MediatR;

namespace MasterSTI.Api.Features.Auth.Me;

public record MeQuery() : IRequest<UserInfo>;
