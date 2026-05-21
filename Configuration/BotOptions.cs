namespace AntiSpamBot.Configuration;

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    public string Token { get; set; } = "";
    public bool RegisterGuildCommandsOnReady { get; set; } = true;
    public string DataDirectory { get; set; } = "data";
    public GuildSettings DefaultSettings { get; set; } = GuildSettings.CreateDefault();
}
