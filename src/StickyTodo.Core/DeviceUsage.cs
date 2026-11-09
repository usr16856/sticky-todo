namespace StickyTodo.Core;

public enum DeviceUsageState { UNMARKED, IDLE, IN_USE }

public sealed record DeviceUsage(string serial, string name, DeviceUsageState state, string user, string purpose,
    DateTimeOffset updatedAt) {
    public string stateName => getStateName(state);

    public static string getStateName(DeviceUsageState state) => state switch {
        DeviceUsageState.UNMARKED => "未標記",
        DeviceUsageState.IDLE => "閒置",
        DeviceUsageState.IN_USE => "使用中",
        _ => throw new ArgumentException("測試機使用狀態無效。")
    };

    public DeviceUsage changeUsage(DeviceUsageState state, string user, string purpose) {
        var changed = this with { state = state, user = state == DeviceUsageState.IN_USE ? user.Trim() : "",
            purpose = state == DeviceUsageState.IN_USE ? purpose.Trim() : "", updatedAt = DateTimeOffset.Now };
        changed.validate();
        return changed;
    }

    public void validate() {
        if (new[] { serial, name, user, purpose }.Any(value => value == null || value.Contains('\r') || value.Contains('\n'))
            || string.IsNullOrWhiteSpace(serial) || serial != serial.Trim() || string.IsNullOrWhiteSpace(name)
            || !Enum.IsDefined(state)) {
            throw new FormatException("測試機序號、名稱或使用資訊無效；各欄位須為單行文字。");
        }
        if (state == DeviceUsageState.IN_USE && (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(purpose))) {
            throw new FormatException("使用中時請填寫使用者與用途。");
        }
        if (state != DeviceUsageState.IN_USE && (user.Length > 0 || purpose.Length > 0)) {
            throw new FormatException("閒置或未標記時，使用者與用途須為空白。");
        }
    }
}

public sealed record ConnectedDevice(string serial, string name) {
    public DeviceBatteryInfo? battery { get; init; }
}
public sealed record DeviceScanResult(IReadOnlyList<ConnectedDevice> devices, bool isComplete = true, string? warning = null);

public interface IDeviceDiscovery {
    Task<DeviceScanResult> scan(CancellationToken cancellationToken);
}
