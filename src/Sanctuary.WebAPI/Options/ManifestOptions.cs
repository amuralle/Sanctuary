namespace Sanctuary.WebAPI.Options;

public sealed class ManifestOptions
{
    public const string Section = "Manifest";

    public string Name { get; set; } = "Local Sanctuary";
    public string Description { get; set; } = "Local Sanctuary development server.";

    public string? WebApiUrl { get; set; }
    public string LoginServer { get; set; } = "127.0.0.1:20042";

    public string ClientFilesPath { get; set; } = "Client";
    public string[] Languages { get; set; } = [];
}
