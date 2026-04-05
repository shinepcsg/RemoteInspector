using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aion.RemoteInspector;
using UnityEngine;

namespace Aion.RemoteInspector.Internal
{
    internal sealed class RemoteInspectorServer
    {
        private readonly AionRemoteInspector _inspector;
        private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();

        private CancellationTokenSource _cancellationTokenSource;
        private TcpListener _listener;
        private Task _acceptLoopTask;

        public RemoteInspectorServer(AionRemoteInspector inspector)
        {
            _inspector = inspector;
            _inspector.LogService.LogAdded += HandleLogAdded;
        }

        public bool IsRunning => _listener != null;

        public int ConnectedClientCount => _clients.Count;

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _inspector.Port);
            _listener.Start();
            _acceptLoopTask = AcceptLoopAsync(_cancellationTokenSource.Token);
            Debug.Log($"[AionRemoteInspector] Listening on {_inspector.GetLanUrl()}");
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            _cancellationTokenSource.Cancel();
            try
            {
                _listener.Stop();
            }
            catch
            {
                // Ignore shutdown errors.
            }

            foreach (var client in _clients.Values.ToArray())
            {
                client.Dispose();
            }

            _clients.Clear();
            _listener = null;
            _acceptLoopTask = null;
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient tcpClient;
                try
                {
                    tcpClient = await _listener.AcceptTcpClientAsync();
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[AionRemoteInspector] Accept failed: {exception.Message}");
                    continue;
                }

