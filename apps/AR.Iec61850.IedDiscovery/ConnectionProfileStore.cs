using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using AR.Iec61850.IedDiscovery.ViewModels;

namespace AR.Iec61850.IedDiscovery;

internal static class ConnectionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string StorePath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "ARIEC61850", "ied-discovery-connections.json");
        }
    }

    public static IReadOnlyList<ConnectionProfileRow> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return DefaultProfiles();

            var json = File.ReadAllText(StorePath);
            var rows = JsonSerializer.Deserialize<List<ConnectionProfileRow>>(json, JsonOptions) ?? [];
            return rows.Where(x => !string.IsNullOrWhiteSpace(x.Host)).Take(50).ToArray();
        }
        catch (Exception)
        {
            return DefaultProfiles();
        }
    }

    public static void Save(ConnectionProfileRow profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Host))
            return;

        var list = Load()
            .Where(x => !string.Equals(x.Host, profile.Host, StringComparison.OrdinalIgnoreCase) || x.Port != profile.Port)
            .ToList();
        list.Insert(0, profile);
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(list.Take(50), JsonOptions));
    }

    private static IReadOnlyList<ConnectionProfileRow> DefaultProfiles()
        => [
            new("192.168.1.10", 102, "Lab IED", 30000),
            new("127.0.0.1", 102, "Local simulator", 30000)
        ];
}
