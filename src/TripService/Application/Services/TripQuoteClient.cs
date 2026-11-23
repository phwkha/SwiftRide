using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SwiftRide.Contracts.Common;

namespace TripService.Application.Services;

public interface ITripQuoteClient
{
    Task<TripQuoteSnapshot?> GetAsync(Guid quoteId, CancellationToken ct);
}

public sealed class TripQuoteClient(HttpClient httpClient, IHttpContextAccessor httpContext) : ITripQuoteClient
{
    public async Task<TripQuoteSnapshot?> GetAsync(Guid quoteId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/quotes/{quoteId:D}");
        var authorization = httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(authorization, out var bearer) ||
            !string.Equals(bearer.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new QuoteLookupException(401, "UNAUTHORIZED", "A Rider bearer token is required.");
        request.Headers.Authorization = bearer;

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new QuoteLookupException(403, "FORBIDDEN_QUOTE_ACCESS", "The quote belongs to another rider.");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new QuoteLookupException(401, "UNAUTHORIZED", "The Rider bearer token is invalid or expired.");

            response.EnsureSuccessStatusCode();
            var quote = await response.Content.ReadFromJsonAsync<TripQuoteSnapshot>(ct);
            if (quote is null || quote.QuoteId != quoteId || quote.RiderId == Guid.Empty)
                throw new JsonException("Invalid quote snapshot.");
            return quote;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new QuoteLookupException(503, "QUOTE_SERVICE_UNAVAILABLE", "Matching/Pricing could not verify the quote.");
        }
    }
}

public sealed class QuoteLookupException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
