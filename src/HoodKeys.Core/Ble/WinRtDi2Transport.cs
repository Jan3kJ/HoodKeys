using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace HoodKeys.Core.Ble;

/// <summary><see cref="IDi2Transport"/> over the Windows Runtime Bluetooth LE API.</summary>
public sealed class WinRtDi2Transport : IDi2Transport
{
    public async Task<IReadOnlyList<AdvertisedDevice>> ScanAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var seen = new Dictionary<ulong, AdvertisedDevice>();
        var stopped = new TaskCompletionSource<BluetoothError>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Active scanning asks for scan responses, which often carry the device name.
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };

        watcher.Received += (_, e) =>
        {
            lock (seen)
            {
                var name = string.IsNullOrEmpty(e.Advertisement.LocalName) ? null : e.Advertisement.LocalName;
                if (seen.TryGetValue(e.BluetoothAddress, out var known))
                {
                    seen[e.BluetoothAddress] = known with
                    {
                        Name = name ?? known.Name,
                        Rssi = e.RawSignalStrengthInDBm,
                        ServiceUuids = known.ServiceUuids.Union(e.Advertisement.ServiceUuids).ToList(),
                    };
                }
                else
                {
                    seen[e.BluetoothAddress] = new AdvertisedDevice(
                        e.BluetoothAddress, name, e.RawSignalStrengthInDBm, e.Advertisement.ServiceUuids.ToList());
                }
            }
        };
        watcher.Stopped += (_, e) => stopped.TrySetResult(e.Error);

        watcher.Start();
        try
        {
            var finished = await Task.WhenAny(Task.Delay(duration, cancellationToken), stopped.Task);
            if (finished == stopped.Task && stopped.Task.Result != BluetoothError.Success)
            {
                throw new InvalidOperationException($"Bluetooth scan stopped: {stopped.Task.Result}");
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            watcher.Stop();
        }

        lock (seen)
        {
            return seen.Values.ToList();
        }
    }

    public async Task<IDi2Link> ConnectAsync(ulong address, CancellationToken cancellationToken)
    {
        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cancellationToken)
            ?? throw new InvalidOperationException($"No Bluetooth LE device at {BleAddress.Format(address)}");
        return new WinRtDi2Link(device);
    }
}

internal sealed class WinRtDi2Link : IDi2Link
{
    private readonly BluetoothLEDevice _device;
    private readonly List<GattDeviceService> _services = [];
    // Held so the ValueChanged subscription stays alive for the life of the link.
    private GattCharacteristic? _dfly;

    public WinRtDi2Link(BluetoothLEDevice device)
    {
        _device = device;
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    public string? Name => _device.Name;

    public bool IsConnected => _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

    public event Action<bool>? ConnectionChanged;

    public event Action<RawReport>? ReportReceived;

    public async Task<IReadOnlyList<GattServiceInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var services = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        Ensure(services.Status, services.ProtocolError, "listing services");

        DisposeServices();
        _services.AddRange(services.Services);

        var result = new List<GattServiceInfo>();
        foreach (var service in services.Services)
        {
            var characteristics = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            var infos = new List<GattCharacteristicInfo>();
            if (characteristics.Status == GattCommunicationStatus.Success)
            {
                foreach (var c in characteristics.Characteristics)
                {
                    infos.Add(new GattCharacteristicInfo(c.Uuid, c.CharacteristicProperties.ToString()));
                    if (c.Uuid == DflyGatt.CharacteristicUuid)
                    {
                        _dfly = c;
                    }
                }
            }
            else
            {
                infos.Add(new GattCharacteristicInfo(Guid.Empty, $"(characteristics not listed: {characteristics.Status})"));
            }

            result.Add(new GattServiceInfo(service.Uuid, infos));
        }

        return result;
    }

    public async Task<RawReport?> ReadDflyAsync(CancellationToken cancellationToken)
    {
        var c = await FindDflyAsync(cancellationToken);
        if (c is null || !c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
        {
            return null;
        }

        var read = await c.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        Ensure(read.Status, read.ProtocolError, "reading D-Fly");
        var report = new RawReport(DateTimeOffset.Now, "read", ToBytes(read.Value));
        ReportReceived?.Invoke(report);
        return report;
    }

    public async Task<string> SubscribeDflyAsync(CancellationToken cancellationToken)
    {
        var c = await FindDflyAsync(cancellationToken)
            ?? throw new InvalidOperationException("The device has no D-Fly characteristic");

        var (mode, source) = c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate)
            ? (GattClientCharacteristicConfigurationDescriptorValue.Indicate, "indicate")
            : c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                ? (GattClientCharacteristicConfigurationDescriptorValue.Notify, "notify")
                : throw new InvalidOperationException($"D-Fly offers neither indications nor notifications ({c.CharacteristicProperties})");

        c.ValueChanged += (_, e) => ReportReceived?.Invoke(new RawReport(e.Timestamp, source, ToBytes(e.CharacteristicValue)));
        var status = await c.WriteClientCharacteristicConfigurationDescriptorAsync(mode).AsTask(cancellationToken);
        Ensure(status, null, $"subscribing ({source})");
        return source;
    }

    public void Dispose()
    {
        _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _dfly = null;
        DisposeServices();
        _device.Dispose();
    }

    private async Task<GattCharacteristic?> FindDflyAsync(CancellationToken cancellationToken)
    {
        if (_dfly is null)
        {
            await DiscoverAsync(cancellationToken);
        }

        return _dfly;
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args) =>
        ConnectionChanged?.Invoke(sender.ConnectionStatus == BluetoothConnectionStatus.Connected);

    private void DisposeServices()
    {
        foreach (var s in _services)
        {
            s.Dispose();
        }

        _services.Clear();
    }

    private static void Ensure(GattCommunicationStatus status, byte? protocolError, string what)
    {
        if (status != GattCommunicationStatus.Success)
        {
            var detail = protocolError is { } code ? $", protocol error 0x{code:X2}" : "";
            throw new InvalidOperationException($"GATT {what} failed: {status}{detail}");
        }
    }

    private static byte[] ToBytes(IBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
