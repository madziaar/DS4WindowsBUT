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
using System.Drawing;
using System.Linq;

namespace DS4Windows
{
    public enum ControllerHealthStatus
    {
        Excellent,
        Good,
        Fair,
        Poor,
        Critical
    }

    public class StickWearData
    {
        public Point CenterPosition { get; set; }
        public double CenterDriftDistance { get; set; }
        public Dictionary<int, int> DeadZoneHits { get; set; } = new Dictionary<int, int>();
        public long TotalMovements { get; set; }
        public DateTime LastCalibration { get; set; }
        public bool RequiresRecalibration => CenterDriftDistance > 0.15; // 15% drift threshold
    }

    public class ButtonWearData
    {
        public long PressCount { get; set; }
        public TimeSpan TotalPressTime { get; set; }
        public DateTime LastPressed { get; set; }
        public bool IsSticking { get; set; }
        public double ResponseTime { get; set; } // Average response time in ms
    }

    public class MotionSensorData
    {
        public double GyroNoiseLevel { get; set; }
        public double AccelNoiseLevel { get; set; }
        public bool GyroCalibrationValid { get; set; }
        public bool AccelCalibrationValid { get; set; }
        public DateTime LastCalibration { get; set; }
    }

    public class ControllerHealthReport
    {
        public ControllerHealthStatus OverallHealth { get; set; }
        public Dictionary<string, StickWearData> StickWear { get; set; } = new Dictionary<string, StickWearData>();
        public Dictionary<string, ButtonWearData> ButtonWear { get; set; } = new Dictionary<string, ButtonWearData>();
        public MotionSensorData MotionSensors { get; set; } = new MotionSensorData();
        public BatteryHealth BatteryHealth { get; set; }
        public TimeSpan TotalUsageTime { get; set; }
        public DateTime LastHealthCheck { get; set; }
        public List<string> RecommendedActions { get; set; } = new List<string>();
    }

    public class HardwareHealthMonitor
    {
        private readonly DS4Device device;
        private readonly Stopwatch usageStopwatch;
        private readonly Dictionary<string, Stopwatch> buttonPressStopwatches;
        private readonly Dictionary<string, long> buttonPressCounts;
        private readonly Dictionary<string, StickWearData> stickWearData;
        
        private DateTime lastHealthCheck;
        private DS4State previousState;
        private MotionSensorData motionData;
        
        // Monitoring thresholds
        private const double STICK_DRIFT_THRESHOLD = 0.15; // 15% from center
        private const long BUTTON_WEAR_THRESHOLD = 100000; // 100k presses
        private const double GYRO_NOISE_THRESHOLD = 5.0; // degrees/sec noise level
        private const double ACCEL_NOISE_THRESHOLD = 0.1; // g noise level

        public event EventHandler<HealthStatusChangedEventArgs> HealthStatusChanged;
        public event EventHandler<WearWarningEventArgs> WearWarning;

        public HardwareHealthMonitor(DS4Device device)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.usageStopwatch = new Stopwatch();
            this.buttonPressStopwatches = new Dictionary<string, Stopwatch>();
            this.buttonPressCounts = new Dictionary<string, long>();
            this.stickWearData = new Dictionary<string, StickWearData>();
            this.motionData = new MotionSensorData();
            this.lastHealthCheck = DateTime.UtcNow;
            
            InitializeMonitoring();
            usageStopwatch.Start();
        }

