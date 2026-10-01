using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Devices.Enumeration;

namespace Lutris.Services;

/// <summary>
/// A graphics card a game can be pinned to. <see cref="Id"/> is the display-adapter device
/// interface path, which is exactly what Windows stores for the choice.
/// </summary>
public sealed record GpuAdapter(string Id, string Name);

/// <summary>
/// The per-executable graphics card choice that Windows keeps for Settings > System > Display >
/// Graphics. Windows applies it on its own when the executable starts, so launching needs no extra
/// work: this only reads and writes the same registry entry the Settings page does. Pinning to a
/// specific card exists from Windows 11 22H2; earlier builds only know power-saving versus
/// high-performance roles, which are left alone here.
/// </summary>
public static class GpuPreferences
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string PreferenceField = "GpuPreference";
    private const string SpecificGpuField = "SpecificGPU";

    // GUID_DISPLAY_DEVICE_ARRIVAL: the interface every adapter that can render exposes.
    private const string DisplayAdapterInterface = "{1CA05180-A699-450A-9A0C-DE4FBE3DDD89}";
    // The "Display adapters" device setup class, used only for the friendly names.
    private const string DisplayAdapterClass = "{4D36E968-E325-11CE-BFC1-08002BE10318}";

    public static bool IsSupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// The hardware adapters in the order Windows enumerates them. Microsoft's software renderers
    /// are left out, as are adapters disabled in Device Manager.
    /// </summary>
    public static async Task<IReadOnlyList<GpuAdapter>> ListAdaptersAsync()
    {
        if (!IsSupported) return Array.Empty<GpuAdapter>();

        var interfaces = await DeviceInformation.FindAllAsync(
            $"System.Devices.InterfaceClassGuid:=\"{DisplayAdapterInterface}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True");
        // Interface entries are named after the PC, so the card names come from the devices.
        var devices = await DeviceInformation.FindAllAsync(
            $"System.Devices.ClassGuid:=\"{DisplayAdapterClass}\"", null, DeviceInformationKind.Device);
        var names = devices.ToDictionary(d => d.Id, d => d.Name, StringComparer.OrdinalIgnoreCase);

        var adapters = new List<GpuAdapter>();
        foreach (var iface in interfaces)
        {
            var instanceId = InstanceIdOf(iface.Id);
            // ROOT-enumerated adapters are the Basic Render and Basic Display drivers, not cards.
            if (instanceId.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase)) continue;
            var name = names.GetValueOrDefault(instanceId) ?? $"Graphics adapter {adapters.Count + 1}";
            adapters.Add(new GpuAdapter(iface.Id, name));
        }
        return adapters;
    }

    /// <summary>
    /// The adapter an executable is pinned to, or null when Windows decides (including the older
    /// power-saving / high-performance roles, which name no particular card).
    /// </summary>
    public static string? GetSpecificGpu(string exePath)
    {
        return Parse(ReadValue(exePath)).FirstOrDefault(f => Is(f.Key, SpecificGpuField)).Value;
    }

    /// <summary>
    /// Pins an executable to an adapter, or returns it to "Let Windows decide" when
    /// <paramref name="adapterId"/> is null. Other settings Windows keeps in the same entry, such
    /// as Auto HDR, are preserved.
    /// </summary>
    public static void SetSpecificGpu(string exePath, string? adapterId)
    {
        var fields = new List<KeyValuePair<string, string>> { new(PreferenceField, "0") };
        if (adapterId is not null) fields.Add(new(SpecificGpuField, adapterId));
        fields.AddRange(Parse(ReadValue(exePath)).Where(f => !Is(f.Key, PreferenceField) && !Is(f.Key, SpecificGpuField)));
        WriteValue(exePath, fields);
    }

    /// <summary>
    /// Carries a pinned adapter over when a game's executable path changes. Best effort: Windows
    /// keys the choice by path, so without this an edited game would fall back to the default.
    /// </summary>
    public static void Move(string oldExePath, string newExePath)
    {
        if (string.Equals(oldExePath, newExePath, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var adapter = GetSpecificGpu(oldExePath);
            if (adapter is null || GetSpecificGpu(newExePath) is not null) return;
            SetSpecificGpu(newExePath, adapter);
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(oldExePath, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            // The game itself was saved; losing the card choice is the lesser problem.
        }
    }

    // \\?\PCI#VEN_10DE&DEV_1F95&...#4&cea572a&0&0008#{guid} -> PCI\VEN_10DE&DEV_1F95&...\4&cea572a&0&0008
    private static string InstanceIdOf(string interfaceId)
    {
        var body = interfaceId.StartsWith(@"\\?\", StringComparison.Ordinal) ? interfaceId[4..] : interfaceId;
        var brace = body.LastIndexOf("#{", StringComparison.Ordinal);
        if (brace >= 0) body = body[..brace];
        return body.Replace('#', '\\');
    }

    // Entries look like "GpuPreference=0;SpecificGPU=\\?\PCI#...;" with any number of fields.
    private static List<KeyValuePair<string, string>> Parse(string? value)
    {
        var fields = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(value)) return fields;
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            fields.Add(new(part[..eq], part[(eq + 1)..]));
        }
        return fields;
    }

    private static bool Is(string field, string name) => string.Equals(field, name, StringComparison.OrdinalIgnoreCase);

    private static string? ReadValue(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(exePath) as string;
    }

    private static void WriteValue(string exePath, IEnumerable<KeyValuePair<string, string>> fields)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(exePath, string.Concat(fields.Select(f => $"{f.Key}={f.Value};")), RegistryValueKind.String);
    }
}
