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
using Sensorit.Base;

namespace DS4Windows
{
    public enum MotionFilterType
    {
        None,
        LowPass,
        OneEuro,
        Kalman,
        MedianFilter,
        AdaptiveNoise
    }

    public class MotionGesture
    {
        public string Name { get; set; }
        public List<MotionPattern> Patterns { get; set; } = new List<MotionPattern>();
        public TimeSpan TimeWindow { get; set; }
        public double Threshold { get; set; }
        public Action<MotionGesture> OnDetected { get; set; }
        public string AssignedAction { get; set; }
    }

    public class MotionPattern
    {
        public double ExpectedYaw { get; set; }
        public double ExpectedPitch { get; set; }
        public double ExpectedRoll { get; set; }
        public double Tolerance { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class MotionSample
    {
        public DateTime Timestamp { get; set; }
        public double Yaw { get; set; }
        public double Pitch { get; set; }
        public double Roll { get; set; }
        public double AccelX { get; set; }
        public double AccelY { get; set; }
        public double AccelZ { get; set; }
    }

    public class AdvancedMotionFilter
    {
        private readonly DS4Device device;
        private readonly Queue<MotionSample> motionHistory;
        private readonly Dictionary<string, MotionGesture> registeredGestures;
        private readonly int maxHistorySize = 100;
        
        // Filter parameters
        private MotionFilterType filterType = MotionFilterType.OneEuro;
        private double noiseThreshold = 0.5;
        private double adaptiveGain = 0.1;
        
        // OneEuro filter parameters
        private AdvancedOneEuroFilter3D gyroFilter;
        private AdvancedOneEuroFilter3D accelFilter;
        
        // Kalman filter state
        private KalmanFilter3D kalmanGyro;
        private KalmanFilter3D kalmanAccel;
        
        // Motion detection
        private DateTime lastGestureCheck;
        private const double GESTURE_CHECK_INTERVAL_MS = 50;

        public event EventHandler<MotionGestureDetectedEventArgs> GestureDetected;
        public event EventHandler<OrientationChangedEventArgs> OrientationChanged;

        public AdvancedMotionFilter(DS4Device device)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.motionHistory = new Queue<MotionSample>();
            this.registeredGestures = new Dictionary<string, MotionGesture>();
            this.lastGestureCheck = DateTime.UtcNow;
            
            InitializeFilters();
            RegisterDefaultGestures();
        }

        private void InitializeFilters()
        {
            // Initialize OneEuro filters with optimized parameters for DS4v2
            gyroFilter = new AdvancedOneEuroFilter3D();
            accelFilter = new AdvancedOneEuroFilter3D();
            
            // Initialize Kalman filters
            kalmanGyro = new KalmanFilter3D();
            kalmanAccel = new KalmanFilter3D();
        }

        private void RegisterDefaultGestures()
        {
            // Shake left-right gesture
            RegisterGesture(new MotionGesture
            {
                Name = "ShakeHorizontal",
                TimeWindow = TimeSpan.FromMilliseconds(1000),
                Threshold = 15.0, // degrees/sec
                Patterns = new List<MotionPattern>
                {
                    new MotionPattern { ExpectedYaw = 30, Tolerance = 10, Duration = TimeSpan.FromMilliseconds(200) },
                    new MotionPattern { ExpectedYaw = -30, Tolerance = 10, Duration = TimeSpan.FromMilliseconds(200) },
                    new MotionPattern { ExpectedYaw = 30, Tolerance = 10, Duration = TimeSpan.FromMilliseconds(200) }
                }
            });

            // Tilt forward gesture
            RegisterGesture(new MotionGesture
            {
                Name = "TiltForward",
                TimeWindow = TimeSpan.FromMilliseconds(500),
                Threshold = 20.0,
                Patterns = new List<MotionPattern>
                {
                    new MotionPattern { ExpectedPitch = -25, Tolerance = 10, Duration = TimeSpan.FromMilliseconds(300) }
                }
            });

            // Quick rotation gesture
            RegisterGesture(new MotionGesture
            {
                Name = "QuickSpin",
                TimeWindow = TimeSpan.FromMilliseconds(800),
                Threshold = 45.0,
                Patterns = new List<MotionPattern>
                {
                    new MotionPattern { ExpectedRoll = 90, Tolerance = 20, Duration = TimeSpan.FromMilliseconds(400) }
                }
            });
        }

        /// <summary>
        /// Processes motion data with advanced filtering
        /// </summary>
        public MotionSample ProcessMotionData(SixAxis sixAxis, DateTime timestamp)
        {
            if (sixAxis == null) return null;

            var rawSample = new MotionSample
            {
                Timestamp = timestamp,
                Yaw = sixAxis.angVelYaw,
                Pitch = sixAxis.angVelPitch,
                Roll = sixAxis.angVelRoll,
                AccelX = sixAxis.accelXG,
                AccelY = sixAxis.accelYG,
                AccelZ = sixAxis.accelZG
            };

            // Apply selected filter
            var filteredSample = ApplyMotionFilter(rawSample);

            // Add to history
            motionHistory.Enqueue(filteredSample);
            if (motionHistory.Count > maxHistorySize)
            {
                motionHistory.Dequeue();
            }

            // Check for gestures
            if ((timestamp - lastGestureCheck).TotalMilliseconds >= GESTURE_CHECK_INTERVAL_MS)
            {
                CheckForGestures();
                lastGestureCheck = timestamp;
            }

            return filteredSample;
        }

        private MotionSample ApplyMotionFilter(MotionSample rawSample)
        {
            switch (filterType)
            {
                case MotionFilterType.OneEuro:
                    return ApplyOneEuroFilter(rawSample);
                    
                case MotionFilterType.Kalman:
                    return ApplyKalmanFilter(rawSample);
                    
                case MotionFilterType.LowPass:
                    return ApplyLowPassFilter(rawSample);
                    
                case MotionFilterType.MedianFilter:
                    return ApplyMedianFilter(rawSample);
                    
                case MotionFilterType.AdaptiveNoise:
                    return ApplyAdaptiveNoiseFilter(rawSample);
                    
                default:
                    return rawSample;
            }
        }

        private MotionSample ApplyOneEuroFilter(MotionSample sample)
        {
            var deltaTime = 1.0 / 60.0; // Assume 60Hz update rate for DS4v2
            
            var filteredGyro = gyroFilter.Filter(sample.Yaw, sample.Pitch, sample.Roll, deltaTime);
            var filteredAccel = accelFilter.Filter(sample.AccelX, sample.AccelY, sample.AccelZ, deltaTime);

            return new MotionSample
            {
                Timestamp = sample.Timestamp,
                Yaw = filteredGyro.X,
                Pitch = filteredGyro.Y,
                Roll = filteredGyro.Z,
                AccelX = filteredAccel.X,
                AccelY = filteredAccel.Y,
                AccelZ = filteredAccel.Z
            };
        }

        private MotionSample ApplyKalmanFilter(MotionSample sample)
        {
            var filteredGyro = kalmanGyro.Update(sample.Yaw, sample.Pitch, sample.Roll);
            var filteredAccel = kalmanAccel.Update(sample.AccelX, sample.AccelY, sample.AccelZ);

            return new MotionSample
            {
                Timestamp = sample.Timestamp,
                Yaw = filteredGyro.X,
                Pitch = filteredGyro.Y,
                Roll = filteredGyro.Z,
                AccelX = filteredAccel.X,
                AccelY = filteredAccel.Y,
                AccelZ = filteredAccel.Z
            };
        }

        private MotionSample ApplyLowPassFilter(MotionSample sample)
        {
            const double alpha = 0.7; // Low pass filter coefficient
            
            if (motionHistory.Count == 0)
                return sample;

            var previous = motionHistory.Last();
            
            return new MotionSample
            {
                Timestamp = sample.Timestamp,
                Yaw = alpha * sample.Yaw + (1 - alpha) * previous.Yaw,
                Pitch = alpha * sample.Pitch + (1 - alpha) * previous.Pitch,
                Roll = alpha * sample.Roll + (1 - alpha) * previous.Roll,
                AccelX = alpha * sample.AccelX + (1 - alpha) * previous.AccelX,
                AccelY = alpha * sample.AccelY + (1 - alpha) * previous.AccelY,
                AccelZ = alpha * sample.AccelZ + (1 - alpha) * previous.AccelZ
            };
        }

        private MotionSample ApplyMedianFilter(MotionSample sample)
        {
            const int windowSize = 5;
            
            if (motionHistory.Count < windowSize)
                return sample;

            var recentSamples = motionHistory.TakeLast(windowSize).ToList();
            recentSamples.Add(sample);

            return new MotionSample
            {
                Timestamp = sample.Timestamp,
                Yaw = GetMedian(recentSamples.Select(s => s.Yaw)),
                Pitch = GetMedian(recentSamples.Select(s => s.Pitch)),
                Roll = GetMedian(recentSamples.Select(s => s.Roll)),
                AccelX = GetMedian(recentSamples.Select(s => s.AccelX)),
                AccelY = GetMedian(recentSamples.Select(s => s.AccelY)),
                AccelZ = GetMedian(recentSamples.Select(s => s.AccelZ))
            };
        }

        private MotionSample ApplyAdaptiveNoiseFilter(MotionSample sample)
        {
            // Adaptive filter that adjusts based on detected noise level
            var noiseLevel = CalculateNoiseLevel();
            var adaptiveFactor = Math.Min(1.0, noiseLevel / noiseThreshold);
            var filterStrength = adaptiveFactor * adaptiveGain;

            if (motionHistory.Count == 0)
                return sample;

            var previous = motionHistory.Last();

            return new MotionSample
            {
                Timestamp = sample.Timestamp,
                Yaw = (1 - filterStrength) * sample.Yaw + filterStrength * previous.Yaw,
                Pitch = (1 - filterStrength) * sample.Pitch + filterStrength * previous.Pitch,
                Roll = (1 - filterStrength) * sample.Roll + filterStrength * previous.Roll,
                AccelX = (1 - filterStrength) * sample.AccelX + filterStrength * previous.AccelX,
                AccelY = (1 - filterStrength) * sample.AccelY + filterStrength * previous.AccelY,
                AccelZ = (1 - filterStrength) * sample.AccelZ + filterStrength * previous.AccelZ
            };
        }

        private double CalculateNoiseLevel()
        {
            if (motionHistory.Count < 10) return 0.0;

            var recent = motionHistory.TakeLast(10).ToList();
            var variance = CalculateVariance(recent.Select(s => s.Yaw).Concat(
                                           recent.Select(s => s.Pitch)).Concat(
                                           recent.Select(s => s.Roll)));
            
            return Math.Sqrt(variance);
        }

        private double CalculateVariance(IEnumerable<double> values)
        {
            var valuesList = values.ToList();
            if (valuesList.Count == 0) return 0.0;

            var average = valuesList.Average();
            var variance = valuesList.Select(v => Math.Pow(v - average, 2)).Average();
            return variance;
        }

        private double GetMedian(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int count = sorted.Count;
            
            if (count % 2 == 0)
                return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
            else
                return sorted[count / 2];
        }

        private void CheckForGestures()
        {
            if (motionHistory.Count < 10) return;

            foreach (var gesture in registeredGestures.Values)
            {
                if (IsGestureMatched(gesture))
                {
                    GestureDetected?.Invoke(this, new MotionGestureDetectedEventArgs(gesture));
                    gesture.OnDetected?.Invoke(gesture);
                }
            }
        }

        private bool IsGestureMatched(MotionGesture gesture)
        {
            var now = DateTime.UtcNow;
            var relevantSamples = motionHistory.Where(s => (now - s.Timestamp) <= gesture.TimeWindow).ToList();
            
            if (relevantSamples.Count < gesture.Patterns.Count)
                return false;

            // Simple pattern matching - would need more sophisticated algorithm for complex gestures
            var maxIntensity = relevantSamples.Max(s => Math.Abs(s.Yaw) + Math.Abs(s.Pitch) + Math.Abs(s.Roll));
            return maxIntensity >= gesture.Threshold;
        }

        /// <summary>
        /// Registers a new motion gesture
        /// </summary>
        public void RegisterGesture(MotionGesture gesture)
        {
            registeredGestures[gesture.Name] = gesture;
        }

        /// <summary>
        /// Unregisters a motion gesture
        /// </summary>
        public void UnregisterGesture(string gestureName)
        {
            registeredGestures.Remove(gestureName);
        }

        /// <summary>
        /// Gets motion statistics for analysis
        /// </summary>
        public MotionStatistics GetMotionStatistics()
        {
            if (motionHistory.Count == 0)
                return new MotionStatistics();

            var samples = motionHistory.ToList();
            
            return new MotionStatistics
            {
                SampleCount = samples.Count,
                AverageYaw = samples.Average(s => s.Yaw),
                AveragePitch = samples.Average(s => s.Pitch),
                AverageRoll = samples.Average(s => s.Roll),
                MaxYaw = samples.Max(s => Math.Abs(s.Yaw)),
                MaxPitch = samples.Max(s => Math.Abs(s.Pitch)),
                MaxRoll = samples.Max(s => Math.Abs(s.Roll)),
                NoiseLevel = CalculateNoiseLevel()
            };
        }

        /// <summary>
        /// Sets the motion filter type
        /// </summary>
        public void SetFilterType(MotionFilterType type)
        {
            filterType = type;
        }

        /// <summary>
        /// Sets filter parameters
        /// </summary>
        public void SetFilterParameters(double noiseThreshold, double adaptiveGain)
        {
            this.noiseThreshold = noiseThreshold;
            this.adaptiveGain = adaptiveGain;
        }
    }

