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
using System.Linq;
using System.Threading;

namespace DS4Windows
{
    public class InputLatencyData
    {
        public DateTime Timestamp { get; set; }
        public double LatencyMs { get; set; }
        public ConnectionType ConnectionType { get; set; }
        public string InputType { get; set; } // Button, Stick, Trigger, etc.
    }

    public class ThroughputData
    {
        public DateTime Timestamp { get; set; }
        public int PacketsPerSecond { get; set; }
        public int BytesPerSecond { get; set; }
        public int DroppedPackets { get; set; }
        public double PacketLossRate { get; set; }
    }

    public class PerformanceMetrics
    {
        public double AverageLatencyMs { get; set; }
        public double MinLatencyMs { get; set; }
        public double MaxLatencyMs { get; set; }
        public double LatencyStandardDeviation { get; set; }
        public double AverageThroughputPPS { get; set; }
        public double PacketLossPercentage { get; set; }
        public double PacketLossRate { get; set; }
        public double ConnectionStability { get; set; }
        public double OverallHealthScore { get; set; }
        public int TotalInputEvents { get; set; }
        public TimeSpan MonitoringDuration { get; set; }
        public Dictionary<string, double> InputTypeLatencies { get; set; } = new Dictionary<string, double>();
        public List<PerformanceAlert> Alerts { get; set; } = new List<PerformanceAlert>();
    }

    public class PerformanceAlert
    {
        public DateTime Timestamp { get; set; }
        public PerformanceAlertType Type { get; set; }
        public string Message { get; set; }
        public double Value { get; set; }
        public double Threshold { get; set; }
    }

    public enum PerformanceAlertType
    {
        HighLatency,
        PacketLoss,
        LowThroughput,
        ConnectionIssue,
        MemoryUsage,
        CPUUsage
    }

    public class PerformanceAnalytics
    {
        private readonly DS4Device device;
        private readonly Queue<InputLatencyData> latencyHistory;
        private readonly Queue<ThroughputData> throughputHistory;
        private readonly List<PerformanceAlert> alerts;
        private readonly Stopwatch monitoringStopwatch;
        private readonly Timer performanceTimer;
        
        private readonly int maxHistorySize = 1000;
        private DateTime startTime;
        private int totalInputEvents = 0;
        private int totalPackets = 0;
        private int droppedPackets = 0;
        private DateTime lastThroughputUpdate;
        private int packetsInCurrentSecond = 0;
        private int bytesInCurrentSecond = 0;
        
        // Performance thresholds
        private const double HIGH_LATENCY_THRESHOLD_MS = 20.0;
        private const double HIGH_PACKET_LOSS_THRESHOLD = 5.0; // 5%
        private const int LOW_THROUGHPUT_THRESHOLD_PPS = 30; // packets per second
        private const double LATENCY_SPIKE_THRESHOLD = 50.0; // ms
        
        public event EventHandler<PerformanceAlertEventArgs> PerformanceAlert;
        public event EventHandler<PerformanceMetricsEventArgs> MetricsUpdated;

        public PerformanceAnalytics(DS4Device device)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.latencyHistory = new Queue<InputLatencyData>();
            this.throughputHistory = new Queue<ThroughputData>();
            this.alerts = new List<PerformanceAlert>();
            this.monitoringStopwatch = new Stopwatch();
            this.startTime = DateTime.UtcNow;
            this.lastThroughputUpdate = DateTime.UtcNow;
            
            // Update performance metrics every second
            this.performanceTimer = new Timer(UpdatePerformanceMetrics, null, 1000, 1000);
            
            monitoringStopwatch.Start();
        }

        /// <summary>
        /// Records input latency measurement
        /// </summary>
        public void RecordInputLatency(double latencyMs, string inputType)
        {
            var latencyData = new InputLatencyData
            {
                Timestamp = DateTime.UtcNow,
                LatencyMs = latencyMs,
                ConnectionType = device.ConnectionType,
                InputType = inputType
            };

            latencyHistory.Enqueue(latencyData);
            if (latencyHistory.Count > maxHistorySize)
            {
                latencyHistory.Dequeue();
            }

            totalInputEvents++;

            // Check for latency spikes
            if (latencyMs > LATENCY_SPIKE_THRESHOLD)
            {
                CreateAlert(PerformanceAlertType.HighLatency, 
                    $"High input latency detected: {latencyMs:F1}ms for {inputType}", 
                    latencyMs, LATENCY_SPIKE_THRESHOLD);
            }
        }

