using System.Globalization;

namespace StickyTodo.Core;

public enum DeviceChargingState { UNKNOWN, CHARGING, DISCHARGING, NOT_CHARGING, FULL }

public sealed record DeviceBatteryInfo(int? percentage, DeviceChargingState chargingState) {
    public string displayText => (percentage is int value ? $"電量 {value}%" : "電量未知") + " · " + (chargingState switch {
        DeviceChargingState.CHARGING => "充電中",
        DeviceChargingState.DISCHARGING => "使用電池",
        DeviceChargingState.NOT_CHARGING => "未在充電",
        DeviceChargingState.FULL => "已充滿",
        _ => "充電狀態未知"
    });

    public static DeviceBatteryInfo? parse(string output) {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in TodoDocument.normalizeLines(output).Split('\n')) {
            var separator = line.IndexOf(':');
            if (separator > 0) { values[line[..separator].Trim()] = line[(separator + 1)..].Trim(); }
        }
        if (values.TryGetValue("present", out var present) && (!bool.TryParse(present, out var hasBattery) || !hasBattery)) { return null; }
        int? percentage = null;
        if (int.TryParse(values.GetValueOrDefault("level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
            && int.TryParse(values.GetValueOrDefault("scale"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale)
            && scale > 0 && level >= 0 && level <= scale) {
            percentage = (int)Math.Round(100.0 * level / scale, MidpointRounding.AwayFromZero);
        }
        var state = values.GetValueOrDefault("status") switch {
            "2" => DeviceChargingState.CHARGING,
            "3" => DeviceChargingState.DISCHARGING,
            "4" => DeviceChargingState.NOT_CHARGING,
            "5" => DeviceChargingState.FULL,
            _ => DeviceChargingState.UNKNOWN
        };
        return percentage == null && state == DeviceChargingState.UNKNOWN ? null : new(percentage, state);
    }
}
