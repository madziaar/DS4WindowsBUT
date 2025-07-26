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
using System.Drawing;
using System.Threading;

namespace DS4Windows
{
    public enum LightbarEffectType
    {
        Static,
        Breathing,
        Rainbow,
        Pulse,
        BatteryIndicator,
        HealthBar,
        AmmoIndicator,
        NotificationFlash,
        Custom
    }

    public class LightbarEffect
    {
        public LightbarEffectType Type { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Loop { get; set; }
        public int Priority { get; set; } // Higher priority effects override lower ones
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }

    public class AdvancedLightbarEffects
    {
        private readonly DS4Device device;
        private readonly Timer effectTimer;
        private readonly List<LightbarEffect> activeEffects;
        private readonly object effectLock = new object();
        
        private DateTime effectStartTime;
        private int rainbowHue = 0;
        private bool breathingIncreasing = true;
        private double breathingValue = 0.0;
        
        // Effect parameters
        private const int EFFECT_UPDATE_INTERVAL_MS = 50; // 20 FPS
        private const int RAINBOW_HUE_STEP = 2;
        private const double BREATHING_STEP = 0.05;

        public AdvancedLightbarEffects(DS4Device device)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.activeEffects = new List<LightbarEffect>();
            this.effectTimer = new Timer(UpdateEffects, null, EFFECT_UPDATE_INTERVAL_MS, EFFECT_UPDATE_INTERVAL_MS);
            this.effectStartTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Adds a new lightbar effect
        /// </summary>
        public void AddEffect(LightbarEffect effect)
        {
            if (effect == null) return;

            lock (effectLock)
            {
                // Remove any existing effect of the same type unless it's a notification
                if (effect.Type != LightbarEffectType.NotificationFlash)
                {
                    activeEffects.RemoveAll(e => e.Type == effect.Type);
                }
                
                activeEffects.Add(effect);
                activeEffects.Sort((e1, e2) => e2.Priority.CompareTo(e1.Priority)); // Sort by priority descending
            }
        }

        /// <summary>
        /// Removes effects of a specific type
        /// </summary>
        public void RemoveEffect(LightbarEffectType effectType)
        {
            lock (effectLock)
            {
                activeEffects.RemoveAll(e => e.Type == effectType);
            }
        }

        /// <summary>
        /// Clears all active effects
        /// </summary>
        public void ClearAllEffects()
        {
            lock (effectLock)
            {
                activeEffects.Clear();
            }
        }

        /// <summary>
        /// Creates a breathing effect
        /// </summary>
        public static LightbarEffect CreateBreathingEffect(Color baseColor, TimeSpan? duration = null, int priority = 1)
        {
            return new LightbarEffect
            {
                Type = LightbarEffectType.Breathing,
                Duration = duration ?? TimeSpan.FromSeconds(-1), // -1 means infinite
                Loop = true,
                Priority = priority,
                Parameters = new Dictionary<string, object>
                {
                    { "BaseColor", baseColor },
                    { "MinBrightness", 0.2 },
                    { "MaxBrightness", 1.0 }
                }
            };
        }

        /// <summary>
        /// Creates a rainbow cycling effect
        /// </summary>
        public static LightbarEffect CreateRainbowEffect(TimeSpan? duration = null, int priority = 1)
        {
            return new LightbarEffect
            {
                Type = LightbarEffectType.Rainbow,
                Duration = duration ?? TimeSpan.FromSeconds(-1),
                Loop = true,
                Priority = priority,
                Parameters = new Dictionary<string, object>
                {
                    { "Saturation", 1.0 },
                    { "Brightness", 1.0 },
                    { "Speed", 1.0 }
                }
            };
        }

        /// <summary>
        /// Creates a battery level indicator effect
        /// </summary>
        public static LightbarEffect CreateBatteryIndicatorEffect(int batteryLevel, int priority = 5)
        {
            Color batteryColor;
            if (batteryLevel > 60)
                batteryColor = Color.Green;
            else if (batteryLevel > 30)
                batteryColor = Color.Yellow;
            else if (batteryLevel > 15)
                batteryColor = Color.Orange;
            else
                batteryColor = Color.Red;

            return new LightbarEffect
            {
                Type = LightbarEffectType.BatteryIndicator,
                Duration = TimeSpan.FromSeconds(3),
                Loop = false,
                Priority = priority,
                Parameters = new Dictionary<string, object>
                {
                    { "BatteryLevel", batteryLevel },
                    { "Color", batteryColor }
                }
            };
        }

        /// <summary>
        /// Creates a health bar effect (for games)
        /// </summary>
        public static LightbarEffect CreateHealthBarEffect(double healthPercentage, int priority = 3)
        {
            // Color transitions from green (100%) to red (0%)
            var hue = (healthPercentage / 100.0) * 120.0; // 120 is green, 0 is red in HSV
            var healthColor = ColorFromHSV(hue, 1.0, 1.0);

            return new LightbarEffect
            {
                Type = LightbarEffectType.HealthBar,
                Duration = TimeSpan.FromSeconds(-1),
                Loop = false,
                Priority = priority,
                Parameters = new Dictionary<string, object>
                {
                    { "HealthPercentage", healthPercentage },
                    { "Color", healthColor }
                }
            };
        }

