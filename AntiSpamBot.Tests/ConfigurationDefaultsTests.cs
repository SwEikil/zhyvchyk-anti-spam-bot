using System.Text.Json;
using AntiSpamBot.Configuration;
using Xunit;

namespace AntiSpamBot.Tests;

public sealed class ConfigurationDefaultsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void UserRiskDefaults_BindToStrictMonitoringDefaults()
    {
        var settings = JsonSerializer.Deserialize<UserRiskSettings>("{}", JsonOptions);

        Assert.NotNull(settings);
        Assert.Equal(60, settings.NewAccountAgeDays);
        Assert.Equal(60, settings.RecentJoinDays);
        Assert.True(settings.StrictMonitoringEnabled);
        Assert.Equal(4, settings.StrictMonitoringScoreBonus);
        Assert.True(settings.StrictMonitoringTimeoutOnConfirmedSpam);
        Assert.Equal(2, settings.StrictMonitoringMinimumSpamCount);
    }

    [Fact]
    public void LegacyUserRiskFields_StillDeserialize()
    {
        const string json = """
            {
              "newAccountAgeHours": 12,
              "recentJoinMinutes": 30
            }
            """;

        var settings = JsonSerializer.Deserialize<UserRiskSettings>(json, JsonOptions);

        Assert.NotNull(settings);
        Assert.Equal(12, settings.NewAccountAgeHours);
        Assert.Equal(30, settings.RecentJoinMinutes);
    }

    [Fact]
    public void ExampleConfig_BindsNewUserRiskSettings()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var json = File.ReadAllText(Path.Combine(root, "appsettings.json.example"));
        var document = JsonDocument.Parse(json);
        var userRisk = document.RootElement
            .GetProperty("Bot")
            .GetProperty("DefaultSettings")
            .GetProperty("UserRisk")
            .Deserialize<UserRiskSettings>(JsonOptions);

        Assert.NotNull(userRisk);
        Assert.Equal(60, userRisk.NewAccountAgeDays);
        Assert.Equal(60, userRisk.RecentJoinDays);
        Assert.True(userRisk.StrictMonitoringEnabled);
        Assert.True(userRisk.StrictMonitoringTimeoutOnConfirmedSpam);
    }
}
