namespace BirthdayBot.Infrastructure.Options;

/// <summary>
/// VK ID OAuth 2.1 application configuration. Requires an app approved for the friends scope.
/// No user tokens or secrets are stored in configuration.
/// </summary>
public sealed class VkImportOptions
{
    public bool Enabled { get; set; } = false;
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string Scope { get; set; } = "friends";
    public string ApiVersion { get; set; } = "5.199";

    public bool IsConfigured =>
        Enabled && long.TryParse(ClientId, out var appId) && appId > 0 &&
        Uri.TryCreate(RedirectUri, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.AbsolutePath == "/integrations/vk/callback";
}
