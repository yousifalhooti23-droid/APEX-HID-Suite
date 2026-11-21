using System.Diagnostics;
using System.Runtime.InteropServices;
using ApexHid.Core.Interop;
using ApexHid.Core.Models;

namespace ApexHid.Core.Services;

/// <summary>
/// Layer 6: Zero-Fake 60fps RawInput (WM_INPUT) + QueryPerformanceCounter Microsecond Oscilloscope.
/// Measures ONLY real WM_INPUT hardware packets using QueryPerformanceCounter.
/// When no controller is actively sending packets, reports 0 Hz / Waiting for Controller.
/// </summary>
public sealed class MicrosecondOscilloscope : IDisposable
{
    public const int WaveformBufferCapacity = 160;
    public const int PolarSectorCount = 36;

    private readonly object _syncLock = new();
    private readonly double[] _deltaHistoryMs = new double[WaveformBufferCapacity];
    private readonly float[] _leftPolarSectors = new float[PolarSectorCount];
    private readonly float[] _rightPolarSectors = new float[PolarSectorCount];

    private long _lastPacketQpcTicks;
    private int _validSampleCount;
    private int _writeIndex;
    private long _totalPackets;

    public bool IsPaused { get; set; }
    public ControllerDevice? ActiveController { get; set; }
    public CalibrationProfile? ActiveCalibration { get; set; }

    public bool IsReceivingPackets
    {
        get
        {
            long last = Interlocked.Read(ref _lastPacketQpcTicks);
            if (last == 0) return false;
            double elapsedMs = (Stopwatch.GetTimestamp() - last) * 1000.0 / Stopwatch.Frequency;
            return elapsedMs <= 500.0;
        }
    }

    public double CurrentHz { get; private set; }
    public double CurrentDeltaMs { get; private set; }
    public double CurrentJitterUs { get; private set; }
    public double P99DeltaMs { get; private set; }
    public long TotalPacketsCaptured => Interlocked.Read(ref _totalPackets);

    public float CurrentLeftX { get; private set; }
    public float CurrentLeftY { get; private set; }
    public float CurrentRightX { get; private set; }
    public float CurrentRightY { get; private set; }
    public double LiveCircularityErrorPercent { get; private set; }

    public MicrosecondOscilloscope()
    {
        ResetBuffers();
    }

    public void ResetBuffers()
    {
        lock (_syncLock)
        {
            Array.Clear(_deltaHistoryMs, 0, _deltaHistoryMs.Length);
            Array.Clear(_leftPolarSectors, 0, _leftPolarSectors.Length);
            Array.Clear(_rightPolarSectors, 0, _rightPolarSectors.Length);
            _validSampleCount = 0;
            _writeIndex = 0;
            Interlocked.Exchange(ref _lastPacketQpcTicks, 0);
            Interlocked.Exchange(ref _totalPackets, 0);

            CurrentHz = 0.0;
            CurrentDeltaMs = 0.0;
            CurrentJitterUs = 0.0;
            P99DeltaMs = 0.0;
            CurrentLeftX = 0f;
            CurrentLeftY = 0f;
            CurrentRightX = 0f;
            CurrentRightY = 0f;
            LiveCircularityErrorPercent = 0.0;
        }
    }

