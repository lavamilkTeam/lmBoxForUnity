using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Lmbox.AIAgent.Tools
{
    /// <summary>
    /// Ring buffer that mirrors Unity Console output so the agent can read logs.
    /// </summary>
    [InitializeOnLoad]
    public static class ConsoleLogCapture
    {
        private const int Capacity = 200;

        public struct Entry
        {
            public string Type;     // Log / Warning / Error
            public string Message;  // first line only
            public string Stack;    // trimmed
        }

        private static readonly Queue<Entry> _entries = new Queue<Entry>(Capacity);

        static ConsoleLogCapture()
        {
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            var e = new Entry
            {
                Type = type.ToString(),
                Message = FirstLine(condition),
                Stack = type == LogType.Error || type == LogType.Exception
                    ? Trim(stackTrace, 600)
                    : ""
            };
            lock (_entries)
            {
                if (_entries.Count >= Capacity)
                    _entries.Dequeue();
                _entries.Enqueue(e);
            }
        }

        public static string DumpRecent(int count, string typeFilter)
        {
            Entry[] snapshot;
            lock (_entries)
                snapshot = _entries.ToArray();

            var sb = new StringBuilder();
            int written = 0;
            for (int i = snapshot.Length - 1; i >= 0 && written < count; i--)
            {
                var e = snapshot[i];
                if (!string.IsNullOrEmpty(typeFilter) &&
                    !string.Equals(e.Type, typeFilter, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                sb.Append('[').Append(e.Type).Append("] ").Append(e.Message);
                if (!string.IsNullOrEmpty(e.Stack))
                    sb.Append('\n').Append(e.Stack);
                sb.Append("\n---\n");
                written++;
            }

            return written == 0 ? "(没有匹配的日志)" : sb.ToString();
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int idx = s.IndexOf('\n');
            var line = idx >= 0 ? s.Substring(0, idx) : s;
            return Trim(line, 300);
        }

        private static string Trim(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "...");
    }
}