        /// <summary>
        /// Records packet throughput data
        /// </summary>
        public void RecordPacketData(int packetSize, bool wasDropped = false)
        {
            totalPackets++;
            packetsInCurrentSecond++;
            bytesInCurrentSecond += packetSize;
            
            if (wasDropped)
            {
                droppedPackets++;
            }
        }

        /// <summary>
        /// Measures input latency for a specific input event
        /// </summary>
        public double MeasureInputLatency(Action inputAction, string inputType)
        {
            var stopwatch = Stopwatch.StartNew();
            inputAction?.Invoke();
            stopwatch.Stop();
            
            var latencyMs = stopwatch.Elapsed.TotalMilliseconds;
            RecordInputLatency(latencyMs, inputType);
            
            return latencyMs;
        }

        /// <summary>
        /// Gets current performance metrics
        /// </summary>
        public PerformanceMetrics GetCurrentMetrics()
        {
            var metrics = new PerformanceMetrics();
            
            if (latencyHistory.Any())
            {
                var latencies = latencyHistory.Select(l => l.LatencyMs).ToList();
                metrics.AverageLatencyMs = latencies.Average();
                metrics.MinLatencyMs = latencies.Min();
                metrics.MaxLatencyMs = latencies.Max();
                metrics.LatencyStandardDeviation = CalculateStandardDeviation(latencies);
                
                // Calculate per-input-type latencies
                var inputGroups = latencyHistory.GroupBy(l => l.InputType);
                foreach (var group in inputGroups)
                {
                    metrics.InputTypeLatencies[group.Key] = group.Average(l => l.LatencyMs);
                }
            }

            if (throughputHistory.Any())
            {
                metrics.AverageThroughputPPS = throughputHistory.Average(t => t.PacketsPerSecond);
                var totalPacketsProcessed = throughputHistory.Sum(t => t.PacketsPerSecond);
                var totalDropped = throughputHistory.Sum(t => t.DroppedPackets);
                metrics.PacketLossPercentage = totalPacketsProcessed > 0 ? 
                    (totalDropped / (double)totalPacketsProcessed) * 100 : 0;
            }

            metrics.TotalInputEvents = totalInputEvents;
            metrics.MonitoringDuration = monitoringStopwatch.Elapsed;
            metrics.Alerts = alerts.TakeLast(10).ToList(); // Last 10 alerts
            
            return metrics;
        }

        /// <summary>
        /// Gets performance analytics for a specific time period
        /// </summary>
        public PerformanceMetrics GetMetricsForPeriod(TimeSpan period)
        {
            var cutoffTime = DateTime.UtcNow - period;
            var relevantLatency = latencyHistory.Where(l => l.Timestamp >= cutoffTime).ToList();
            var relevantThroughput = throughputHistory.Where(t => t.Timestamp >= cutoffTime).ToList();
            
            var metrics = new PerformanceMetrics();
            
            if (relevantLatency.Any())
            {
                var latencies = relevantLatency.Select(l => l.LatencyMs).ToList();
                metrics.AverageLatencyMs = latencies.Average();
                metrics.MinLatencyMs = latencies.Min();
                metrics.MaxLatencyMs = latencies.Max();
                metrics.LatencyStandardDeviation = CalculateStandardDeviation(latencies);
            }

            if (relevantThroughput.Any())
            {
                metrics.AverageThroughputPPS = relevantThroughput.Average(t => t.PacketsPerSecond);
                var periodPackets = relevantThroughput.Sum(t => t.PacketsPerSecond);
                var periodDropped = relevantThroughput.Sum(t => t.DroppedPackets);
                metrics.PacketLossPercentage = periodPackets > 0 ? 
                    (periodDropped / (double)periodPackets) * 100 : 0;
            }

            metrics.TotalInputEvents = relevantLatency.Count;
            metrics.MonitoringDuration = period;
            
            return metrics;
        }

