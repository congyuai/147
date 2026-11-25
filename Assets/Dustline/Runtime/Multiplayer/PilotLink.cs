using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;

namespace Dustline
{
    internal static class PilotAddress
    {
        public const string Local = "127.0.0.1";
        public const ushort Port = 7777;
        public const int MaxPemLength = FixedPEMString.MaxLength;
        public static bool TryParse(string host, string port, out NetworkEndpoint endpoint, out bool loopback)
        {
            endpoint = default;
            loopback = false;
            host = host?.Trim();
            if (host == null || host.Length > 15 || !IPAddress.TryParse(host, out var address)
                || address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != host
                || !ushort.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number == 0) return false;
            loopback = host == Local;
            byte[] ip = address.GetAddressBytes();
            bool isPrivate = ip[0] == 10 || ip[0] == 172 && ip[1] >= 16 && ip[1] <= 31 || ip[0] == 192 && ip[1] == 168
                || ip[0] == 100 && ip[1] >= 64 && ip[1] <= 127;
            return (loopback || isPrivate) && NetworkEndpoint.TryParse(host, number, out endpoint, NetworkFamily.Ipv4);
        }

        public static bool IsCertificate(string pem) => !string.IsNullOrWhiteSpace(pem) && Encoding.UTF8.GetByteCount(pem) <= FixedPEMString.MaxLength
            && pem.Contains("-----BEGIN CERTIFICATE-----") && pem.Contains("-----END CERTIFICATE-----") && !pem.Contains("PRIVATE KEY-----");

        public static bool IsPrivateKey(string pem) => !string.IsNullOrWhiteSpace(pem) && Encoding.UTF8.GetByteCount(pem) <= FixedPEMString.MaxLength
            && (pem.Contains("-----BEGIN PRIVATE KEY-----") || pem.Contains("-----BEGIN RSA PRIVATE KEY-----") || pem.Contains("-----BEGIN EC PRIVATE KEY-----"));

        public static bool IsServerName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 253
            && Uri.CheckHostName(name) == UriHostNameType.Dns;
    }

    internal sealed class PilotLink : IDisposable
    {
        NetworkDriver driver;
        NetworkPipeline reliable;

        public void StartHost(NetworkEndpoint endpoint, string certificate, string key)
        {
            if ((!endpoint.IsLoopback || certificate != null || key != null)
                && (!PilotAddress.IsCertificate(certificate) || !PilotAddress.IsPrivateKey(key)))
                throw new InvalidOperationException("DTLS needs a server certificate and key.");
            try
            {
                Create(certificate, key, null, null);
                if (driver.Bind(endpoint) != 0 || driver.Listen() != 0)
                    throw new InvalidOperationException("The pilot endpoint could not listen.");
            }
            catch { Dispose(); throw; }
        }

        public NetworkConnection StartClient(NetworkEndpoint endpoint, string trust, string name)
        {
            if ((!endpoint.IsLoopback || trust != null || name != null) && (!PilotAddress.IsCertificate(trust) || !PilotAddress.IsServerName(name)))
                throw new InvalidOperationException("A trusted CA and server name are required.");
            try
            {
                Create(null, null, trust, name);
                var connection = driver.Connect(endpoint);
                if (!connection.IsCreated) throw new InvalidOperationException("The pilot endpoint could not connect.");
                return connection;
            }
            catch { Dispose(); throw; }
        }

        void Create(string certificate, string key, string trust, string name)
        {
            var settings = new NetworkSettings(Allocator.Temp);
            try
            {
                settings.WithNetworkConfigParameters(connectTimeoutMS: 600, maxConnectAttempts: 12, disconnectTimeoutMS: 8000,
                    heartbeatTimeoutMS: 500, reconnectionTimeoutMS: 0, receiveQueueCapacity: 64, sendQueueCapacity: 64, maxMessageSize: 1200);
                if (certificate != null) settings.WithSecureServerParameters(certificate, key);
                else if (trust != null) settings.WithSecureClientParameters(trust, name);
                driver = NetworkDriver.Create(settings);
                reliable = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            }
            finally { settings.Dispose(); }
        }

        public ushort LocalPort => driver.GetLocalEndpoint().Port;
        public void Pump() => driver.ScheduleUpdate().Complete();
        public NetworkConnection Accept() => driver.Accept();

        public NetworkEvent.Type Receive(byte[] bytes, out NetworkConnection from, out int length)
        {
            var type = driver.PopEvent(out from, out var reader);
            length = 0;
            if (type != NetworkEvent.Type.Data || reader.Length > bytes.Length) return type;
            length = reader.Length;
            for (int i = 0; i < length; i++) bytes[i] = reader.ReadByte();
            return type;
        }

        public bool Send(NetworkConnection to, byte[] bytes, int length, bool control = false)
        {
            if (driver.BeginSend(control ? reliable : NetworkPipeline.Null, to, out var writer, length) != 0) return false;
            for (int i = 0; i < length; i++) writer.WriteByte(bytes[i]);
            return driver.EndSend(writer) == length;
        }

        public void Disconnect(NetworkConnection connection)
        {
            if (driver.IsCreated && connection.IsCreated) driver.Disconnect(connection);
        }

        public void Dispose()
        {
            if (!driver.IsCreated) return;
            try { driver.ScheduleUpdate().Complete(); }
            finally { driver.Dispose(); driver = default; }
        }
    }
}
