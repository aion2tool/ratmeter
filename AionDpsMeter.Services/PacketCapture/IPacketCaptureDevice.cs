namespace AionDpsMeter.Services.PacketCapture;

public interface IPacketCaptureDevice : IDisposable
{
    bool IsCapturing { get; }
    /// <summary>The game connection was found and capture is locked onto it.</summary>
    bool IsLocked { get; }
    string? DeviceName { get; }
    void StartCapture();
    void StopCapture();
    void DiscoverAdapters();
}