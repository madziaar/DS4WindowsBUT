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
using System.Text.Json;

namespace DS4Windows
{
    public enum ProfileSwitchTrigger
    {
        GameLaunch,
        GameFocus,
        ControllerConnect,
        BatteryLevel,
        TimeOfDay,
        Manual
    }

    public class SmartProfile
    {
        public string Name { get; set; }
        public string ProfilePath { get; set; }
        public List<string> AssociatedProcesses { get; set; } = new List<string>();
        public List<string> AssociatedWindowTitles { get; set; } = new List<string>();
        public int Priority { get; set; } = 1;
        public DateTime CreatedDate { get; set; }
        public DateTime LastUsed { get; set; }
        public TimeSpan TotalUsageTime { get; set; }
        public int UsageCount { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public bool AutoOptimize { get; set; } = true;
        public ProfileSwitchTrigger SwitchTrigger { get; set; } = ProfileSwitchTrigger.GameLaunch;
    }

    public class ProfileUsageStats
    {
        public string ProfileName { get; set; }
        public TimeSpan TotalUsageTime { get; set; }
        public int LaunchCount { get; set; }
        public DateTime LastUsed { get; set; }
        public Dictionary<string, TimeSpan> GameSpecificUsage { get; set; } = new Dictionary<string, TimeSpan>();
        public double AverageSessionDuration => LaunchCount > 0 ? TotalUsageTime.TotalMinutes / LaunchCount : 0;
    }

    public class GameDetectionRule
    {
        public string GameName { get; set; }
        public List<string> ProcessNames { get; set; } = new List<string>();
        public List<string> WindowTitlePatterns { get; set; } = new List<string>();
        public List<string> ExecutablePaths { get; set; } = new List<string>();
        public string RecommendedProfile { get; set; }
        public DateTime LastDetected { get; set; }
        public bool IsActive { get; set; }
    }

    public class SmartProfileManager
    {
        private readonly Dictionary<string, SmartProfile> profiles;
        private readonly Dictionary<string, GameDetectionRule> gameRules;
        private readonly Dictionary<string, ProfileUsageStats> usageStats;
        private readonly string profilesDirectory;
        private readonly string configFilePath;
        
        private Process currentActiveProcess;
        private SmartProfile currentProfile;
        private DateTime profileSwitchTime;
        private readonly System.Timers.Timer processMonitorTimer;

        public event EventHandler<ProfileSwitchedEventArgs> ProfileSwitched;
        public event EventHandler<ProfileGameDetectedEventArgs> GameDetected;
        public event EventHandler<ProfileOptimizationEventArgs> ProfileOptimized;

        public SmartProfileManager(string profilesDirectory)
        {
            this.profilesDirectory = profilesDirectory ?? throw new ArgumentNullException(nameof(profilesDirectory));
            this.configFilePath = Path.Combine(profilesDirectory, "smart_profiles.json");
            this.profiles = new Dictionary<string, SmartProfile>();
            this.gameRules = new Dictionary<string, GameDetectionRule>();
            this.usageStats = new Dictionary<string, ProfileUsageStats>();
            this.profileSwitchTime = DateTime.UtcNow;
            
            // Monitor active processes every 2 seconds
            this.processMonitorTimer = new System.Timers.Timer(2000);
            this.processMonitorTimer.Elapsed += OnProcessMonitorTick;
            this.processMonitorTimer.Start();
            
            LoadConfiguration();
            LoadGameDetectionRules();
        }

        /// <summary>
        /// Registers a new smart profile
        /// </summary>
        public void RegisterProfile(SmartProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            
            profiles[profile.Name] = profile;
            
            if (!usageStats.ContainsKey(profile.Name))
            {
                usageStats[profile.Name] = new ProfileUsageStats
                {
                    ProfileName = profile.Name,
                    LastUsed = DateTime.UtcNow
                };
            }
            
            SaveConfiguration();
        }

        /// <summary>
        /// Creates a smart profile from an existing DS4Windows profile
        /// </summary>
        public SmartProfile CreateSmartProfile(string name, string profilePath, List<string> associatedProcesses = null)
        {
            var smartProfile = new SmartProfile
            {
                Name = name,
                ProfilePath = profilePath,
                AssociatedProcesses = associatedProcesses ?? new List<string>(),
                CreatedDate = DateTime.UtcNow,
                Priority = 1
            };
            
            RegisterProfile(smartProfile);
            return smartProfile;
        }

