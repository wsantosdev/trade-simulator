using TradeSimulator.Account.Infrastructure.Http;

namespace TradeSimulator.Account.Features.Stream;

public static class AccountStreamEndpoints
{
    public static IEndpointRouteBuilder MapAccountStreamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/accounts/{id:guid}/stream", async (Guid id, IAccountStream stream, CancellationToken ct) =>
            Respond(await stream.GetAsync(id, ct)));

        endpoints.MapPost("/api/accounts/{id:guid}/stream/events", async (
            Guid id, InsertEventRequest request, IAccountStream stream, CancellationToken ct) =>
            Respond(await stream.InsertAsync(id, request.Position, request.Type ?? string.Empty, request.Amount, ct)));

        endpoints.MapPost("/api/accounts/{id:guid}/stream/replay", async (Guid id, IAccountStream stream, CancellationToken ct) =>
            Respond(await stream.ReplayAsync(id, ct)));

        return endpoints;
    }

    private static IResult Respond(Moonad.Result<AccountStreamView, Domain.IAccountError> result) =>
        result.IsError ? result.ErrorValue.ToHttpResult() : Results.Ok(result.ResultValue);
}

public sealed record InsertEventRequest(int Position, string? Type, decimal Amount);