        /// <summary>
        /// Creates a notification flash effect
        /// </summary>
        public static LightbarEffect CreateNotificationFlash(Color flashColor, int flashCount = 3, int priority = 10)
        {
            return new LightbarEffect
            {
                Type = LightbarEffectType.NotificationFlash,
                Duration = TimeSpan.FromMilliseconds(flashCount * 500),
                Loop = false,
                Priority = priority,
                Parameters = new Dictionary<string, object>
                {
                    { "FlashColor", flashColor },
                    { "FlashCount", flashCount },
                    { "FlashDuration", 250 } // ms per flash
                }
            };
        }

        private void UpdateEffects(object state)
        {
            if (device == null) return;

            lock (effectLock)
            {
                // Remove expired effects
                var now = DateTime.UtcNow;
                activeEffects.RemoveAll(e => e.Duration.TotalSeconds > 0 && 
                                           (now - effectStartTime) > e.Duration);

                if (activeEffects.Count == 0)
                    return;

                // Get the highest priority effect
                var activeEffect = activeEffects[0];
                var color = CalculateEffectColor(activeEffect, now);

                // Apply the color to the device
                device.LightBarColor = new DS4Color(color);
            }
        }

        private Color CalculateEffectColor(LightbarEffect effect, DateTime currentTime)
        {
            var elapsed = currentTime - effectStartTime;
            
            switch (effect.Type)
            {
                case LightbarEffectType.Static:
                    return (Color)effect.Parameters.GetValueOrDefault("Color", Color.Blue);

                case LightbarEffectType.Breathing:
                    return CalculateBreathingColor(effect, elapsed);

                case LightbarEffectType.Rainbow:
                    return CalculateRainbowColor(effect, elapsed);

                case LightbarEffectType.BatteryIndicator:
                    return (Color)effect.Parameters.GetValueOrDefault("Color", Color.Green);

                case LightbarEffectType.HealthBar:
                    return (Color)effect.Parameters.GetValueOrDefault("Color", Color.Green);

                case LightbarEffectType.NotificationFlash:
                    return CalculateNotificationFlashColor(effect, elapsed);

                default:
                    return Color.Blue;
            }
        }

        private Color CalculateBreathingColor(LightbarEffect effect, TimeSpan elapsed)
        {
            var baseColor = (Color)effect.Parameters.GetValueOrDefault("BaseColor", Color.Blue);
            var minBrightness = (double)effect.Parameters.GetValueOrDefault("MinBrightness", 0.2);
            var maxBrightness = (double)effect.Parameters.GetValueOrDefault("MaxBrightness", 1.0);

            // Update breathing value
            if (breathingIncreasing)
            {
                breathingValue += BREATHING_STEP;
                if (breathingValue >= 1.0)
                {
                    breathingValue = 1.0;
                    breathingIncreasing = false;
                }
            }
            else
            {
                breathingValue -= BREATHING_STEP;
                if (breathingValue <= 0.0)
                {
                    breathingValue = 0.0;
                    breathingIncreasing = true;
                }
            }

            var brightness = minBrightness + (maxBrightness - minBrightness) * breathingValue;
            return Color.FromArgb(
                (int)(baseColor.R * brightness),
                (int)(baseColor.G * brightness),
                (int)(baseColor.B * brightness)
            );
        }

        private Color CalculateRainbowColor(LightbarEffect effect, TimeSpan elapsed)
        {
            var saturation = (double)effect.Parameters.GetValueOrDefault("Saturation", 1.0);
            var brightness = (double)effect.Parameters.GetValueOrDefault("Brightness", 1.0);
            var speed = (double)effect.Parameters.GetValueOrDefault("Speed", 1.0);

            rainbowHue = (rainbowHue + (int)(RAINBOW_HUE_STEP * speed)) % 360;
            return ColorFromHSV(rainbowHue, saturation, brightness);
        }

        private Color CalculateNotificationFlashColor(LightbarEffect effect, TimeSpan elapsed)
        {
            var flashColor = (Color)effect.Parameters.GetValueOrDefault("FlashColor", Color.White);
            var flashDuration = (int)effect.Parameters.GetValueOrDefault("FlashDuration", 250);
            
            var flashCycle = (int)(elapsed.TotalMilliseconds / flashDuration);
            return flashCycle % 2 == 0 ? flashColor : Color.Black;
        }

        private static Color ColorFromHSV(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            if (hi == 0)
                return Color.FromArgb(255, v, t, p);
            else if (hi == 1)
                return Color.FromArgb(255, q, v, p);
            else if (hi == 2)
                return Color.FromArgb(255, p, v, t);
            else if (hi == 3)
                return Color.FromArgb(255, p, q, v);
            else if (hi == 4)
                return Color.FromArgb(255, t, p, v);
            else
                return Color.FromArgb(255, v, p, q);
        }

        /// <summary>
        /// Applies lightbar effects based on detected game
        /// </summary>
        public void ApplyGameProfile(string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return;

            // Apply game-specific lighting effects
            // This is a basic implementation - could be expanded based on game type
            var gameColor = GetGameColor(gameName);
            ApplyEffect(new LightbarEffect
            {
                Type = LightbarEffectType.Solid,
                Duration = TimeSpan.FromMinutes(5),
                Loop = true,
                Priority = 7
            });
        }

        private Color GetGameColor(string gameName)
        {
            // Simple color mapping based on game name
            var hash = gameName.GetHashCode();
            var r = (byte)((hash & 0xFF0000) >> 16);
            var g = (byte)((hash & 0x00FF00) >> 8);
            var b = (byte)(hash & 0x0000FF);
            return Color.FromArgb(255, r, g, b);
        }

        public void Dispose()
        {
            effectTimer?.Dispose();
        }
    }
}
