using MasterSTI.Shared.DTOs.Auth;
using MediatR;

namespace MasterSTI.Api.Features.Auth.Login;

public record LoginCommand(string Email, string Password) : IRequest<LoginResponse>;
