using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Aion.RemoteInspector.Internal
{
    internal sealed class RemoteInspectorLogService : IDisposable
    {
        private readonly object _sync = new();
        private readonly List<RemoteLogEntryDto> _entries;
        private readonly int _maxEntries;
        private int _nextIndex;

        public RemoteInspectorLogService(int maxEntries)
        {
            _maxEntries = Mathf.Max(100, maxEntries);
            _entries = new List<RemoteLogEntryDto>(_maxEntries);
            Application.logMessageReceivedThreaded += HandleLogReceived;
        }

        public event Action<RemoteLogEntryDto> LogAdded;

        public RemoteLogEntryDto[] GetSnapshot()
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _entries.Clear();
            }
        }

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= HandleLogReceived;
        }

        private void HandleLogReceived(string condition, string stackTrace, LogType type)
        {
            var entry = new RemoteLogEntryDto
            {
                index = Interlocked.Increment(ref _nextIndex),
                timestamp = DateTimeOffset.Now.ToString("HH:mm:ss.fff"),
                logType = type.ToString(),
                message = condition ?? string.Empty,
                stackTrace = stackTrace ?? string.Empty
            };

            lock (_sync)
            {
                if (_entries.Count >= _maxEntries)
                {
                    _entries.RemoveAt(0);
                }

                _entries.Add(entry);
            }

            try
            {
                LogAdded?.Invoke(entry);
            }
            catch
            {
                // Logging should never break the host application.
            }
        }
    }
}
