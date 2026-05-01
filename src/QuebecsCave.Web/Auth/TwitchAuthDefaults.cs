namespace QuebecsCave.Web.Auth;

public static class TwitchAuthDefaults
{
    public const string Scheme = "Twitch";
    public const string CallbackPath = "/signin-twitch";
    public const string AuthorizationEndpoint = "https://id.twitch.tv/oauth2/authorize";
    public const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    public const string UserInformationEndpoint = "https://api.twitch.tv/helix/users";
}