    // Helper classes
    public class AdvancedOneEuroFilter3D
    {
        private OneEuroFilter filterX = new OneEuroFilter(1.0, 0.1);
        private OneEuroFilter filterY = new OneEuroFilter(1.0, 0.1);
        private OneEuroFilter filterZ = new OneEuroFilter(1.0, 0.1);

        public (double X, double Y, double Z) Filter(double x, double y, double z, double deltaTime)
        {
            return (
                filterX.Filter(x, deltaTime),
                filterY.Filter(y, deltaTime),
                filterZ.Filter(z, deltaTime)
            );
        }
    }

    public class KalmanFilter3D
    {
        private KalmanFilter1D filterX = new KalmanFilter1D();
        private KalmanFilter1D filterY = new KalmanFilter1D();
        private KalmanFilter1D filterZ = new KalmanFilter1D();

        public (double X, double Y, double Z) Update(double x, double y, double z)
        {
            return (
                filterX.Update(x),
                filterY.Update(y),
                filterZ.Update(z)
            );
        }
    }

    public class KalmanFilter1D
    {
        private double x = 0.0; // state
        private double P = 1.0; // covariance
        private double Q = 0.01; // process noise
        private double R = 0.1; // measurement noise

        public double Update(double measurement)
        {
            // Prediction
            P = P + Q;

            // Update
            double K = P / (P + R);
            x = x + K * (measurement - x);
            P = (1 - K) * P;

            return x;
        }
    }

    public class MotionStatistics
    {
        public int SampleCount { get; set; }
        public double AverageYaw { get; set; }
        public double AveragePitch { get; set; }
        public double AverageRoll { get; set; }
        public double MaxYaw { get; set; }
        public double MaxPitch { get; set; }
        public double MaxRoll { get; set; }
        public double NoiseLevel { get; set; }
    }

    // Event argument classes
    public class MotionGestureDetectedEventArgs : EventArgs
    {
        public MotionGesture Gesture { get; }

        public MotionGestureDetectedEventArgs(MotionGesture gesture)
        {
            Gesture = gesture;
        }
    }

    public class OrientationChangedEventArgs : EventArgs
    {
        public double Yaw { get; }
        public double Pitch { get; }
        public double Roll { get; }

        public OrientationChangedEventArgs(double yaw, double pitch, double roll)
        {
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
        }
    }
}
