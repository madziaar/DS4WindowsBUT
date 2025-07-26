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
using System.Linq;

namespace DS4Windows
{
    public enum BatteryHealth
    {
        Excellent,   // 90-100% of original capacity
        Good,        // 70-89% of original capacity
        Fair,        // 50-69% of original capacity
        Poor,        // 30-49% of original capacity
        VeryPoor,    // <30% of original capacity
        Unknown      // Unable to determine battery health
    }

    public class BatteryStats
    {
        public DateTime Timestamp { get; set; }
        public int BatteryLevel { get; set; }
        public bool IsCharging { get; set; }
        public TimeSpan SessionDuration { get; set; }
        public double PowerConsumptionRate { get; set; } // Battery drain per hour
    }

    public class BatteryManager
    {
        private readonly DS4Device device;
        private readonly List<BatteryStats> batteryHistory;
        private readonly int maxHistoryEntries = 1000;
        
        private DateTime lastBatteryUpdate;
        private int previousBatteryLevel;
        private DateTime sessionStartTime;
        private bool wasCharging;
        
        // Battery health estimation
        private double estimatedMaxCapacity = 100.0;
        private int chargeCycles = 0;
        
        public event EventHandler<BatteryHealthChangedEventArgs> BatteryHealthChanged;
        public event EventHandler<LowBatteryEventArgs> LowBatteryWarning;
        public event EventHandler<BatteryStatsEventArgs> BatteryStatsUpdated;

        public BatteryStats CurrentStats { get; private set; }

        public BatteryManager(DS4Device device)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.batteryHistory = new List<BatteryStats>();
            this.sessionStartTime = DateTime.UtcNow;
            this.lastBatteryUpdate = DateTime.UtcNow;
            this.previousBatteryLevel = device.Battery;
            this.wasCharging = device.Charging;
        }

        /// <summary>
        /// Updates battery statistics and health monitoring
        /// </summary>
        public void UpdateBatteryStats()
        {
            var now = DateTime.UtcNow;
            var currentLevel = device.Battery;
            var isCharging = device.Charging;
            var sessionDuration = now - sessionStartTime;

            // Calculate power consumption rate
            double powerConsumptionRate = 0.0;
            if (!isCharging && !wasCharging && lastBatteryUpdate != DateTime.MinValue)
            {
                var timeDiff = (now - lastBatteryUpdate).TotalHours;
                var levelDiff = previousBatteryLevel - currentLevel;
                if (timeDiff > 0 && levelDiff > 0)
                {
                    powerConsumptionRate = levelDiff / timeDiff;
                }
            }

            // Detect charge cycles
            if (!wasCharging && isCharging && currentLevel < 95)
            {
                chargeCycles++;
            }

            // Create battery stats entry
            var stats = new BatteryStats
            {
                Timestamp = now,
                BatteryLevel = currentLevel,
                IsCharging = isCharging,
                SessionDuration = sessionDuration,
                PowerConsumptionRate = powerConsumptionRate
            };

            // Add to history
            batteryHistory.Add(stats);
            if (batteryHistory.Count > maxHistoryEntries)
            {
                batteryHistory.RemoveAt(0);
            }

            // Check for low battery warning
            CheckLowBatteryWarning(currentLevel, isCharging);

            // Update battery health estimation
            UpdateBatteryHealth();

            // Fire events
            BatteryStatsUpdated?.Invoke(this, new BatteryStatsEventArgs(stats));

            // Update tracking variables
            lastBatteryUpdate = now;
            previousBatteryLevel = currentLevel;
            wasCharging = isCharging;
        }

        /// <summary>
        /// Gets current battery health assessment
        /// </summary>
        public BatteryHealth GetBatteryHealth()
        {
            var healthPercentage = (estimatedMaxCapacity / 100.0) * 100.0;
            
            if (healthPercentage >= 90) return BatteryHealth.Excellent;
            if (healthPercentage >= 70) return BatteryHealth.Good;
            if (healthPercentage >= 50) return BatteryHealth.Fair;
            if (healthPercentage >= 30) return BatteryHealth.Poor;
            return BatteryHealth.VeryPoor;
        }

        /// <summary>
        /// Gets estimated remaining battery life based on current usage patterns
        /// </summary>
        public TimeSpan GetEstimatedRemainingLife()
        {
            if (device.Charging)
                return TimeSpan.MaxValue; // Charging, so infinite battery life

            var recentStats = batteryHistory.Where(s => !s.IsCharging && s.PowerConsumptionRate > 0)
                                          .TakeLast(10)
                                          .ToList();

            if (!recentStats.Any())
                return TimeSpan.FromHours(4); // Default estimate

            var avgConsumptionRate = recentStats.Average(s => s.PowerConsumptionRate);
            if (avgConsumptionRate <= 0)
                return TimeSpan.FromHours(4);

            var remainingHours = device.Battery / avgConsumptionRate;
            return TimeSpan.FromHours(Math.Max(0, remainingHours));
        }

