namespace identity.Api;

public static class CurrentUser
{
    public static string? Id(HttpContext context)
    {
        return context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst("sub")?.Value : null;
    }
}
