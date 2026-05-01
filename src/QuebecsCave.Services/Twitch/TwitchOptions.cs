namespace QuebecsCave.Services.Twitch;

public sealed class TwitchOptions
{
    public const string SectionName = "Twitch";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string StreamerUserId { get; set; } = "";
    public string StreamerLogin { get; set; } = "";
    public string RedirectPath { get; set; } = "/signin-twitch";
    public int ModRefreshSeconds { get; set; } = 900;
    public int LiveStatusPollSeconds { get; set; } = 60;
    public bool DevForceLive { get; set; }
    public string DevForceLiveGame { get; set; } = "";
    public string DevForceLiveTitle { get; set; } = "";
}