        /// <summary>
        /// Automatically detects the best profile for the current active application
        /// </summary>
        public SmartProfile DetectOptimalProfile()
        {
            var activeProcess = GetActiveProcess();
            if (activeProcess == null)
                return currentProfile;

            // Check if we have a specific rule for this game/application
            var detectedGame = DetectGame(activeProcess);
            if (detectedGame != null && !string.IsNullOrEmpty(detectedGame.RecommendedProfile))
            {
                if (profiles.ContainsKey(detectedGame.RecommendedProfile))
                {
                    return profiles[detectedGame.RecommendedProfile];
                }
            }

            // Find profiles associated with this process
            var matchingProfiles = profiles.Values
                .Where(p => p.AssociatedProcesses.Any(proc => 
                    string.Equals(proc, activeProcess.ProcessName, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => p.Priority)
                .ThenByDescending(p => usageStats.ContainsKey(p.Name) ? usageStats[p.Name].LaunchCount : 0)
                .ToList();

            if (matchingProfiles.Any())
            {
                return matchingProfiles.First();
            }

            // Try to match by window title
            var windowTitle = GetActiveWindowTitle();
            if (!string.IsNullOrEmpty(windowTitle))
            {
                var titleMatchingProfiles = profiles.Values
                    .Where(p => p.AssociatedWindowTitles.Any(title => 
                        windowTitle.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderByDescending(p => p.Priority)
                    .ToList();

                if (titleMatchingProfiles.Any())
                {
                    return titleMatchingProfiles.First();
                }
            }

            return currentProfile; // Keep current profile if no match found
        }

        /// <summary>
        /// Switches to a profile automatically or manually
        /// </summary>
        public bool SwitchToProfile(string profileName, ProfileSwitchTrigger trigger = ProfileSwitchTrigger.Manual)
        {
            if (!profiles.ContainsKey(profileName))
                return false;

            var newProfile = profiles[profileName];
            var oldProfile = currentProfile;
            
            // Update usage statistics for the previous profile
            if (currentProfile != null)
            {
                var sessionDuration = DateTime.UtcNow - profileSwitchTime;
                UpdateUsageStats(currentProfile.Name, sessionDuration);
            }

            currentProfile = newProfile;
            profileSwitchTime = DateTime.UtcNow;
            
            // Update profile usage
            if (usageStats.ContainsKey(profileName))
            {
                var stats = usageStats[profileName];
                stats.LaunchCount++;
                stats.LastUsed = DateTime.UtcNow;
            }

            newProfile.LastUsed = DateTime.UtcNow;
            newProfile.UsageCount++;

            ProfileSwitched?.Invoke(this, new ProfileSwitchedEventArgs(oldProfile, newProfile, trigger));
            
            // Auto-optimize profile if enabled
            if (newProfile.AutoOptimize)
            {
                OptimizeProfile(newProfile);
            }
            
            SaveConfiguration();
            return true;
        }

        /// <summary>
        /// Automatically optimizes a profile based on usage patterns
        /// </summary>
        public void OptimizeProfile(SmartProfile profile)
        {
            if (profile == null) return;

            var optimizations = new List<string>();
            
            // Get usage statistics for this profile
            if (usageStats.ContainsKey(profile.Name))
            {
                var stats = usageStats[profile.Name];
                
                // Optimize based on usage patterns
                if (stats.AverageSessionDuration > 60) // Long sessions
                {
                    optimizations.Add("Reduced lightbar brightness for battery conservation");
                    optimizations.Add("Enabled idle timeout for power saving");
                }
                
                if (stats.LaunchCount > 50) // Frequently used
                {
                    profile.Priority = Math.Min(10, profile.Priority + 1);
                    optimizations.Add("Increased profile priority due to frequent use");
                }
                
                // Optimize for detected games
                var detectedGames = stats.GameSpecificUsage.Keys;
                foreach (var game in detectedGames)
                {
                    if (IsGameRequiringLowLatency(game))
                    {
                        optimizations.Add($"Optimized for low-latency gaming ({game})");
                    }
                }
            }
            
            if (optimizations.Any())
            {
                ProfileOptimized?.Invoke(this, new ProfileOptimizationEventArgs(profile, optimizations));
            }
        }

        /// <summary>
        /// Gets profile recommendations based on current context
        /// </summary>
        public List<SmartProfile> GetProfileRecommendations()
        {
            var recommendations = new List<SmartProfile>();
            var activeProcess = GetActiveProcess();
            
            if (activeProcess != null)
            {
                // Recommend based on current application
                var contextualProfiles = profiles.Values
                    .Where(p => p.AssociatedProcesses.Contains(activeProcess.ProcessName, StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(p => usageStats.ContainsKey(p.Name) ? usageStats[p.Name].LaunchCount : 0)
                    .Take(3)
                    .ToList();
                
                recommendations.AddRange(contextualProfiles);
            }
            
            // Add most frequently used profiles
            var frequentProfiles = profiles.Values
                .OrderByDescending(p => usageStats.ContainsKey(p.Name) ? usageStats[p.Name].LaunchCount : 0)
                .Take(5)
                .Where(p => !recommendations.Contains(p))
                .ToList();
            
            recommendations.AddRange(frequentProfiles);
            
            return recommendations.Take(5).ToList();
        }

        /// <summary>
        /// Adds a game detection rule
        /// </summary>
        public void AddGameDetectionRule(GameDetectionRule rule)
        {
            gameRules[rule.GameName] = rule;
            SaveConfiguration();
        }

        /// <summary>
        /// Gets comprehensive usage analytics
        /// </summary>
        public ProfileAnalytics GetProfileAnalytics()
        {
            return new ProfileAnalytics
            {
                TotalProfiles = profiles.Count,
                ActiveProfiles = profiles.Values.Count(p => (DateTime.UtcNow - p.LastUsed).TotalDays <= 30),
                MostUsedProfile = usageStats.Values.OrderByDescending(s => s.LaunchCount).FirstOrDefault()?.ProfileName,
                TotalSwitches = usageStats.Values.Sum(s => s.LaunchCount),
                AverageSessionDuration = TimeSpan.FromMinutes(usageStats.Values.Average(s => s.AverageSessionDuration)),
                ProfileUsageStats = usageStats.Values.ToList(),
                GameDetectionRules = gameRules.Values.ToList()
            };
        }

        private void OnProcessMonitorTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            var optimalProfile = DetectOptimalProfile();
            
            if (optimalProfile != null && optimalProfile != currentProfile)
            {
                // Auto-switch if the profile has the appropriate trigger
                if (optimalProfile.SwitchTrigger == ProfileSwitchTrigger.GameFocus ||
                    optimalProfile.SwitchTrigger == ProfileSwitchTrigger.GameLaunch)
                {
                    SwitchToProfile(optimalProfile.Name, optimalProfile.SwitchTrigger);
                }
            }
        }

        private Process GetActiveProcess()
        {
            try
            {
                var foregroundWindow = WindowNativeMethods.GetForegroundWindow();
                WindowNativeMethods.GetWindowThreadProcessId(foregroundWindow, out uint processId);
                return Process.GetProcessById((int)processId);
            }
            catch
            {
                return null;
            }
        }

        private string GetActiveWindowTitle()
        {
            try
            {
                var foregroundWindow = WindowNativeMethods.GetForegroundWindow();
                const int nChars = 256;
                var buffer = new System.Text.StringBuilder(nChars);
                return WindowNativeMethods.GetWindowText(foregroundWindow, buffer, nChars) > 0 ? buffer.ToString() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private GameDetectionRule DetectGame(Process process)
        {
            return gameRules.Values.FirstOrDefault(rule =>
                rule.ProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase) ||
                rule.ExecutablePaths.Any(path => process.MainModule?.FileName?.EndsWith(path, StringComparison.OrdinalIgnoreCase) == true));
        }

        private void UpdateUsageStats(string profileName, TimeSpan sessionDuration)
        {
            if (!usageStats.ContainsKey(profileName))
                usageStats[profileName] = new ProfileUsageStats { ProfileName = profileName };

            var stats = usageStats[profileName];
            stats.TotalUsageTime = stats.TotalUsageTime.Add(sessionDuration);
            
            // Track game-specific usage
            var activeProcess = GetActiveProcess();
            if (activeProcess != null)
            {
                var gameName = activeProcess.ProcessName;
                if (!stats.GameSpecificUsage.ContainsKey(gameName))
                    stats.GameSpecificUsage[gameName] = TimeSpan.Zero;
                
                stats.GameSpecificUsage[gameName] = stats.GameSpecificUsage[gameName].Add(sessionDuration);
            }
        }

        private bool IsGameRequiringLowLatency(string gameName)
        {
            var competitiveGames = new[]
            {
                "csgo", "valorant", "overwatch", "rocketleague", "apexlegends",
                "fortnite", "pubg", "callofduty", "rainbow6", "doom"
            };
            
            return competitiveGames.Any(game => gameName.IndexOf(game, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    var json = File.ReadAllText(configFilePath);
                    var config = JsonSerializer.Deserialize<SmartProfileConfig>(json);
                    
                    foreach (var profile in config.Profiles)
                    {
                        profiles[profile.Name] = profile;
                    }
                    
                    foreach (var stats in config.UsageStats)
                    {
                        usageStats[stats.ProfileName] = stats;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but continue
                Debug.WriteLine($"Failed to load smart profile configuration: {ex.Message}");
            }
        }

        private void LoadGameDetectionRules()
        {
            // Load built-in game detection rules
            var builtInRules = new[]
            {
                new GameDetectionRule
                {
                    GameName = "Steam Games",
                    ProcessNames = new List<string> { "steam", "steamwebhelper" },
                    WindowTitlePatterns = new List<string> { "Steam" }
                },
                new GameDetectionRule
                {
                    GameName = "Epic Games",
                    ProcessNames = new List<string> { "epicgameslauncher", "unrealengine" },
                    WindowTitlePatterns = new List<string> { "Epic Games Launcher" }
                }
            };
            
            foreach (var rule in builtInRules)
            {
                gameRules[rule.GameName] = rule;
            }
        }

        private void SaveConfiguration()
        {
            try
            {
                var config = new SmartProfileConfig
                {
                    Profiles = profiles.Values.ToList(),
                    UsageStats = usageStats.Values.ToList(),
                    GameRules = gameRules.Values.ToList()
                };
                
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save smart profile configuration: {ex.Message}");
            }
        }

        public void Dispose()
        {
            processMonitorTimer?.Dispose();
            SaveConfiguration();
        }
    }

    // Configuration and data classes
    public class SmartProfileConfig
    {
        public List<SmartProfile> Profiles { get; set; } = new List<SmartProfile>();
        public List<ProfileUsageStats> UsageStats { get; set; } = new List<ProfileUsageStats>();
        public List<GameDetectionRule> GameRules { get; set; } = new List<GameDetectionRule>();
    }

    public class ProfileAnalytics
    {
        public int TotalProfiles { get; set; }
        public int ActiveProfiles { get; set; }
        public string MostUsedProfile { get; set; }
        public int TotalSwitches { get; set; }
        public TimeSpan AverageSessionDuration { get; set; }
        public List<ProfileUsageStats> ProfileUsageStats { get; set; }
        public List<GameDetectionRule> GameDetectionRules { get; set; }
    }

    // Event argument classes
    public class ProfileSwitchedEventArgs : EventArgs
    {
        public SmartProfile OldProfile { get; }
        public SmartProfile NewProfile { get; }
        public ProfileSwitchTrigger Trigger { get; }

        public ProfileSwitchedEventArgs(SmartProfile oldProfile, SmartProfile newProfile, ProfileSwitchTrigger trigger)
        {
            OldProfile = oldProfile;
            NewProfile = newProfile;
            Trigger = trigger;
        }
    }

    public class ProfileGameDetectedEventArgs : EventArgs
    {
        public GameDetectionRule DetectedGame { get; }
        public Process Process { get; }

        public ProfileGameDetectedEventArgs(GameDetectionRule detectedGame, Process process)
        {
            DetectedGame = detectedGame;
            Process = process;
        }
    }

    public class ProfileOptimizationEventArgs : EventArgs
    {
        public SmartProfile Profile { get; }
        public List<string> Optimizations { get; }

        public ProfileOptimizationEventArgs(SmartProfile profile, List<string> optimizations)
        {
            Profile = profile;
            Optimizations = optimizations;
        }
    }

    // Native methods for window detection
    internal static class WindowNativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    }
}