                _ = HandleClientAsync(tcpClient, cancellationToken);
            }
        }

        private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            using (tcpClient)
            {
                tcpClient.NoDelay = true;
                var networkStream = tcpClient.GetStream();
                Stream stream = networkStream;
                HttpRequestData request;
                try
                {
                    if (_inspector.UseTls)
                    {
                        stream = await CreateTransportStreamAsync(networkStream, cancellationToken);
                    }

                    using (stream)
                    {
                        request = await ReadHttpRequestAsync(stream, cancellationToken);
                        if (request == null)
                        {
                            return;
                        }

                        if (string.Equals(request.Path, "/api/info", StringComparison.OrdinalIgnoreCase))
                        {
                            var json = JsonUtility.ToJson(_inspector.GetInfo());
                            await SendHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), cancellationToken);
                            return;
                        }

                        if (IsWebSocketRequest(request))
                        {
                            if (!string.Equals(request.Path, "/ws", StringComparison.OrdinalIgnoreCase))
                            {
                                await SendHttpResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"), cancellationToken);
                                return;
                            }

                            await UpgradeToWebSocketAsync(tcpClient, stream, request, cancellationToken);
                            return;
                        }

                        if (!RemoteInspectorWebAssets.TryGetAsset(request.Path, out var contentType, out var data))
                        {
                            await SendHttpResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not Found"), cancellationToken);
                            return;
                        }

                        await SendHttpResponseAsync(stream, 200, "OK", contentType, data, cancellationToken);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[AionRemoteInspector] Request read failed: {exception.Message}");
                    return;
                }
            }
        }

        private async Task UpgradeToWebSocketAsync(TcpClient tcpClient, Stream stream, HttpRequestData request, CancellationToken cancellationToken)
        {
            if (!request.Headers.TryGetValue("Sec-WebSocket-Key", out var webSocketKey))
            {
                await SendHttpResponseAsync(stream, 400, "Bad Request", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Missing Sec-WebSocket-Key"), cancellationToken);
                return;
            }

            var acceptKey = ComputeWebSocketAccept(webSocketKey);
            var response = new StringBuilder();
            response.Append("HTTP/1.1 101 Switching Protocols\r\n");
            response.Append("Connection: Upgrade\r\n");
            response.Append("Upgrade: websocket\r\n");
            response.Append("Sec-WebSocket-Accept: ").Append(acceptKey).Append("\r\n");
            response.Append("\r\n");

            await WriteStringAsync(stream, response.ToString(), cancellationToken);

            var client = new ClientConnection(tcpClient, stream);
            _clients[client.Id] = client;
            try
            {
                await ReceiveLoopAsync(client, cancellationToken);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[AionRemoteInspector] Client error: {exception.Message}");
            }
            finally
            {
                _clients.TryRemove(client.Id, out _);
                client.Dispose();
            }
        }

        private async Task<Stream> CreateTransportStreamAsync(Stream networkStream, CancellationToken cancellationToken)
        {
            var sslStream = new SslStream(networkStream, false);
            await sslStream.AuthenticateAsServerAsync(_inspector.GetServerCertificate(), false, SslProtocols.Tls12, false);
            return sslStream;
        }

        private async Task ReceiveLoopAsync(ClientConnection client, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && client.IsConnected)
            {
                var frame = await ReadFrameAsync(client.Stream, cancellationToken);
                if (frame == null)
                {
                    break;
                }

                switch (frame.Opcode)
                {
                    case 0x8:
                        return;
                    case 0x9:
                        await client.SendControlFrameAsync(0xA, frame.Payload, cancellationToken);
                        continue;
                    case 0x1:
                        break;
                    default:
                        continue;
                }

                SocketEnvelope envelope;
                try
                {
                    envelope = JsonUtility.FromJson<SocketEnvelope>(Encoding.UTF8.GetString(frame.Payload));
                }
                catch (Exception exception)
                {
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("error", new ErrorPayload
                    {
                        message = $"Invalid message: {exception.Message}"
                    }), cancellationToken);
                    continue;
                }

                if (envelope == null || string.IsNullOrWhiteSpace(envelope.type))
                {
                    continue;
                }

                try
                {
                    await ProcessMessageAsync(client, envelope, cancellationToken);
                }
                catch (Exception exception)
                {
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("error", new ErrorPayload
                    {
                        message = exception.Message
                    }, envelope.requestId), cancellationToken);
                }
            }
        }

        private async Task ProcessMessageAsync(ClientConnection client, SocketEnvelope envelope, CancellationToken cancellationToken)
        {
            if (string.Equals(envelope.type, "auth", StringComparison.OrdinalIgnoreCase))
            {
                var payload = RemoteInspectorJson.FromPayloadJson<AuthRequestPayload>(envelope.payloadJson);
                var requiresPassword = !string.IsNullOrEmpty(_inspector.Password);
                client.IsAuthenticated = !requiresPassword || _inspector.ValidatePassword(payload.password);

                await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("auth/result", new AuthResponsePayload
                {
                    success = client.IsAuthenticated,
                    message = client.IsAuthenticated ? "Connected." : "Invalid password.",
                    info = _inspector.GetInfo()
                }, envelope.requestId), cancellationToken);
                return;
            }

            if (string.IsNullOrEmpty(_inspector.Password))
            {
                client.IsAuthenticated = true;
            }

            if (!client.IsAuthenticated)
            {
                await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("error", new ErrorPayload
                {
                    message = "Authentication is required."
                }, envelope.requestId), cancellationToken);
                return;
            }

            switch (envelope.type)
            {
                case "info/get":
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("info/result", _inspector.GetInfo(), envelope.requestId), cancellationToken);
                    break;

                case "hierarchy/get":
                {
                    var request = RemoteInspectorJson.FromPayloadJson<HierarchyRequestPayload>(envelope.payloadJson);
                    var response = await _inspector.RunOnMainThreadAsync(() => RemoteInspectorIntrospection.BuildHierarchy(request));
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("hierarchy/result", response, envelope.requestId), cancellationToken);
                    break;
                }

                case "inspect/get":
                {
                    var request = RemoteInspectorJson.FromPayloadJson<InspectorRequestPayload>(envelope.payloadJson);
                    var response = await _inspector.RunOnMainThreadAsync(() => RemoteInspectorIntrospection.BuildInspector(request.gameObjectInstanceId));
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("inspect/result", response, envelope.requestId), cancellationToken);
                    break;
                }

                case "member/set":
                {
                    var request = RemoteInspectorJson.FromPayloadJson<SetMemberRequestPayload>(envelope.payloadJson);
                    var response = await _inspector.RunOnMainThreadAsync(() => RemoteInspectorIntrospection.SetMember(request));
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("ack", response, envelope.requestId), cancellationToken);
                    BroadcastSceneChanged();
                    break;
                }

                case "gameobject/set-active":
                {
                    var request = RemoteInspectorJson.FromPayloadJson<SetActiveRequestPayload>(envelope.payloadJson);
                    var response = await _inspector.RunOnMainThreadAsync(() => RemoteInspectorIntrospection.SetActive(request));
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("ack", response, envelope.requestId), cancellationToken);
                    BroadcastSceneChanged();
                    break;
                }

                case "gameobject/create-empty":
                {
                    var request = RemoteInspectorJson.FromPayloadJson<GameObjectOperationPayload>(envelope.payloadJson);
                    var response = await _inspector.RunOnMainThreadAsync(() => RemoteInspectorIntrospection.CreateEmpty(request));
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("ack", response, envelope.requestId), cancellationToken);
                    BroadcastSceneChanged();
                    break;
                }

                case "gameobject/duplicate":
                {
                    var request = RemoteInspectorJson.FromPayloadJson<GameObjectOperationPayload>(envelope.payloadJson);
                    var response = await _inspector.RunOnMainThreadAsync(() => RemoteInspectorIntrospection.Duplicate(request));
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("ack", response, envelope.requestId), cancellationToken);
                    BroadcastSceneChanged();
                    break;
                }

                case "gameobject/destroy":
                {
                    var request = RemoteInspectorJson.FromPayloadJson<GameObjectOperationPayload>(envelope.payloadJson);
                    var response = await _inspector.RunOnMainThreadAsync(() => RemoteInspectorIntrospection.DestroyGameObject(request));
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("ack", response, envelope.requestId), cancellationToken);
                    BroadcastSceneChanged();
                    break;
                }

                case "component/add":
                {
                    var request = RemoteInspectorJson.FromPayloadJson<AddComponentRequestPayload>(envelope.payloadJson);
                    var response = await _inspector.RunOnMainThreadAsync(() => RemoteInspectorIntrospection.AddComponent(request));
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("ack", response, envelope.requestId), cancellationToken);
                    BroadcastSceneChanged();
                    break;
                }

                case "component/destroy":
                {
                    var request = RemoteInspectorJson.FromPayloadJson<DestroyComponentRequestPayload>(envelope.payloadJson);
                    var response = await _inspector.RunOnMainThreadAsync(() => RemoteInspectorIntrospection.DestroyComponent(request));
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("ack", response, envelope.requestId), cancellationToken);
                    BroadcastSceneChanged();
                    break;
                }

                case "logs/get":
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("logs/result", new LogsResponsePayload
                    {
                        entries = _inspector.LogService.GetSnapshot()
                    }, envelope.requestId), cancellationToken);
                    break;

                case "logs/clear":
                    _inspector.LogService.Clear();
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("ack", new AckPayload
                    {
                        message = "Logs cleared.",
                        instanceId = 0
                    }, envelope.requestId), cancellationToken);
                    await BroadcastAsync(RemoteInspectorJson.CreateEnvelope("logs/cleared", new EmptyPayload()), cancellationToken);
                    break;

                case "console/execute":
                {
                    var request = RemoteInspectorJson.FromPayloadJson<ConsoleRequestPayload>(envelope.payloadJson);
                    try
                    {
                        var output = await _inspector.RunOnMainThreadAsync(() => _inspector.Console.Execute(request.command));
                        await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("console/result", new ConsoleResponsePayload
                        {
                            success = true,
                            output = output
                        }, envelope.requestId), cancellationToken);
                        BroadcastSceneChanged();
                    }
                    catch (Exception exception)
                    {
                        await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("console/result", new ConsoleResponsePayload
                        {
                            success = false,
                            output = exception.Message
                        }, envelope.requestId), cancellationToken);
                    }

                    break;
                }

                default:
                    await client.SendEnvelopeAsync(RemoteInspectorJson.CreateEnvelope("error", new ErrorPayload
                    {
                        message = $"Unknown message type '{envelope.type}'."
                    }, envelope.requestId), cancellationToken);
                    break;
            }
        }

        private void HandleLogAdded(RemoteLogEntryDto entry)
        {
            if (_clients.IsEmpty)
            {
                return;
            }

            var envelope = RemoteInspectorJson.CreateEnvelope("log/append", entry);
            _ = BroadcastAsync(envelope, CancellationToken.None);
        }

        private void BroadcastSceneChanged()
        {
            var envelope = RemoteInspectorJson.CreateEnvelope("scene/changed", new EmptyPayload());
            _ = BroadcastAsync(envelope, CancellationToken.None);
            _ = BroadcastSceneChangedDelayedAsync();
        }

        private async Task BroadcastSceneChangedDelayedAsync()
        {
            try
            {
                await Task.Delay(120);
                await BroadcastAsync(RemoteInspectorJson.CreateEnvelope("scene/changed", new EmptyPayload()), CancellationToken.None);
            }
            catch
            {
                // Ignore shutdown and transport timing errors.
            }
        }

        private async Task BroadcastAsync(SocketEnvelope envelope, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            foreach (var client in _clients.Values)
            {
                if (!client.IsConnected || (!client.IsAuthenticated && !string.IsNullOrEmpty(_inspector.Password)))
                {
                    continue;
                }

                tasks.Add(client.SendEnvelopeAsync(envelope, cancellationToken));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // Individual client failures are cleaned up by their receive loops.
            }
        }

        private static async Task<HttpRequestData> ReadHttpRequestAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[1024];
            while (buffer.Length < 64 * 1024)
            {
                var bytesRead = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken);
                if (bytesRead <= 0)
                {
                    return null;
                }

                buffer.Write(chunk, 0, bytesRead);
                if (HasHeaderTerminator(buffer.GetBuffer(), (int)buffer.Length))
                {
                    break;
                }
            }

            var requestText = Encoding.UTF8.GetString(buffer.ToArray());
            var headerEndIndex = requestText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEndIndex < 0)
            {
                return null;
            }

            var headerLines = requestText.Substring(0, headerEndIndex).Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (headerLines.Length == 0)
            {
                return null;
            }

            var requestLine = headerLines[0].Split(' ');
            if (requestLine.Length < 2)
            {
                return null;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < headerLines.Length; index++)
            {
                var separatorIndex = headerLines[index].IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = headerLines[index].Substring(0, separatorIndex).Trim();
                var value = headerLines[index].Substring(separatorIndex + 1).Trim();
                headers[key] = value;
            }

            return new HttpRequestData
            {
                Method = requestLine[0],
                Path = requestLine[1],
                Headers = headers
            };
        }

        private static bool IsWebSocketRequest(HttpRequestData request)
        {
            return request.Headers.TryGetValue("Connection", out var connection) &&
                   connection.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   request.Headers.TryGetValue("Upgrade", out var upgrade) &&
                   string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasHeaderTerminator(byte[] buffer, int length)
        {
            for (var index = 3; index < length; index++)
            {
                if (buffer[index - 3] == '\r' &&
                    buffer[index - 2] == '\n' &&
                    buffer[index - 1] == '\r' &&
                    buffer[index] == '\n')
                {
                    return true;
                }
            }

            return false;
        }

        private static string ComputeWebSocketAccept(string webSocketKey)
        {
            using var sha1 = SHA1.Create();
            var input = Encoding.ASCII.GetBytes(webSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
            return Convert.ToBase64String(sha1.ComputeHash(input));
        }

        private static async Task SendHttpResponseAsync(Stream stream, int statusCode, string reasonPhrase, string contentType, byte[] body, CancellationToken cancellationToken)
        {
            var header = new StringBuilder();
            header.Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(reasonPhrase).Append("\r\n");
            header.Append("Content-Type: ").Append(contentType).Append("\r\n");
            header.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n");
            header.Append("Connection: close\r\n");
            header.Append("\r\n");

            await WriteStringAsync(stream, header.ToString(), cancellationToken);
            if (body != null && body.Length > 0)
            {
                await stream.WriteAsync(body, 0, body.Length, cancellationToken);
            }
        }

        private static async Task WriteStringAsync(Stream stream, string value, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        }

        private static async Task<WebSocketFrame> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
        {
            var header = await ReadExactAsync(stream, 2, cancellationToken);
            if (header == null)
            {
                return null;
            }

            var firstByte = header[0];
            var secondByte = header[1];
            var opcode = firstByte & 0x0F;
            var masked = (secondByte & 0x80) != 0;
            ulong payloadLength = (ulong)(secondByte & 0x7F);
            if (payloadLength == 126)
            {
                var extended = await ReadExactAsync(stream, 2, cancellationToken);
                if (extended == null)
                {
                    return null;
                }

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(extended);
                }

                payloadLength = BitConverter.ToUInt16(extended, 0);
            }
            else if (payloadLength == 127)
            {
                var extended = await ReadExactAsync(stream, 8, cancellationToken);
                if (extended == null)
                {
                    return null;
                }

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(extended);
                }

                payloadLength = BitConverter.ToUInt64(extended, 0);
            }

            byte[] mask = null;
            if (masked)
            {
                mask = await ReadExactAsync(stream, 4, cancellationToken);
                if (mask == null)
                {
                    return null;
                }
            }

            var payload = payloadLength == 0
                ? Array.Empty<byte>()
                : await ReadExactAsync(stream, (int)payloadLength, cancellationToken);
            if (payload == null)
            {
                return null;
            }

            if (masked)
            {
                for (var index = 0; index < payload.Length; index++)
                {
                    payload[index] ^= mask[index % 4];
                }
            }

            return new WebSocketFrame
            {
                Opcode = opcode,
                Payload = payload
            };
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
        {
            var buffer = new byte[length];
            var totalRead = 0;
            while (totalRead < length)
            {
                var bytesRead = await stream.ReadAsync(buffer, totalRead, length - totalRead, cancellationToken);
                if (bytesRead <= 0)
                {
                    return null;
                }

                totalRead += bytesRead;
            }

            return buffer;
        }

        private sealed class ClientConnection : IDisposable
        {
            private readonly SemaphoreSlim _sendLock = new(1, 1);

            public ClientConnection(TcpClient tcpClient, Stream stream)
            {
                Id = Guid.NewGuid().ToString("N");
                TcpClient = tcpClient;
                Stream = stream;
            }

            public string Id { get; }

            public TcpClient TcpClient { get; }

            public Stream Stream { get; }

            public bool IsAuthenticated { get; set; }

            public bool IsConnected => TcpClient != null && TcpClient.Connected;

            public async Task SendEnvelopeAsync(SocketEnvelope envelope, CancellationToken cancellationToken)
            {
                var json = JsonUtility.ToJson(envelope);
                await SendTextFrameAsync(json, cancellationToken);
            }

            public async Task SendControlFrameAsync(int opcode, byte[] payload, CancellationToken cancellationToken)
            {
                await SendFrameAsync(opcode, payload ?? Array.Empty<byte>(), cancellationToken);
            }

            public async Task SendTextFrameAsync(string text, CancellationToken cancellationToken)
            {
                await SendFrameAsync(0x1, Encoding.UTF8.GetBytes(text ?? string.Empty), cancellationToken);
            }

            public async Task SendFrameAsync(int opcode, byte[] payload, CancellationToken cancellationToken)
            {
                if (!IsConnected)
                {
                    return;
                }

                await _sendLock.WaitAsync(cancellationToken);
                try
                {
                    var header = new List<byte>(10)
                    {
                        (byte)(0x80 | (opcode & 0x0F))
                    };

                    if (payload.Length <= 125)
                    {
                        header.Add((byte)payload.Length);
                    }
                    else if (payload.Length <= ushort.MaxValue)
                    {
                        header.Add(126);
                        var shortLength = BitConverter.GetBytes((ushort)payload.Length);
                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(shortLength);
                        }

                        header.AddRange(shortLength);
                    }
                    else
                    {
                        header.Add(127);
                        var longLength = BitConverter.GetBytes((ulong)payload.Length);
                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(longLength);
                        }

                        header.AddRange(longLength);
                    }

                    await Stream.WriteAsync(header.ToArray(), 0, header.Count, cancellationToken);
                    if (payload.Length > 0)
                    {
                        await Stream.WriteAsync(payload, 0, payload.Length, cancellationToken);
                    }
                }
                finally
                {
                    _sendLock.Release();
                }
            }

            public void Dispose()
            {
                try
                {
                    Stream?.Close();
                }
                catch
                {
                    // Ignore client shutdown errors.
                }

                try
                {
                    TcpClient?.Close();
                }
                catch
                {
                    // Ignore client shutdown errors.
                }

                _sendLock.Dispose();
            }
        }

        private sealed class HttpRequestData
        {
            public string Method;
            public string Path;
            public Dictionary<string, string> Headers;
        }

        private sealed class WebSocketFrame
        {
            public int Opcode;
            public byte[] Payload;
        }
    }
}
