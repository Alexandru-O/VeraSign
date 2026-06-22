using FluentValidation;
using MasterSTI.Shared.DTOs.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MasterSTI.Api.Features.Auth.Login;

public static class LoginEndpoint
{
    public static IEndpointRouteBuilder MapLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (
            [FromBody] LoginRequest body,
            IMediator mediator,
            IValidator<LoginCommand> validator,
            CancellationToken cancellationToken) =>
        {
            var command = new LoginCommand(body.Email, body.Password);

            var validationResult = await validator.ValidateAsync(command, cancellationToken);
            if (!validationResult.IsValid)
                return Results.ValidationProblem(validationResult.ToDictionary());

            try
            {
                var response = await mediator.Send(command, cancellationToken);
                return Results.Ok(response);
            }
            catch (InvalidCredentialsException ex)
            {
                return Results.Problem(
                    title: "Invalid credentials",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status401Unauthorized);
            }
        })
        .AllowAnonymous()
        .WithName("Login")
        .WithTags("Auth")
        .Produces<LoginResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesValidationProblem();

        return app;
    }
}
