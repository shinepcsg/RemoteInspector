using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using RemoteInspector.Internal;
using UnityEngine;

namespace RemoteInspector
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Remote Inspector")]
    public sealed class RemoteInspectorBehaviour : MonoBehaviour
    {
        [Header("General")]
        [SerializeField] private string _displayName = "Remote Inspector";
        [SerializeField] private int _port = 7759;
        [SerializeField] private string _password = "changeme";
        [SerializeField] private bool _runOnEnable = true;
        [SerializeField] private int _logBufferSize = 2000;
        [SerializeField] private bool _dontDestroyOnLoad = true;
        [SerializeField] private bool _setRunInBackground = true;

        [Header("Runtime Status UI")]
        [SerializeField] private bool _showRuntimeStatusUi = true;
        [SerializeField] private bool _showCompactStatusBadge = true;
        [SerializeField] private bool _showExpandedStatusOnStart;
        [SerializeField] private KeyCode _statusToggleKey = KeyCode.F2;

        [Header("TLS")]
        [SerializeField] private bool _useTls;
        [SerializeField] private TextAsset _tlsCertificatePfx;
        [SerializeField] private string _tlsCertificatePassword = "remoteinspector";
        [SerializeField] private bool _autoGenerateSelfSignedCertificate = true;
        [SerializeField] private string _selfSignedCommonName = "Remote Inspector";

        private readonly ConcurrentQueue<IMainThreadWorkItem> _mainThreadQueue = new();

        private RemoteInspectorServer _server;
        private RemoteInspectorLogService _logService;
        private RemoteInspectorConsole _console;
        private bool _previousRunInBackground;
        private bool _runInBackgroundChanged;
        private bool _statusWindowVisible;
        private Rect _statusWindowRect = new(14f, 14f, 420f, 238f);
        private X509Certificate2 _cachedCertificate;
        private bool _isUsingGeneratedCertificate;

        public static RemoteInspectorBehaviour Instance { get; private set; }

        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? Application.productName : _displayName;

        public int Port => Mathf.Clamp(_port, 1024, 65535);

        public string Password => _password ?? string.Empty;

        public bool IsRunning => _server != null && _server.IsRunning;

        internal bool UseTls => _useTls;

        internal bool IsUsingGeneratedCertificate => _isUsingGeneratedCertificate;

        internal int ConnectedClientCount => _server?.ConnectedClientCount ?? 0;

        internal RemoteInspectorLogService LogService => _logService;

        internal RemoteInspectorConsole Console => _console;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (_dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }

            _statusWindowVisible = _showExpandedStatusOnStart;
            _logService = new RemoteInspectorLogService(_logBufferSize);
            _console = new RemoteInspectorConsole();
            _server = new RemoteInspectorServer(this);
        }

        private void OnEnable()
        {
            if (_runOnEnable)
            {
                StartInspector();
            }
        }

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var workItem))
            {
                workItem.Execute();
            }

            if (_showRuntimeStatusUi && Input.GetKeyDown(_statusToggleKey))
            {
                _statusWindowVisible = !_statusWindowVisible;
            }
        }

        private void OnDisable()
        {
            StopInspector();
        }

        private void OnDestroy()
        {
            StopInspector();
            _logService?.Dispose();
            _cachedCertificate?.Dispose();
            _cachedCertificate = null;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnGUI()
        {
            if (!_showRuntimeStatusUi)
            {
                return;
            }

            if (_showCompactStatusBadge)
            {
                DrawCompactStatusBadge();
            }

            if (_statusWindowVisible)
            {
                GUI.depth = 0;
                _statusWindowRect = GUI.Window(GetInstanceID(), _statusWindowRect, DrawStatusWindow, "Remote Inspector");
            }
        }

        public void StartInspector()
        {
            if (IsRunning)
            {
                return;
            }

            if (_setRunInBackground)
            {
                _previousRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
                _runInBackgroundChanged = true;
            }

            if (_useTls)
            {
                try
                {
                    GetServerCertificate();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[RemoteInspector] TLS initialization failed: {exception.Message}");
                    if (_runInBackgroundChanged)
                    {
                        Application.runInBackground = _previousRunInBackground;
                        _runInBackgroundChanged = false;
                    }

                    throw;
                }
            }

            _server.Start();
        }

        public void StopInspector()
        {
            _server?.Stop();

            if (_runInBackgroundChanged)
            {
                Application.runInBackground = _previousRunInBackground;
                _runInBackgroundChanged = false;
            }
        }

        public Task<T> RunOnMainThreadAsync<T>(Func<T> action)
        {
            var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _mainThreadQueue.Enqueue(new MainThreadWorkItem<T>(action, completionSource));
            return completionSource.Task;
        }

        public Task RunOnMainThreadAsync(Action action)
        {
            var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _mainThreadQueue.Enqueue(new MainThreadWorkItem<bool>(() =>
            {
                action();
                return true;
            }, completionSource));
            return completionSource.Task;
        }

        public bool ValidatePassword(string password)
        {
            return string.Equals(Password, password ?? string.Empty, StringComparison.Ordinal);
        }

        public string GetLocalUrl()
        {
            return $"{GetHttpScheme()}://127.0.0.1:{Port}";
        }

        public string GetLanUrl()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var address in host.AddressList)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    {
                        return $"{GetHttpScheme()}://{address}:{Port}";
                    }
                }
            }
            catch
            {
                // Ignore lookup failures and fall back to localhost.
            }

            return GetLocalUrl();
        }

        internal string GetHttpScheme()
        {
            return _useTls ? "https" : "http";
        }

        internal string GetWebSocketScheme()
        {
            return _useTls ? "wss" : "ws";
        }

        internal X509Certificate2 GetServerCertificate()
        {
            if (!_useTls)
            {
                return null;
            }

            if (_cachedCertificate != null)
            {
                return _cachedCertificate;
            }

            if (_tlsCertificatePfx != null && _tlsCertificatePfx.bytes != null && _tlsCertificatePfx.bytes.Length > 0)
            {
                _cachedCertificate = new X509Certificate2(
                    _tlsCertificatePfx.bytes,
                    _tlsCertificatePassword,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                _isUsingGeneratedCertificate = false;
                return _cachedCertificate;
            }

            if (_autoGenerateSelfSignedCertificate)
            {
                _cachedCertificate = CreateSelfSignedCertificate();
                _isUsingGeneratedCertificate = true;
                return _cachedCertificate;
            }

            throw new InvalidOperationException("TLS is enabled but no PFX certificate is assigned.");
        }

        internal InfoPayload GetInfo()
        {
            return new InfoPayload
            {
                productName = Application.productName,
                displayName = DisplayName,
                port = Port,
                version = "0.2.0",
                localUrl = GetLocalUrl(),
                lanUrl = GetLanUrl(),
                requiresPassword = !string.IsNullOrEmpty(Password),
                tlsEnabled = _useTls,
                usingGeneratedCertificate = _isUsingGeneratedCertificate,
                running = IsRunning,
                connectedClients = ConnectedClientCount
            };
        }

        public static void RegisterCommands<T>()
        {
            if (Instance == null)
            {
                return;
            }

            Instance._console.RegisterCommands(typeof(T));
        }

        private void DrawCompactStatusBadge()
        {
            var label = IsRunning
                ? $"{DisplayName}  {ConnectedClientCount} client(s)  {_statusToggleKey} UI"
                : $"{DisplayName} stopped  {_statusToggleKey} UI";
            var size = GUI.skin.box.CalcSize(new GUIContent(label));
            var rect = new Rect(12f, Screen.height - 30f, Mathf.Max(210f, size.x + 20f), 22f);
            GUI.Box(rect, label);
        }

        private void DrawStatusWindow(int windowId)
        {
            var rowY = 26f;
            GUI.Label(new Rect(12f, rowY, 388f, 18f), $"Name: {DisplayName}");
            rowY += 20f;
            GUI.Label(new Rect(12f, rowY, 388f, 18f), $"State: {(IsRunning ? "Running" : "Stopped")}   Clients: {ConnectedClientCount}");
            rowY += 20f;
            GUI.Label(new Rect(12f, rowY, 388f, 18f), $"Transport: {GetHttpScheme().ToUpperInvariant()} / {GetWebSocketScheme().ToUpperInvariant()}   Port: {Port}");
            rowY += 20f;
            GUI.Label(new Rect(12f, rowY, 388f, 18f), $"TLS Certificate: {GetCertificateSummary()}");
            rowY += 22f;

            GUI.Label(new Rect(12f, rowY, 90f, 18f), "Local URL");
            GUI.TextField(new Rect(104f, rowY - 2f, 300f, 20f), GetLocalUrl());
            rowY += 24f;

            GUI.Label(new Rect(12f, rowY, 90f, 18f), "LAN URL");
            GUI.TextField(new Rect(104f, rowY - 2f, 300f, 20f), GetLanUrl());
            rowY += 30f;

            if (!IsRunning)
            {
                if (GUI.Button(new Rect(12f, rowY, 96f, 24f), "Start"))
                {
                    StartInspector();
                }
            }
            else
            {
                if (GUI.Button(new Rect(12f, rowY, 96f, 24f), "Stop"))
                {
                    StopInspector();
                }
            }

            if (GUI.Button(new Rect(116f, rowY, 128f, 24f), _statusWindowVisible ? "Hide Panel" : "Show Panel"))
            {
                _statusWindowVisible = !_statusWindowVisible;
            }

            if (GUI.Button(new Rect(252f, rowY, 152f, 24f), "Refresh URLs"))
            {
                // Repaint only.
            }

            rowY += 30f;
            GUI.Label(new Rect(12f, rowY, 388f, 36f), "Tip: when TLS uses a self-signed certificate, open the HTTPS page once and accept the certificate in the browser.");

            GUI.DragWindow(new Rect(0f, 0f, 420f, 20f));
        }

        private string GetCertificateSummary()
        {
            if (!_useTls)
            {
                return "Disabled";
            }

            if (_cachedCertificate == null)
            {
                return _tlsCertificatePfx != null ? "Configured PFX" : (_autoGenerateSelfSignedCertificate ? "Self-signed on demand" : "Missing");
            }

            return _isUsingGeneratedCertificate ? "Self-signed (generated)" : _cachedCertificate.Subject;
        }

        private X509Certificate2 CreateSelfSignedCertificate()
        {
            using var rsa = RSA.Create(2048);
            var subjectName = string.IsNullOrWhiteSpace(_selfSignedCommonName) ? DisplayName : _selfSignedCommonName.Trim();
            var request = new CertificateRequest(
                $"CN={subjectName}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            sanBuilder.AddDnsName("localhost");
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                sanBuilder.AddDnsName(Dns.GetHostName());
                foreach (var address in host.AddressList)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        sanBuilder.AddIpAddress(address);
                    }
                }
            }
            catch
            {
                // Ignore SAN lookup failures.
            }

            request.CertificateExtensions.Add(sanBuilder.Build());

            using var certificate = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
            return new X509Certificate2(
                certificate.Export(X509ContentType.Pfx, _tlsCertificatePassword),
                _tlsCertificatePassword,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        private interface IMainThreadWorkItem
        {
            void Execute();
        }

        private sealed class MainThreadWorkItem<T> : IMainThreadWorkItem
        {
            private readonly Func<T> _action;
            private readonly TaskCompletionSource<T> _completionSource;

            public MainThreadWorkItem(Func<T> action, TaskCompletionSource<T> completionSource)
            {
                _action = action;
                _completionSource = completionSource;
            }

            public void Execute()
            {
                try
                {
                    _completionSource.TrySetResult(_action());
                }
                catch (Exception exception)
                {
                    _completionSource.TrySetException(exception);
                }
            }
        }
    }
}
