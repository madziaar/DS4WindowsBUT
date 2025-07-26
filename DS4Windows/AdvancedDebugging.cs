/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DS4Windows
{
    public enum DebugLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Critical = 5
    }

    public class DebugSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public string Description { get; set; }
        public List<DebugEvent> Events { get; set; } = new List<DebugEvent>();
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class DebugEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public DebugLevel Level { get; set; }
        public string Category { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
        public Exception Exception { get; set; }
        public string StackTrace { get; set; }
    }

    public class InputReplayData
    {
        public DateTime Timestamp { get; set; }
        public DS4State ControllerState { get; set; }
        public string EventType { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class PacketCapture
    {
        public DateTime Timestamp { get; set; }
        public byte[] RawData { get; set; }
        public string Direction { get; set; } // "Input" or "Output"
        public int ControllerId { get; set; }
        public string PacketType { get; set; }
        public Dictionary<string, object> ParsedData { get; set; } = new Dictionary<string, object>();
    }

    public class PerformanceSnapshot
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public double Latency { get; set; }
        public int ThreadCount { get; set; }
        public long HandleCount { get; set; }
        public Dictionary<string, double> CustomMetrics { get; set; } = new Dictionary<string, double>();
    }

    public class AdvancedDebugging : IDisposable
    {
        private readonly List<DebugSession> activeSessions;
        private readonly Queue<DebugEvent> eventQueue;
        private readonly List<InputReplayData> inputReplayBuffer;
        private readonly List<PacketCapture> packetCaptureBuffer;
        private readonly List<PerformanceSnapshot> performanceHistory;
        private readonly Timer performanceTimer;
        private readonly FileStream logFileStream;
        private readonly StreamWriter logWriter;
        private bool isCapturingInput;
        private bool isCapturingPackets;
        private bool disposed;

        // Configuration
        private DebugLevel minimumLogLevel = DebugLevel.Debug;
        private int maxEventQueueSize = 10000;
        private int maxReplayBufferSize = 5000;
        private int maxPacketCaptureSize = 1000;
        private int maxPerformanceHistory = 2000;
        private string logDirectory;

        public event EventHandler<AdvancedDebugEventArgs> DebugEventLogged;
        public event EventHandler<PerformanceSnapshotEventArgs> PerformanceSnapshotTaken;

        public bool IsCapturingInput => isCapturingInput;
        public bool IsCapturingPackets => isCapturingPackets;
        public int ActiveSessionCount => activeSessions.Count;
        public IReadOnlyList<DebugSession> ActiveSessions => activeSessions.AsReadOnly();

        public AdvancedDebugging()
        {
            activeSessions = new List<DebugSession>();
            eventQueue = new Queue<DebugEvent>();
            inputReplayBuffer = new List<InputReplayData>();
            packetCaptureBuffer = new List<PacketCapture>();
            performanceHistory = new List<PerformanceSnapshot>();

            // Set up logging directory
            logDirectory = Path.Combine(Global.appdatapath, "DebugLogs");
            Directory.CreateDirectory(logDirectory);

            // Initialize log file
            var logFileName = $"DS4Windows_Debug_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            var logFilePath = Path.Combine(logDirectory, logFileName);
            
            try
            {
                logFileStream = new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                logWriter = new StreamWriter(logFileStream, Encoding.UTF8) { AutoFlush = true };
                
                LogEvent(DebugLevel.Info, "Debugging", "Advanced debugging system initialized", "AdvancedDebugging");
            }
            catch (Exception ex)
            {
                AppLogger.LogToGui($"Failed to initialize debug logging: {ex.Message}", true);
            }

            // Start performance monitoring
            performanceTimer = new Timer(TakePerformanceSnapshot, null, 
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public string StartDebugSession(string description)
        {
            var session = new DebugSession
            {
                Description = description,
                StartTime = DateTime.UtcNow
            };

            session.Metadata["MachineName"] = Environment.MachineName;
            session.Metadata["OSVersion"] = Environment.OSVersion.ToString();
            session.Metadata["DS4WindowsVersion"] = Global.exeversion;
            session.Metadata["ProcessorCount"] = Environment.ProcessorCount;

            activeSessions.Add(session);
            LogEvent(DebugLevel.Info, "Session", $"Debug session started: {description}", "AdvancedDebugging");

            return session.SessionId;
        }

        public void EndDebugSession(string sessionId)
        {
            var session = activeSessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session != null)
            {
                session.EndTime = DateTime.UtcNow;
                activeSessions.Remove(session);
                
                // Save session to file
                SaveDebugSession(session);
                
                LogEvent(DebugLevel.Info, "Session", $"Debug session ended: {session.Description}", "AdvancedDebugging");
            }
        }

        public void LogEvent(DebugLevel level, string category, string message, string source, Exception exception = null, Dictionary<string, object> data = null)
        {
            if (level < minimumLogLevel) return;

            var debugEvent = new DebugEvent
            {
                Level = level,
                Category = category,
                Message = message,
                Source = source,
                Exception = exception,
                StackTrace = exception?.StackTrace ?? (level >= DebugLevel.Error ? Environment.StackTrace : null),
                Data = data ?? new Dictionary<string, object>()
            };

            // Add to queue
            lock (eventQueue)
            {
                eventQueue.Enqueue(debugEvent);
                while (eventQueue.Count > maxEventQueueSize)
                {
                    eventQueue.Dequeue();
                }
            }

            // Add to active sessions
            foreach (var session in activeSessions)
            {
                session.Events.Add(debugEvent);
            }

            // Write to log file
            WriteToLogFile(debugEvent);

            // Trigger event
            DebugEventLogged?.Invoke(this, new AdvancedDebugEventArgs(debugEvent));
        }

        private void WriteToLogFile(DebugEvent debugEvent)
        {
            if (logWriter == null) return;

            try
            {
                var logLine = FormatLogEvent(debugEvent);
                logWriter.WriteLine(logLine);
            }
            catch (Exception ex)
            {
                // Avoid recursive logging
                Console.WriteLine($"Failed to write to debug log: {ex.Message}");
            }
        }

        private string FormatLogEvent(DebugEvent debugEvent)
        {
            var sb = new StringBuilder();
            sb.Append($"[{debugEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
            sb.Append($"[{debugEvent.Level}] ");
            sb.Append($"[{debugEvent.Category}] ");
            sb.Append($"[{debugEvent.Source}] ");
            sb.Append(debugEvent.Message);

            if (debugEvent.Data.Count > 0)
            {
                sb.Append(" | Data: ");
                sb.Append(string.Join(", ", debugEvent.Data.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            }

            if (debugEvent.Exception != null)
            {
                sb.AppendLine();
                sb.Append($"Exception: {debugEvent.Exception}");
            }

            return sb.ToString();
        }

        public void StartInputCapture()
        {
            if (isCapturingInput) return;

            isCapturingInput = true;
            inputReplayBuffer.Clear();
            
            LogEvent(DebugLevel.Info, "InputCapture", "Input replay capture started", "AdvancedDebugging");
        }

        public void StopInputCapture()
        {
            if (!isCapturingInput) return;

            isCapturingInput = false;
            
            LogEvent(DebugLevel.Info, "InputCapture", $"Input replay capture stopped. Captured {inputReplayBuffer.Count} events", "AdvancedDebugging");
        }

        public void CaptureControllerInput(DS4State state, int controllerId, string eventType = "Input")
        {
            if (!isCapturingInput) return;

            var replayData = new InputReplayData
            {
                Timestamp = DateTime.UtcNow,
                ControllerState = new DS4State(state), // Deep copy
                EventType = eventType,
                Metadata = new Dictionary<string, object>
                {
                    ["ControllerId"] = controllerId,
                    ["Battery"] = state.Battery,
                    ["Charging"] = state.Charging
                }
            };

            lock (inputReplayBuffer)
            {
                inputReplayBuffer.Add(replayData);
                while (inputReplayBuffer.Count > maxReplayBufferSize)
                {
                    inputReplayBuffer.RemoveAt(0);
                }
            }
        }

        public void StartPacketCapture()
        {
            if (isCapturingPackets) return;

            isCapturingPackets = true;
            packetCaptureBuffer.Clear();
            
            LogEvent(DebugLevel.Info, "PacketCapture", "Packet capture started", "AdvancedDebugging");
        }

        public void StopPacketCapture()
        {
            if (!isCapturingPackets) return;

            isCapturingPackets = false;
            
            LogEvent(DebugLevel.Info, "PacketCapture", $"Packet capture stopped. Captured {packetCaptureBuffer.Count} packets", "AdvancedDebugging");
        }

        public void CapturePacket(byte[] data, string direction, int controllerId, string packetType = "Unknown")
        {
            if (!isCapturingPackets || data == null) return;

            var packet = new PacketCapture
            {
                Timestamp = DateTime.UtcNow,
                RawData = (byte[])data.Clone(),
                Direction = direction,
                ControllerId = controllerId,
                PacketType = packetType,
                ParsedData = ParsePacketData(data, packetType)
            };

            lock (packetCaptureBuffer)
            {
                packetCaptureBuffer.Add(packet);
                while (packetCaptureBuffer.Count > maxPacketCaptureSize)
                {
                    packetCaptureBuffer.RemoveAt(0);
                }
            }
        }

        private Dictionary<string, object> ParsePacketData(byte[] data, string packetType)
        {
            var parsed = new Dictionary<string, object>
            {
                ["Length"] = data.Length,
                ["Checksum"] = CalculateChecksum(data)
            };

            try
            {
                // Parse different packet types
                switch (packetType.ToLower())
                {
                    case "input":
                        ParseInputPacket(data, parsed);
                        break;
                    case "output":
                        ParseOutputPacket(data, parsed);
                        break;
                    case "feature":
                        ParseFeaturePacket(data, parsed);
                        break;
                }
            }
            catch (Exception ex)
            {
                parsed["ParseError"] = ex.Message;
            }

            return parsed;
        }

        private void ParseInputPacket(byte[] data, Dictionary<string, object> parsed)
        {
            if (data.Length >= 64) // Standard DS4 input report
            {
                parsed["ReportId"] = data[0];
                parsed["LeftStickX"] = data[1];
                parsed["LeftStickY"] = data[2];
                parsed["RightStickX"] = data[3];
                parsed["RightStickY"] = data[4];
                parsed["DpadButtons"] = data[5];
                parsed["FaceButtons"] = data[6];
                parsed["L2Trigger"] = data[8];
                parsed["R2Trigger"] = data[9];
                
                if (data.Length > 30)
                {
                    parsed["BatteryLevel"] = data[30] & 0x0F;
                    parsed["IsCharging"] = (data[30] & 0x10) != 0;
                }
            }
        }

        private void ParseOutputPacket(byte[] data, Dictionary<string, object> parsed)
        {
            if (data.Length >= 32)
            {
                parsed["ReportId"] = data[0];
                parsed["RumbleRight"] = data[4];
                parsed["RumbleLeft"] = data[5];
                parsed["LightbarRed"] = data[6];
                parsed["LightbarGreen"] = data[7];
                parsed["LightbarBlue"] = data[8];
                parsed["FlashOn"] = data[9];
                parsed["FlashOff"] = data[10];
            }
        }

        private void ParseFeaturePacket(byte[] data, Dictionary<string, object> parsed)
        {
            if (data.Length > 0)
            {
                parsed["FeatureId"] = data[0];
                parsed["DataLength"] = data.Length - 1;
            }
        }

        private uint CalculateChecksum(byte[] data)
        {
            uint checksum = 0;
            foreach (byte b in data)
            {
                checksum += b;
            }
            return checksum;
        }

        private void TakePerformanceSnapshot(object state)
        {
            if (disposed) return;

            try
            {
                var process = Process.GetCurrentProcess();
                var snapshot = new PerformanceSnapshot
                {
                    Timestamp = DateTime.UtcNow,
                    MemoryUsage = process.WorkingSet64,
                    ThreadCount = process.Threads.Count,
                    HandleCount = process.HandleCount
                };

                // Get CPU usage (simplified)
                try
                {
                    using (var pc = new PerformanceCounter("Process", "% Processor Time", process.ProcessName))
                    {
                        pc.NextValue(); // First call returns 0
                        Thread.Sleep(100);
                        snapshot.CpuUsage = pc.NextValue() / Environment.ProcessorCount;
                    }
                }
                catch
                {
                    snapshot.CpuUsage = 0;
                }

                // Add custom metrics
                if (App.rootHub != null)
                {
                    snapshot.CustomMetrics["ActiveControllers"] = App.rootHub.activeControllers;
                    
                    var firstDevice = App.rootHub.DS4Controllers?.FirstOrDefault(d => d != null);
                    if (firstDevice != null)
                    {
                        snapshot.Latency = firstDevice.Latency;
                        snapshot.CustomMetrics["BatteryLevel"] = firstDevice.getBattery();
                    }
                }

                lock (performanceHistory)
                {
                    performanceHistory.Add(snapshot);
                    while (performanceHistory.Count > maxPerformanceHistory)
                    {
                        performanceHistory.RemoveAt(0);
                    }
                }

                PerformanceSnapshotTaken?.Invoke(this, new PerformanceSnapshotEventArgs(snapshot));
            }
            catch (Exception ex)
            {
                LogEvent(DebugLevel.Warning, "Performance", $"Error taking performance snapshot: {ex.Message}", "AdvancedDebugging");
            }
        }

        public async Task<string> ExportDebugDataAsync(string format = "json")
        {
            var exportData = new
            {
                ExportTime = DateTime.UtcNow,
                Sessions = activeSessions,
                RecentEvents = eventQueue.TakeLast(1000).ToArray(),
                InputReplay = inputReplayBuffer.TakeLast(500).ToArray(),
                PacketCapture = packetCaptureBuffer.TakeLast(200).ToArray(),
                PerformanceHistory = performanceHistory.TakeLast(100).ToArray(),
                SystemInfo = new
                {
                    MachineName = Environment.MachineName,
                    OSVersion = Environment.OSVersion.ToString(),
                    ProcessorCount = Environment.ProcessorCount,
                    DS4WindowsVersion = Global.exeversion,
                    Is64BitProcess = Environment.Is64BitProcess
                }
            };

            var fileName = $"DS4Windows_DebugExport_{DateTime.Now:yyyyMMdd_HHmmss}.{format}";
            var filePath = Path.Combine(logDirectory, fileName);

            try
            {
                switch (format.ToLower())
                {
                    case "json":
                        var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
                        { 
                            WriteIndented = true,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });
                        await File.WriteAllTextAsync(filePath, json);
                        break;

                    case "txt":
                        var text = FormatDebugDataAsText(exportData);
                        await File.WriteAllTextAsync(filePath, text);
                        break;
                }

                LogEvent(DebugLevel.Info, "Export", $"Debug data exported to: {filePath}", "AdvancedDebugging");
                return filePath;
            }
            catch (Exception ex)
            {
                LogEvent(DebugLevel.Error, "Export", $"Failed to export debug data: {ex.Message}", "AdvancedDebugging", ex);
                return null;
            }
        }

        private string FormatDebugDataAsText(object exportData)
        {
            var sb = new StringBuilder();
            sb.AppendLine("DS4Windows Debug Data Export");
            sb.AppendLine("==============================");
            sb.AppendLine($"Export Time: {DateTime.UtcNow}");
            sb.AppendLine($"DS4Windows Version: {Global.exeversion}");
            sb.AppendLine();

            // Add formatted sections here
            sb.AppendLine("Recent Events:");
            sb.AppendLine("--------------");
            foreach (var evt in eventQueue.TakeLast(50))
            {
                sb.AppendLine(FormatLogEvent(evt));
            }

            return sb.ToString();
        }

        private void SaveDebugSession(DebugSession session)
        {
            try
            {
                var fileName = $"DebugSession_{session.SessionId}_{session.StartTime:yyyyMMdd_HHmmss}.json";
                var filePath = Path.Combine(logDirectory, fileName);

                var json = JsonSerializer.Serialize(session, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                LogEvent(DebugLevel.Error, "Session", $"Failed to save debug session: {ex.Message}", "AdvancedDebugging", ex);
            }
        }

        public List<DebugEvent> GetRecentEvents(int count = 100, DebugLevel minLevel = DebugLevel.Debug)
        {
            lock (eventQueue)
            {
                return eventQueue
                    .Where(e => e.Level >= minLevel)
                    .TakeLast(count)
                    .ToList();
            }
        }

        public List<PerformanceSnapshot> GetPerformanceHistory(TimeSpan? duration = null)
        {
            var cutoff = duration.HasValue ? DateTime.UtcNow - duration.Value : DateTime.MinValue;
            
            lock (performanceHistory)
            {
                return performanceHistory
                    .Where(p => p.Timestamp >= cutoff)
                    .ToList();
            }
        }

        public void SetLogLevel(DebugLevel level)
        {
            minimumLogLevel = level;
            LogEvent(DebugLevel.Info, "Config", $"Debug log level set to: {level}", "AdvancedDebugging");
        }

        public void ClearCaptures()
        {
            lock (inputReplayBuffer)
            {
                inputReplayBuffer.Clear();
            }
            
            lock (packetCaptureBuffer)
            {
                packetCaptureBuffer.Clear();
            }

            LogEvent(DebugLevel.Info, "Cleanup", "Debug captures cleared", "AdvancedDebugging");
        }

        public void Dispose()
        {
            if (disposed) return;
            
            disposed = true;

            try
            {
                performanceTimer?.Dispose();
                
                // End all active sessions
                foreach (var session in activeSessions.ToList())
                {
                    EndDebugSession(session.SessionId);
                }

                logWriter?.Dispose();
                logFileStream?.Dispose();

                LogEvent(DebugLevel.Info, "Shutdown", "Advanced debugging system disposed", "AdvancedDebugging");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disposing AdvancedDebugging: {ex.Message}");
            }
        }
    }

    // Event argument classes
    public class AdvancedDebugEventArgs : EventArgs
    {
        public DebugEvent Event { get; }

        public AdvancedDebugEventArgs(DebugEvent debugEvent)
        {
            Event = debugEvent;
        }
    }

    public class PerformanceSnapshotEventArgs : EventArgs
    {
        public PerformanceSnapshot Snapshot { get; }

        public PerformanceSnapshotEventArgs(PerformanceSnapshot snapshot)
        {
            Snapshot = snapshot;
        }
    }
}