        private void InitializeMonitoring()
        {
            // Initialize button monitoring
            var buttonNames = new[]
            {
                "Cross", "Triangle", "Circle", "Square", "R1", "L1", "R2", "L2",
                "R3", "L3", "Options", "Share", "PS", "TouchButton", "DpadUp",
                "DpadDown", "DpadLeft", "DpadRight"
            };

            foreach (var buttonName in buttonNames)
            {
                buttonPressStopwatches[buttonName] = new Stopwatch();
                buttonPressCounts[buttonName] = 0;
            }

            // Initialize stick monitoring
            stickWearData["LeftStick"] = new StickWearData
            {
                CenterPosition = new Point(128, 128), // Default center for DS4
                LastCalibration = DateTime.UtcNow
            };
            
            stickWearData["RightStick"] = new StickWearData
            {
                CenterPosition = new Point(128, 128),
                LastCalibration = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Updates hardware health monitoring with current controller state
        /// </summary>
        public void UpdateHealthMonitoring(DS4State currentState)
        {
            if (currentState == null) return;

            // Monitor button usage
            MonitorButtonUsage(currentState);
            
            // Monitor stick wear
            MonitorStickWear(currentState);
            
            // Monitor motion sensors
            MonitorMotionSensors();
            
            // Perform periodic health check
            if ((DateTime.UtcNow - lastHealthCheck).TotalMinutes >= 5)
            {
                PerformHealthCheck();
                lastHealthCheck = DateTime.UtcNow;
            }

            previousState = currentState;
        }

        private void MonitorButtonUsage(DS4State currentState)
        {
            var buttonStates = new Dictionary<string, bool>
            {
                { "Cross", currentState.Cross },
                { "Triangle", currentState.Triangle },
                { "Circle", currentState.Circle },
                { "Square", currentState.Square },
                { "R1", currentState.R1 },
                { "L1", currentState.L1 },
                { "R2", currentState.R2Btn },
                { "L2", currentState.L2Btn },
                { "R3", currentState.R3 },
                { "L3", currentState.L3 },
                { "Options", currentState.Options },
                { "Share", currentState.Share },
                { "PS", currentState.PS },
                { "TouchButton", currentState.TouchButton },
                { "DpadUp", currentState.DpadUp },
                { "DpadDown", currentState.DpadDown },
                { "DpadLeft", currentState.DpadLeft },
                { "DpadRight", currentState.DpadRight }
            };

            foreach (var kvp in buttonStates)
            {
                string buttonName = kvp.Key;
                bool isPressed = kvp.Value;
                bool wasPressed = false;

                // Check previous state
                if (previousState != null)
                {
                    switch (buttonName)
                    {
                        case "Cross": wasPressed = previousState.Cross; break;
                        case "Triangle": wasPressed = previousState.Triangle; break;
                        case "Circle": wasPressed = previousState.Circle; break;
                        case "Square": wasPressed = previousState.Square; break;
                        case "R1": wasPressed = previousState.R1; break;
                        case "L1": wasPressed = previousState.L1; break;
                        case "R2": wasPressed = previousState.R2Btn; break;
                        case "L2": wasPressed = previousState.L2Btn; break;
                        case "R3": wasPressed = previousState.R3; break;
                        case "L3": wasPressed = previousState.L3; break;
                        case "Options": wasPressed = previousState.Options; break;
                        case "Share": wasPressed = previousState.Share; break;
                        case "PS": wasPressed = previousState.PS; break;
                        case "TouchButton": wasPressed = previousState.TouchButton; break;
                        case "DpadUp": wasPressed = previousState.DpadUp; break;
                        case "DpadDown": wasPressed = previousState.DpadDown; break;
                        case "DpadLeft": wasPressed = previousState.DpadLeft; break;
                        case "DpadRight": wasPressed = previousState.DpadRight; break;
                    }
                }

                // Track button press/release
                if (isPressed && !wasPressed) // Button just pressed
                {
                    buttonPressCounts[buttonName]++;
                    buttonPressStopwatches[buttonName].Restart();
                }
                else if (!isPressed && wasPressed) // Button just released
                {
                    buttonPressStopwatches[buttonName].Stop();
                }
            }
        }

        private void MonitorStickWear(DS4State currentState)
        {
            // Monitor left stick
            var leftStickData = stickWearData["LeftStick"];
            var leftStickPos = new Point(currentState.LX, currentState.LY);
            var leftDrift = CalculateDistanceFromCenter(leftStickPos, leftStickData.CenterPosition);
            
            leftStickData.CenterDriftDistance = Math.Max(leftStickData.CenterDriftDistance, leftDrift / 128.0);
            leftStickData.TotalMovements++;

            // Monitor right stick
            var rightStickData = stickWearData["RightStick"];
            var rightStickPos = new Point(currentState.RX, currentState.RY);
            var rightDrift = CalculateDistanceFromCenter(rightStickPos, rightStickData.CenterPosition);
            
            rightStickData.CenterDriftDistance = Math.Max(rightStickData.CenterDriftDistance, rightDrift / 128.0);
            rightStickData.TotalMovements++;
        }

        private void MonitorMotionSensors()
        {
            if (device.SixAxis == null) return;

            var currentState = device.getCurrentState();
            if (currentState?.Motion == null) return;

            // Simple noise level calculation (would need more sophisticated analysis in reality)
            var gyroNoise = Math.Abs(currentState.Motion.angVelYaw) + Math.Abs(currentState.Motion.angVelPitch) + Math.Abs(currentState.Motion.angVelRoll);
            var accelNoise = Math.Abs(currentState.Motion.accelXG) + Math.Abs(currentState.Motion.accelYG) + Math.Abs(currentState.Motion.accelZG);
            
            motionData.GyroNoiseLevel = Math.Max(motionData.GyroNoiseLevel, gyroNoise);
            motionData.AccelNoiseLevel = Math.Max(motionData.AccelNoiseLevel, accelNoise);
        }

        private void PerformHealthCheck()
        {
            var report = GenerateHealthReport();
            var overallHealth = DetermineOverallHealth(report);
            
            if (report.OverallHealth != overallHealth)
            {
                var oldHealth = report.OverallHealth;
                report.OverallHealth = overallHealth;
                HealthStatusChanged?.Invoke(this, new HealthStatusChangedEventArgs(oldHealth, overallHealth));
            }

            // Check for specific wear warnings
            CheckWearWarnings(report);
        }

        private void CheckWearWarnings(ControllerHealthReport report)
        {
            // Check stick drift
            foreach (var kvp in report.StickWear)
            {
                if (kvp.Value.RequiresRecalibration)
                {
                    WearWarning?.Invoke(this, new WearWarningEventArgs(
                        WearWarningType.StickDrift, 
                        $"{kvp.Key} requires recalibration due to drift"));
                }
            }

            // Check button wear
            foreach (var kvp in report.ButtonWear)
            {
                if (kvp.Value.PressCount > BUTTON_WEAR_THRESHOLD)
                {
                    WearWarning?.Invoke(this, new WearWarningEventArgs(
                        WearWarningType.ButtonWear, 
                        $"{kvp.Key} has high usage ({kvp.Value.PressCount:N0} presses)"));
                }
            }

            // Check motion sensor issues
            if (report.MotionSensors.GyroNoiseLevel > GYRO_NOISE_THRESHOLD)
            {
                WearWarning?.Invoke(this, new WearWarningEventArgs(
                    WearWarningType.MotionSensorIssue, 
                    "Gyro sensor showing high noise levels"));
            }
        }

        public ControllerHealthReport GenerateHealthReport()
        {
            var report = new ControllerHealthReport
            {
                LastHealthCheck = DateTime.UtcNow,
                TotalUsageTime = usageStopwatch.Elapsed,
                StickWear = new Dictionary<string, StickWearData>(stickWearData),
                MotionSensors = motionData
            };

            // Generate button wear data
            foreach (var kvp in buttonPressCounts)
            {
                report.ButtonWear[kvp.Key] = new ButtonWearData
                {
                    PressCount = kvp.Value,
                    LastPressed = DateTime.UtcNow // Would track actual last press time
                };
            }

            report.OverallHealth = DetermineOverallHealth(report);
            report.RecommendedActions = GenerateRecommendations(report);

            return report;
        }

        private ControllerHealthStatus DetermineOverallHealth(ControllerHealthReport report)
        {
            int healthScore = 100;

            // Deduct points for stick drift
            foreach (var stick in report.StickWear.Values)
            {
                if (stick.CenterDriftDistance > 0.20) healthScore -= 30;
                else if (stick.CenterDriftDistance > 0.15) healthScore -= 20;
                else if (stick.CenterDriftDistance > 0.10) healthScore -= 10;
            }

            // Deduct points for excessive button wear
            foreach (var button in report.ButtonWear.Values)
            {
                if (button.PressCount > BUTTON_WEAR_THRESHOLD * 2) healthScore -= 20;
                else if (button.PressCount > BUTTON_WEAR_THRESHOLD) healthScore -= 10;
            }

            // Deduct points for motion sensor issues
            if (report.MotionSensors.GyroNoiseLevel > GYRO_NOISE_THRESHOLD) healthScore -= 15;
            if (report.MotionSensors.AccelNoiseLevel > ACCEL_NOISE_THRESHOLD) healthScore -= 15;

            if (healthScore >= 90) return ControllerHealthStatus.Excellent;
            if (healthScore >= 70) return ControllerHealthStatus.Good;
            if (healthScore >= 50) return ControllerHealthStatus.Fair;
            if (healthScore >= 30) return ControllerHealthStatus.Poor;
            return ControllerHealthStatus.Critical;
        }

        private List<string> GenerateRecommendations(ControllerHealthReport report)
        {
            var recommendations = new List<string>();

            foreach (var kvp in report.StickWear)
            {
                if (kvp.Value.RequiresRecalibration)
                {
                    recommendations.Add($"Recalibrate {kvp.Key} to fix drift issues");
                }
            }

            if (report.MotionSensors.GyroNoiseLevel > GYRO_NOISE_THRESHOLD)
            {
                recommendations.Add("Recalibrate gyro sensor to reduce noise");
            }

            if (report.ButtonWear.Values.Any(b => b.PressCount > BUTTON_WEAR_THRESHOLD))
            {
                recommendations.Add("Consider reducing button press intensity to extend controller life");
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add("Controller is in good health. Continue regular usage.");
            }

            return recommendations;
        }

        private double CalculateDistanceFromCenter(Point position, Point center)
        {
            var dx = position.X - center.X;
            var dy = position.Y - center.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    // Event argument classes
    public enum WearWarningType
    {
        StickDrift,
        ButtonWear,
        MotionSensorIssue,
        BatteryDegradation
    }

    public class HealthStatusChangedEventArgs : EventArgs
    {
        public ControllerHealthStatus OldStatus { get; }
        public ControllerHealthStatus NewStatus { get; }

        public HealthStatusChangedEventArgs(ControllerHealthStatus oldStatus, ControllerHealthStatus newStatus)
        {
            OldStatus = oldStatus;
            NewStatus = newStatus;
        }
    }

    public class WearWarningEventArgs : EventArgs
    {
        public WearWarningType WarningType { get; }
        public string Message { get; }

        public WearWarningEventArgs(WearWarningType warningType, string message)
        {
            WarningType = warningType;
            Message = message;
        }
    }
}