        /// <summary>
        /// Gets latency percentiles for detailed analysis
        /// </summary>
        public Dictionary<int, double> GetLatencyPercentiles()
        {
            if (!latencyHistory.Any())
                return new Dictionary<int, double>();

            var sortedLatencies = latencyHistory.Select(l => l.LatencyMs).OrderBy(l => l).ToList();
            var count = sortedLatencies.Count;
            
            return new Dictionary<int, double>
            {
                { 50, GetPercentile(sortedLatencies, 0.50) },  // Median
                { 90, GetPercentile(sortedLatencies, 0.90) },  // 90th percentile
                { 95, GetPercentile(sortedLatencies, 0.95) },  // 95th percentile
                { 99, GetPercentile(sortedLatencies, 0.99) }   // 99th percentile
            };
        }

        /// <summary>
        /// Analyzes connection quality
        /// </summary>
        public ConnectionQualityReport AnalyzeConnectionQuality()
        {
            var recentData = throughputHistory.TakeLast(60).ToList(); // Last minute
            var recentLatency = latencyHistory.TakeLast(100).ToList();
            
            var report = new ConnectionQualityReport
            {
                Timestamp = DateTime.UtcNow,
                ConnectionType = device.ConnectionType
            };

            if (recentLatency.Any())
            {
                report.AverageLatency = recentLatency.Average(l => l.LatencyMs);
                report.LatencyVariability = CalculateStandardDeviation(recentLatency.Select(l => l.LatencyMs));
            }

            if (recentData.Any())
            {
                report.PacketLossRate = recentData.Average(t => t.PacketLossRate);
                report.Throughput = recentData.Average(t => t.PacketsPerSecond);
            }

            // Determine overall quality score (0-100)
            report.QualityScore = CalculateQualityScore(report);
            report.QualityRating = GetQualityRating(report.QualityScore);
            
            return report;
        }

        /// <summary>
        /// Generates performance optimization recommendations
        /// </summary>
        public List<string> GetOptimizationRecommendations()
        {
            var recommendations = new List<string>();
            var metrics = GetCurrentMetrics();
            
            if (metrics.AverageLatencyMs > HIGH_LATENCY_THRESHOLD_MS)
            {
                recommendations.Add("Consider using USB connection for lower latency");
                recommendations.Add("Close unnecessary background applications");
                
                if (device.ConnectionType == ConnectionType.BT)
                {
                    recommendations.Add("Move closer to Bluetooth adapter");
                    recommendations.Add("Reduce Bluetooth polling rate if experiencing issues");
                }
            }

            if (metrics.PacketLossPercentage > HIGH_PACKET_LOSS_THRESHOLD)
            {
                recommendations.Add("Check for wireless interference");
                recommendations.Add("Update controller drivers");
                recommendations.Add("Try a different USB port or Bluetooth adapter");
            }

            if (metrics.AverageThroughputPPS < LOW_THROUGHPUT_THRESHOLD_PPS)
            {
                recommendations.Add("Check controller connection stability");
                recommendations.Add("Restart DS4Windows service");
            }

            var latencyPercentiles = GetLatencyPercentiles();
            if (latencyPercentiles.ContainsKey(99) && latencyPercentiles[99] > 100)
            {
                recommendations.Add("Experiencing occasional latency spikes - check system performance");
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add("Performance is optimal - no recommendations needed");
            }

            return recommendations;
        }

        private void UpdatePerformanceMetrics(object state)
        {
            // Update throughput data
            var now = DateTime.UtcNow;
            var throughputData = new ThroughputData
            {
                Timestamp = now,
                PacketsPerSecond = packetsInCurrentSecond,
                BytesPerSecond = bytesInCurrentSecond,
                DroppedPackets = 0, // Would need to track this properly
                PacketLossRate = totalPackets > 0 ? (droppedPackets / (double)totalPackets) * 100 : 0
            };

            throughputHistory.Enqueue(throughputData);
            if (throughputHistory.Count > maxHistorySize)
            {
                throughputHistory.Dequeue();
            }

            // Check performance thresholds
            CheckPerformanceThresholds(throughputData);

            // Reset counters
            packetsInCurrentSecond = 0;
            bytesInCurrentSecond = 0;
            lastThroughputUpdate = now;

            // Fire metrics updated event
            MetricsUpdated?.Invoke(this, new PerformanceMetricsEventArgs(GetCurrentMetrics()));
        }

