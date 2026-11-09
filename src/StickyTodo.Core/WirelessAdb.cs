using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace StickyTodo.Core;

public sealed record WirelessAdbDevice(string serviceName, string address) {
    public string displayName => serviceName + " · " + address;
    public override string ToString() => displayName;
}

public sealed record WirelessAdbConnectionResult(bool isConnected, string message);

public interface IWirelessAdb {
    Task<IReadOnlyList<WirelessAdbDevice>> discover(CancellationToken cancellationToken);
    Task pair(string address, string pairingCode, CancellationToken cancellationToken);
    Task<WirelessAdbConnectionResult> connect(string pairingAddress, string? connectionAddress, CancellationToken cancellationToken);
}

public sealed class WirelessAdb(Func<IReadOnlyList<string>, CancellationToken, Task<string>>? command = null) : IWirelessAdb {
    private const string PAIRING_SERVICE = "_adb-tls-pairing._tcp";
    private const string CONNECTION_SERVICE = "_adb-tls-connect._tcp";

    public async Task<IReadOnlyList<WirelessAdbDevice>> discover(CancellationToken cancellationToken) =>
        (await readServices(cancellationToken)).Where(service => service.type == PAIRING_SERVICE)
            .Select(service => new WirelessAdbDevice(service.name, service.endpoint.ToString()))
            .Distinct().OrderBy(device => device.serviceName, StringComparer.Ordinal).ThenBy(device => device.address, StringComparer.Ordinal).ToList();

    public async Task pair(string address, string pairingCode, CancellationToken cancellationToken) {
        var endpoint = parseAddress(address);
        var code = pairingCode.Trim();
        if (code.Length != 6 || code.Any(character => character is < '0' or > '9')) {
            throw new FormatException("請輸入手機顯示的六位數配對碼。");
        }
        var output = await run(["pair", endpoint.ToString(), code], cancellationToken);
        var success = "Successfully paired to " + endpoint;
        if (!TodoDocument.normalizeLines(output).Split('\n').Any(line => line.Trim() == success
            || line.Trim().StartsWith(success + " ", StringComparison.Ordinal))) {
            throw new IOException("配對未成功，請確認六位配對碼、配對位址及手機的配對視窗仍開啟。\n" + output.Trim());
        }
    }

    public async Task<WirelessAdbConnectionResult> connect(string pairingAddress, string? connectionAddress, CancellationToken cancellationToken) {
        var pairedEndpoint = parseAddress(pairingAddress);
        IPEndPoint? endpoint = null;
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(connectionAddress)) {
            endpoint = parseAddress(connectionAddress);
            if (!endpoint.Address.Equals(pairedEndpoint.Address) || endpoint.Equals(pairedEndpoint)) {
                throw new FormatException("請填入同一台手機無線偵錯主畫面的 IP 與連接埠，不能使用配對連接埠。");
            }
        } else {
            var services = (await readServices(cancellationToken)).Where(service => service.type == CONNECTION_SERVICE
                && service.endpoint.Address.Equals(pairedEndpoint.Address) && !service.endpoint.Equals(pairedEndpoint)).ToList();
            var endpoints = services.Select(service => service.endpoint).Distinct().ToList();
            if (endpoints.Count == 1) {
                endpoint = endpoints[0];
                foreach (var service in services) { aliases.Add(service.name + "." + CONNECTION_SERVICE); }
            }
        }
        if (endpoint == null) {
            return new(false, "配對成功；尚無法唯一確認連線位址。請填入手機無線偵錯主畫面的 IP 與連接埠後連線。");
        }
        aliases.Add(endpoint.ToString());
        if (await isOnline(aliases, cancellationToken)) { return new(true, "已連線"); }
        var output = await run(["connect", endpoint.ToString()], cancellationToken);
        // ADB can return exit code 0 with a failure message. Verify this target's actual transport instead.
        for (var attempt = 0; attempt < 3; attempt++) {
            if (await isOnline(aliases, cancellationToken)) { return new(true, "已連線"); }
            if (attempt < 2) { await Task.Delay(250, cancellationToken); }
        }
        return new(false, "配對成功，但尚未確認裝置已連線。請確認手機的連線位址及 Wi-Fi 後重試。\n" + output.Trim());
    }

    private async Task<bool> isOnline(HashSet<string> aliases, CancellationToken cancellationToken) {
        var output = await run(["devices", "-l"], cancellationToken);
        return TodoDocument.normalizeLines(output).Split('\n').Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Any(columns => columns.Length >= 2 && columns[1] == "device" && aliases.Contains(columns[0].TrimEnd('.')));
    }

    private async Task<List<(string name, string type, IPEndPoint endpoint)>> readServices(CancellationToken cancellationToken) {
        var output = await run(["mdns", "services"], cancellationToken);
        var services = new List<(string name, string type, IPEndPoint endpoint)>();
        var hasHeader = false;
        foreach (var line in TodoDocument.normalizeLines(output).Split('\n')) {
            if (line.StartsWith("List of discovered mdns services", StringComparison.Ordinal)) { hasHeader = true; continue; }
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 3 || columns[1].TrimEnd('.') is not (PAIRING_SERVICE or CONNECTION_SERVICE)) { continue; }
            try { services.Add((columns[0].TrimEnd('.'), columns[1].TrimEnd('.'), parseAddress(columns[2]))); }
            catch (FormatException) { /* Ignore malformed advertisements; manual input remains available. */ }
        }
        if (!hasHeader && services.Count == 0) {
            throw new IOException("無法搜尋無線裝置，請確認 Android SDK Platform-Tools 支援 mDNS，或改用手動輸入。\n" + output.Trim());
        }
        return services;
    }

    private async Task<string> run(IReadOnlyList<string> arguments, CancellationToken cancellationToken) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { return await (command ?? AdbCommand.run)(arguments, timeout.Token); }
        catch (IOException exception) when (exception.Message.Contains("unknown command", StringComparison.OrdinalIgnoreCase)) {
            throw new IOException("目前的 ADB 不支援這項操作，請更新 Android SDK Platform-Tools。\n" + exception.Message, exception);
        }
    }

    private static IPEndPoint parseAddress(string value) {
        var text = value.Trim();
        var separator = text.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(text.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535) { throw invalidAddress(); }
        var host = text[..separator];
        var isBracketed = host.StartsWith('[') && host.EndsWith(']');
        if (isBracketed) { host = host[1..^1]; }
        if (!IPAddress.TryParse(host, out var address) || (address.AddressFamily == AddressFamily.InterNetworkV6 && !isBracketed)
            || (address.AddressFamily == AddressFamily.InterNetwork && (isBracketed || host.Split('.').Length != 4
                || host.Any(character => character != '.' && (character < '0' || character > '9'))))) { throw invalidAddress(); }
        return new(address, port);
    }

    private static FormatException invalidAddress() => new("請輸入有效的 IP:連接埠，例如 192.168.1.10:37001（IPv6 請用 [位址]:連接埠）。");
}
