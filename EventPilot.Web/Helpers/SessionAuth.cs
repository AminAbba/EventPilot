namespace EventPilot.Web.Helpers;

public static class SessionAuth
{
    private const string TokenKey = "ACCESS_TOKEN";

    public static void SetToken(this ISession session, string token) =>
        session.SetString(TokenKey, token);

    public static string? GetToken(this ISession session) =>
        session.GetString(TokenKey);

    public static void ClearToken(this ISession session) =>
        session.Remove(TokenKey);
}
