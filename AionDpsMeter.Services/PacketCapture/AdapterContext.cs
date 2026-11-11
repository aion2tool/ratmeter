using SharpPcap;
using System;
using System.Collections.Generic;
using System.Text;

namespace AionDpsMeter.Services.PacketCapture
{
    internal enum AdapterKind { Loopback, Physical, Skip }

    internal sealed class AdapterContext
    {
        public ICaptureDevice Device { get; }
        public AdapterKind Kind { get; }
        public bool IsOpen { get; set; }

        /// <summary>
        /// Packet-arrival handler attached to <see cref="Device"/>. Kept so it is
        /// attached only once across detection resets instead of stacking up.
        /// </summary>
        public PacketArrivalEventHandler? ArrivalHandler { get; set; }

        public AdapterContext(ICaptureDevice device, AdapterKind kind)
        {
            Device = device;
            Kind = kind;
        }
    }
}
