using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Xml.Linq;
using System.Threading;

using HashDepot;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

using Sanctuary.WebAPI.Options;

namespace Sanctuary.WebAPI.Endpoints;

public static class ManifestEndpoints
{
    private static readonly SemaphoreSlim ClientManifestLock = new(1, 1);
    private static string? _clientManifest;
    private static string? _clientManifestCacheKey;

    public static void UseClientFiles(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ManifestOptions>>().Value;
        var clientRoot = GetClientRoot(app.Environment.ContentRootPath, options);

        if (!Directory.Exists(clientRoot))
            return;

        app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = "/client",
            FileProvider = new PhysicalFileProvider(clientRoot),
            ServeUnknownFileTypes = true,
            DefaultContentType = MediaTypeNames.Application.Octet
        });
    }

    public static void MapManifestEndpoints(this WebApplication app)
    {
        app.MapGet("/servermanifest.xml", (
            HttpContext context,
            IOptionsSnapshot<ManifestOptions> options) =>
        {
            var manifestOptions = options.Value;
            var webApiUrl = string.IsNullOrWhiteSpace(manifestOptions.WebApiUrl)
                ? GetRequestBaseUrl(context)
                : manifestOptions.WebApiUrl.TrimEnd('/');

            var document = new XDocument(
                new XElement("ServerManifest",
                    new XAttribute("version", 2),
                    new XElement("Name", manifestOptions.Name),
                    new XElement("Description", manifestOptions.Description),
                    new XElement("WebApiUrl", webApiUrl),
                    new XElement("LoginServer", manifestOptions.LoginServer)));

            return Xml(document);
        });

        app.MapGet("/clientmanifest.xml", async (
            IWebHostEnvironment environment,
            IOptionsSnapshot<ManifestOptions> options) =>
        {
            var manifestOptions = options.Value;
            var clientRoot = GetClientRoot(environment.ContentRootPath, manifestOptions);
            var languages = manifestOptions.Languages.Length == 0
                ? "en_US"
                : string.Join(',', manifestOptions.Languages);
            var cacheKey = $"{clientRoot}|{languages}|{GetClientRootStamp(clientRoot)}";

            if (_clientManifest is not null && _clientManifestCacheKey == cacheKey)
                return Results.Text(_clientManifest, MediaTypeNames.Application.Xml);

            await ClientManifestLock.WaitAsync();

            try
            {
                if (_clientManifest is not null && _clientManifestCacheKey == cacheKey)
                    return Results.Text(_clientManifest, MediaTypeNames.Application.Xml);

                var document = new XDocument(
                    new XElement("ClientManifest",
                        new XAttribute("version", 1),
                        new XAttribute("languages", languages),
                        BuildFolderElement(clientRoot, clientRoot, string.Empty)));

                _clientManifest = document.ToString(SaveOptions.DisableFormatting);
                _clientManifestCacheKey = cacheKey;

                return Results.Text(_clientManifest, MediaTypeNames.Application.Xml);
            }
            finally
            {
                ClientManifestLock.Release();
            }
        });
    }

    private static IResult Xml(XDocument document)
    {
        return Results.Text(document.ToString(SaveOptions.DisableFormatting), MediaTypeNames.Application.Xml);
    }

    private static XElement BuildFolderElement(string rootPath, string path, string name)
    {
        var folder = new XElement("Folder", new XAttribute("name", name));

        if (!Directory.Exists(path))
            return folder;

        foreach (var directory in Directory.EnumerateDirectories(path).Order(StringComparer.OrdinalIgnoreCase))
        {
            folder.Add(BuildFolderElement(rootPath, directory, Path.GetFileName(directory)));
        }

        foreach (var file in Directory.EnumerateFiles(path).Order(StringComparer.OrdinalIgnoreCase))
        {
            var fileInfo = new FileInfo(file);

            if (fileInfo.Length == 0)
                continue;

            using var stream = fileInfo.OpenRead();

            folder.Add(new XElement("File",
                new XAttribute("name", fileInfo.Name),
                new XAttribute("size", fileInfo.Length),
                new XAttribute("hash", XXHash.Hash64(stream))));
        }

        return folder;
    }

    private static string GetClientRoot(string contentRootPath, ManifestOptions options)
    {
        if (Path.IsPathRooted(options.ClientFilesPath))
            return Path.GetFullPath(options.ClientFilesPath);

        return Path.GetFullPath(Path.Combine(contentRootPath, options.ClientFilesPath));
    }

    private static string GetRequestBaseUrl(HttpContext context)
    {
        return $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}".TrimEnd('/');
    }

    private static long GetClientRootStamp(string clientRoot)
    {
        if (!Directory.Exists(clientRoot))
            return 0;

        return Directory.EnumerateFiles(clientRoot, "*", SearchOption.AllDirectories)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max()
            .Ticks;
    }
}