    /// <summary>
    /// Processes a real WM_INPUT hardware packet directly from the Windows RawInput stream.
    /// Timestamped with QueryPerformanceCounter (Stopwatch.GetTimestamp()).
    /// </summary>
    public void ProcessRawInputMessage(IntPtr hRawInput)
    {
        if (IsPaused) return;

        long nowTicks = Stopwatch.GetTimestamp();

        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
        if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
            return;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, buffer, ref size, headerSize) != size)
                return;

            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
            if (header.dwType != NativeMethods.RIM_TYPEHID)
                return;

            long prevTicks = Interlocked.Exchange(ref _lastPacketQpcTicks, nowTicks);
            Interlocked.Increment(ref _totalPackets);

            // Extract raw HID report payload following RAWINPUTHEADER + dwSizeHid (4) + dwCount (4)
            int hidHeaderOffset = (int)headerSize;
            if (size >= hidHeaderOffset + 8)
            {
                int reportSize = Marshal.ReadInt32(buffer, hidHeaderOffset);
                int reportCount = Marshal.ReadInt32(buffer, hidHeaderOffset + 4);
                int payloadOffset = hidHeaderOffset + 8;

                if (reportSize >= 5 && reportCount >= 1 && size >= payloadOffset + reportSize)
                {
                    byte[] report = new byte[reportSize];
                    Marshal.Copy(IntPtr.Add(buffer, payloadOffset), report, 0, reportSize);
                    ParseSonyHidStickAxes(report);
                }
            }

            if (prevTicks == 0)
                return;

            double deltaMs = (nowTicks - prevTicks) * 1000.0 / Stopwatch.Frequency;
            if (deltaMs <= 0.05 || deltaMs > 250.0)
                return;

            lock (_syncLock)
            {
                _deltaHistoryMs[_writeIndex] = deltaMs;
                _writeIndex = (_writeIndex + 1) % WaveformBufferCapacity;
                if (_validSampleCount < WaveformBufferCapacity)
                    _validSampleCount++;

                RecomputeRollingStatistics();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void ParseSonyHidStickAxes(byte[] report)
    {
        // USB Report ID 0x01 for DualShock 4 & DualSense:
        // byte[0] = ReportID (0x01), byte[1] = LX, byte[2] = LY, byte[3] = RX, byte[4] = RY
        int offset = report[0] == 0x01 ? 1 : 0;
        if (report.Length < offset + 4)
            return;

        float lx = (report[offset] - 127.5f) / 127.5f;
        float ly = (report[offset + 1] - 127.5f) / 127.5f;
        float rx = (report[offset + 2] - 127.5f) / 127.5f;
        float ry = (report[offset + 3] - 127.5f) / 127.5f;

        lock (_syncLock)
        {
            CurrentLeftX = Math.Clamp(lx, -1.15f, 1.15f);
            CurrentLeftY = Math.Clamp(ly, -1.15f, 1.15f);
            CurrentRightX = Math.Clamp(rx, -1.15f, 1.15f);
            CurrentRightY = Math.Clamp(ry, -1.15f, 1.15f);

            float magL = MathF.Sqrt(lx * lx + ly * ly);
            if (magL > 0.65f)
            {
                double angleL = Math.Atan2(ly, lx);
                if (angleL < 0) angleL += Math.PI * 2.0;
                int sectorL = (int)Math.Floor((angleL / (Math.PI * 2.0)) * PolarSectorCount) % PolarSectorCount;
                if (magL > _leftPolarSectors[sectorL])
                    _leftPolarSectors[sectorL] = magL;
            }

            float magR = MathF.Sqrt(rx * rx + ry * ry);
            if (magR > 0.65f)
            {
                double angleR = Math.Atan2(ry, rx);
                if (angleR < 0) angleR += Math.PI * 2.0;
                int sectorR = (int)Math.Floor((angleR / (Math.PI * 2.0)) * PolarSectorCount) % PolarSectorCount;
                if (magR > _rightPolarSectors[sectorR])
                    _rightPolarSectors[sectorR] = magR;
            }

            // Compute circularity error only across populated outer sectors
            int populatedSectors = 0;
            double errSum = 0;
            for (int s = 0; s < PolarSectorCount; s++)
            {
                if (_leftPolarSectors[s] > 0.5f)
                {
                    errSum += Math.Abs(_leftPolarSectors[s] - 1.0);
                    populatedSectors++;
                }
            }

            LiveCircularityErrorPercent = populatedSectors > 0
                ? (errSum / populatedSectors) * 100.0
                : 0.0;

            if (ActiveCalibration != null)
            {
                if (magL < 0.15f)
                    ActiveCalibration.LeftDriftPercent = magL * 100.0;
                if (magR < 0.15f)
                    ActiveCalibration.RightDriftPercent = magR * 100.0;
                if (populatedSectors > 0)
                    ActiveCalibration.LeftCircularityErrorPercent = LiveCircularityErrorPercent;
            }
        }
    }

    private void RecomputeRollingStatistics()
    {
        if (_validSampleCount == 0)
        {
            CurrentDeltaMs = 0.0;
            CurrentHz = 0.0;
            CurrentJitterUs = 0.0;
            P99DeltaMs = 0.0;
            return;
        }

        double sum = 0;
        double maxDelta = 0;
        for (int i = 0; i < _validSampleCount; i++)
        {
            double val = _deltaHistoryMs[i];
            sum += val;
            if (val > maxDelta)
                maxDelta = val;
        }

        double meanMs = sum / _validSampleCount;
        double varianceSum = 0;
        for (int i = 0; i < _validSampleCount; i++)
        {
            double diff = _deltaHistoryMs[i] - meanMs;
            varianceSum += diff * diff;
        }

        double stdDevMs = Math.Sqrt(varianceSum / _validSampleCount);
        CurrentDeltaMs = meanMs;
        CurrentHz = meanMs > 0.001 ? 1000.0 / meanMs : 0.0;
        CurrentJitterUs = stdDevMs * 1000.0;
        P99DeltaMs = maxDelta;
    }

    public void CheckPacketTimeout()
    {
        if (!IsReceivingPackets)
        {
            CurrentHz = 0.0;
            CurrentDeltaMs = 0.0;
            CurrentJitterUs = 0.0;
            P99DeltaMs = 0.0;
        }
    }

    public int CopyWaveformSnapshot(double[] destination)
    {
        lock (_syncLock)
        {
            if (!IsReceivingPackets || _validSampleCount == 0)
            {
                Array.Clear(destination, 0, destination.Length);
                return 0;
            }

            int count = Math.Min(destination.Length, _validSampleCount);
            int start = _validSampleCount < WaveformBufferCapacity ? 0 : _writeIndex;
            for (int i = 0; i < count; i++)
            {
                int idx = (start + i) % WaveformBufferCapacity;
                destination[i] = _deltaHistoryMs[idx];
            }
            return count;
        }
    }

    public void CopyPolarHeatmapSnapshot(float[] leftSectors, float[] rightSectors)
    {
        lock (_syncLock)
        {
            Array.Copy(_leftPolarSectors, leftSectors, Math.Min(leftSectors.Length, PolarSectorCount));
            Array.Copy(_rightPolarSectors, rightSectors, Math.Min(rightSectors.Length, PolarSectorCount));
        }
    }

    public void Dispose()
    {
        // Clean release
    }
}