        /// <summary>
        /// Gets battery usage analytics
        /// </summary>
        public BatteryAnalytics GetBatteryAnalytics()
        {
            var last24Hours = batteryHistory.Where(s => s.Timestamp >= DateTime.UtcNow.AddHours(-24)).ToList();
            var lastWeek = batteryHistory.Where(s => s.Timestamp >= DateTime.UtcNow.AddDays(-7)).ToList();

            return new BatteryAnalytics
            {
                ChargeCycles = chargeCycles,
                EstimatedHealth = GetBatteryHealth(),
                EstimatedMaxCapacity = estimatedMaxCapacity,
                AverageDaily24HourConsumption = last24Hours.Where(s => !s.IsCharging).Sum(s => s.PowerConsumptionRate),
                AverageWeeklyConsumption = lastWeek.Where(s => !s.IsCharging).Average(s => s.PowerConsumptionRate),
                TotalSessionTime = DateTime.UtcNow - sessionStartTime,
                BatteryHistory = batteryHistory.ToList()
            };
        }

        private void UpdateBatteryHealth()
        {
            // Simple battery health estimation based on charge cycles
            // Real-world degradation is more complex, but this provides a basic estimate
            var degradationFactor = Math.Max(0.3, 1.0 - (chargeCycles * 0.0002)); // 0.02% per cycle
            var newMaxCapacity = 100.0 * degradationFactor;
            
            if (Math.Abs(newMaxCapacity - estimatedMaxCapacity) > 1.0)
            {
                var oldHealth = GetBatteryHealth();
                estimatedMaxCapacity = newMaxCapacity;
                var newHealth = GetBatteryHealth();
                
                if (oldHealth != newHealth)
                {
                    BatteryHealthChanged?.Invoke(this, new BatteryHealthChangedEventArgs(oldHealth, newHealth));
                }
            }
        }

        private void CheckLowBatteryWarning(int currentLevel, bool isCharging)
        {
            if (!isCharging)
            {
                if (currentLevel <= 1) // Critical
                {
                    LowBatteryWarning?.Invoke(this, new LowBatteryEventArgs(currentLevel, BatteryWarningLevel.Critical));
                }
                else if (currentLevel <= 2) // Very Low
                {
                    LowBatteryWarning?.Invoke(this, new LowBatteryEventArgs(currentLevel, BatteryWarningLevel.VeryLow));
                }
                else if (currentLevel <= 3) // Low
                {
                    LowBatteryWarning?.Invoke(this, new LowBatteryEventArgs(currentLevel, BatteryWarningLevel.Low));
                }
            }
        }
    }

    public class BatteryAnalytics
    {
        public int ChargeCycles { get; set; }
        public BatteryHealth EstimatedHealth { get; set; }
        public double EstimatedMaxCapacity { get; set; }
        public double AverageDaily24HourConsumption { get; set; }
        public double AverageWeeklyConsumption { get; set; }
        public TimeSpan TotalSessionTime { get; set; }
        public List<BatteryStats> BatteryHistory { get; set; }
    }

    public enum BatteryWarningLevel
    {
        Low,        // ~20%
        VeryLow,    // ~10%
        Critical    // ~5%
    }

    // Event argument classes
    public class BatteryHealthChangedEventArgs : EventArgs
    {
        public BatteryHealth OldHealth { get; }
        public BatteryHealth NewHealth { get; }

        public BatteryHealthChangedEventArgs(BatteryHealth oldHealth, BatteryHealth newHealth)
        {
            OldHealth = oldHealth;
            NewHealth = newHealth;
        }
    }

    public class LowBatteryEventArgs : EventArgs
    {
        public int BatteryLevel { get; }
        public BatteryWarningLevel WarningLevel { get; }

        public LowBatteryEventArgs(int batteryLevel, BatteryWarningLevel warningLevel)
        {
            BatteryLevel = batteryLevel;
            WarningLevel = warningLevel;
        }
    }

    public class BatteryStatsEventArgs : EventArgs
    {
        public BatteryStats BatteryStats { get; }

        public BatteryStatsEventArgs(BatteryStats batteryStats)
        {
            BatteryStats = batteryStats;
        }
    }
}
