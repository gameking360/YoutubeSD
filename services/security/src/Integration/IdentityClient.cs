using System.Net;
using System.Net.Http.Json;

namespace YoutubeSD.Security.Integration;

public sealed record IdentityUser(string UserId, string Role);
public sealed class IdentityUnavailableException : Exception
{
    public IdentityUnavailableException() : base("Identity indisponível.") { }
}

public sealed class IdentityClient(HttpClient http)
{
    public Task<IdentityUser?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/identity/internal/authenticate")
        {
            Content = JsonContent.Create(new { email, password })
        }, cancellationToken);

    public Task<IdentityUser?> GetUserAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get,
            $"/api/identity/internal/users/{Uri.EscapeDataString(userId)}/authentication"), cancellationToken);

    private async Task<IdentityUser?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            try
            {
                using var response = await http.SendAsync(request, cancellationToken);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
                    return null;
                if (!response.IsSuccessStatusCode)
                    throw new IdentityUnavailableException();
                var user = await response.Content.ReadFromJsonAsync<IdentityUser>(cancellationToken);
                if (user is null || !Guid.TryParse(user.UserId, out _) || string.IsNullOrWhiteSpace(user.Role))
                    throw new IdentityUnavailableException();
                return user;
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // Do not retain HTTP requests containing credentials in an exception chain.
                throw new IdentityUnavailableException();
            }
        }
    }
}
