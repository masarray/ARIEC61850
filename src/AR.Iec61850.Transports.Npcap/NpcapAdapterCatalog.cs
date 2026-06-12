using AR.Iec61850.Ethernet;
using SharpPcap;

namespace AR.Iec61850.Transports.Npcap;

public static class NpcapAdapterCatalog
{
    public static IReadOnlyList<NpcapAdapterInfo> ListAdapters()
    {
        var devices = CaptureDeviceList.Instance;
        var result = new List<NpcapAdapterInfo>(devices.Count);

        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            result.Add(new NpcapAdapterInfo
            {
                Index = i + 1,
                Name = device.Name ?? string.Empty,
                Description = device.Description ?? string.Empty,
                MacAddress = TryGetMacAddress(device)
            });
        }

        return result;
    }

    public static ICaptureDevice ResolveAdapter(string adapterSelector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterSelector);

        var devices = CaptureDeviceList.Instance;
        if (devices.Count == 0)
            throw new InvalidOperationException("No Npcap/WinPcap capture devices were found.");

        if (int.TryParse(adapterSelector, out var index))
        {
            if (index < 1 || index > devices.Count)
                throw new ArgumentOutOfRangeException(nameof(adapterSelector), $"Adapter index must be 1..{devices.Count}.");

            return devices[index - 1];
        }

        foreach (var device in devices)
        {
            if (string.Equals(device.Name, adapterSelector, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(device.Description, adapterSelector, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        throw new InvalidOperationException($"No adapter matched '{adapterSelector}'. Run list-adapters first.");
    }

    public static NpcapAdapterInfo ResolveAdapterInfo(string adapterSelector)
    {
        var adapters = ListAdapters();
        if (int.TryParse(adapterSelector, out var index))
        {
            var byIndex = adapters.FirstOrDefault(a => a.Index == index);
            if (byIndex is not null)
                return byIndex;
        }

        var byText = adapters.FirstOrDefault(a =>
            string.Equals(a.Name, adapterSelector, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.Description, adapterSelector, StringComparison.OrdinalIgnoreCase));

        if (byText is not null)
            return byText;

        throw new InvalidOperationException($"No adapter matched '{adapterSelector}'. Run list-adapters first.");
    }

    private static MacAddress? TryGetMacAddress(ICaptureDevice device)
    {
        try
        {
            var address = device.MacAddress;
            if (address is null)
                return null;

            var bytes = address.GetAddressBytes();
            return bytes.Length == 6 ? new MacAddress(bytes) : null;
        }
        catch
        {
            return null;
        }
    }
}
