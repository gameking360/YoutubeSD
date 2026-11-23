namespace identity.Api;

public static class CurrentUser
{
    public const string Header = "X-User-Id";

    public static string? Id(HttpContext context)
    {
        var header = context.Request.Headers[Header].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header))
        {
            return header.Trim();
        }

        return context.User.FindFirst("sub")?.Value;
    }
}
