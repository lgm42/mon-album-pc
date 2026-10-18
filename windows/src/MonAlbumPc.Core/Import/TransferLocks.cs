using System.Collections.Concurrent;

namespace MonAlbumPc.Core.Import;

/// <summary>USB imports win over Wi-Fi: while a phone is imported over USB, its Wi-Fi uploads are paused.</summary>
public sealed class TransferLocks
{
    private readonly ConcurrentDictionary<Guid, byte> _usb = new();

    public bool IsUsbBusy(Guid deviceId) => _usb.ContainsKey(deviceId);

    /// <summary>Marks the phone as being imported over USB until the returned handle is disposed.</summary>
    public IDisposable AcquireUsb(Guid deviceId)
    {
        if (!_usb.TryAdd(deviceId, 0))
        {
            throw new InvalidOperationException("Un import USB de ce téléphone est déjà en cours.");
        }
        return new Release(() => _usb.TryRemove(deviceId, out _));
    }

    private sealed class Release(Action release) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                release();
            }
        }
    }
}
