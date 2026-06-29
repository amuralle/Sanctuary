using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Collections;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Resources;

public sealed class CollectionNodeDefinitionCollection : ObservableConcurrentDictionary<string, CollectionNodeDefinition>
{
    private readonly ILogger _logger;

    public CollectionNodeDefinitionCollection(ILogger logger)
    {
        _logger = logger;
    }

    public bool Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("Failed to find file \"{file}\"", filePath);
            return false;
        }

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var entries = JsonSerializer.Deserialize<List<CollectionNodeDefinition>>(fileStream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (entries is null)
            {
                _logger.LogError("No entries found in file \"{file}\".", filePath);
                return false;
            }

            Clear();

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    _logger.LogWarning("Collection node entry without key in \"{file}\".", filePath);
                    continue;
                }

                if (!TryAdd(entry.Key, entry))
                    _logger.LogWarning("Failed to add collection node entry. {key} \"{file}\"", entry.Key, filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse file \"{file}\".", filePath);
            return false;
        }

        if (Count == 0)
        {
            _logger.LogError("No data was loaded. \"{file}\"", filePath);
            return false;
        }

        return true;
    }
}