        private void CheckPerformanceThresholds(ThroughputData throughputData)
        {
            // Check for low throughput
            if (throughputData.PacketsPerSecond < LOW_THROUGHPUT_THRESHOLD_PPS)
            {
                CreateAlert(PerformanceAlertType.LowThroughput,
                    $"Low throughput detected: {throughputData.PacketsPerSecond} PPS",
                    throughputData.PacketsPerSecond, LOW_THROUGHPUT_THRESHOLD_PPS);
            }

            // Check for high packet loss
            if (throughputData.PacketLossRate > HIGH_PACKET_LOSS_THRESHOLD)
            {
                CreateAlert(PerformanceAlertType.PacketLoss,
                    $"High packet loss: {throughputData.PacketLossRate:F1}%",
                    throughputData.PacketLossRate, HIGH_PACKET_LOSS_THRESHOLD);
            }
        }

        private void CreateAlert(PerformanceAlertType type, string message, double value, double threshold)
        {
            var alert = new PerformanceAlert
            {
                Timestamp = DateTime.UtcNow,
                Type = type,
                Message = message,
                Value = value,
                Threshold = threshold
            };

            alerts.Add(alert);
            
            // Keep only recent alerts
            if (alerts.Count > 100)
            {
                alerts.RemoveAt(0);
            }

            PerformanceAlert?.Invoke(this, new PerformanceAlertEventArgs(alert));
        }

        private double CalculateStandardDeviation(IEnumerable<double> values)
        {
            var valuesList = values.ToList();
            if (valuesList.Count <= 1) return 0.0;

            var average = valuesList.Average();
            var sumOfSquaresOfDifferences = valuesList.Select(val => (val - average) * (val - average)).Sum();
            return Math.Sqrt(sumOfSquaresOfDifferences / (valuesList.Count - 1));
        }

        private double GetPercentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0) return 0.0;
            
            var index = percentile * (sortedValues.Count - 1);
            var lowerIndex = (int)Math.Floor(index);
            var upperIndex = (int)Math.Ceiling(index);
            
            if (lowerIndex == upperIndex)
                return sortedValues[lowerIndex];
            
            var weight = index - lowerIndex;
            return sortedValues[lowerIndex] * (1 - weight) + sortedValues[upperIndex] * weight;
        }

        private double CalculateQualityScore(ConnectionQualityReport report)
        {
            double score = 100.0;
            
            // Deduct points for high latency
            if (report.AverageLatency > 10) score -= Math.Min(30, (report.AverageLatency - 10) * 2);
            
            // Deduct points for packet loss
            score -= report.PacketLossRate * 10;
            
            // Deduct points for low throughput
            if (report.Throughput < 60) score -= (60 - report.Throughput) / 2;
            
            // Deduct points for high latency variability
            score -= Math.Min(20, report.LatencyVariability);
            
            return Math.Max(0, score);
        }

        private string GetQualityRating(double score)
        {
            if (score >= 90) return "Excellent";
            if (score >= 75) return "Good";
            if (score >= 60) return "Fair";
            if (score >= 40) return "Poor";
            return "Very Poor";
        }

        public void Dispose()
        {
            performanceTimer?.Dispose();
            monitoringStopwatch?.Stop();
        }
    }

    public class ConnectionQualityReport
    {
        public DateTime Timestamp { get; set; }
        public ConnectionType ConnectionType { get; set; }
        public double AverageLatency { get; set; }
        public double LatencyVariability { get; set; }
        public double PacketLossRate { get; set; }
        public double Throughput { get; set; }
        public double QualityScore { get; set; }
        public string QualityRating { get; set; }
    }

    // Event argument classes
    public class PerformanceAlertEventArgs : EventArgs
    {
        public PerformanceAlert Alert { get; }

        public PerformanceAlertEventArgs(PerformanceAlert alert)
        {
            Alert = alert;
        }
    }

    public class PerformanceMetricsEventArgs : EventArgs
    {
        public PerformanceMetrics Metrics { get; }

        public PerformanceMetricsEventArgs(PerformanceMetrics metrics)
        {
            Metrics = metrics;
        }
    }

    public class PerformanceDashboard
    {
        public double AverageLatencyMs { get; set; }
        public double CurrentCpuUsage { get; set; }
        public double MemoryUsageMB { get; set; }
        public int PacketsPerSecond { get; set; }
        public double PacketLossRate { get; set; }
        public int ActiveControllers { get; set; }
        public List<PerformanceAlert> RecentAlerts { get; set; } = new List<PerformanceAlert>();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
