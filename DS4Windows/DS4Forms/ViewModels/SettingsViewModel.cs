﻿/*
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
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using DS4Windows;
using static DS4Windows.Util;
using Microsoft.Win32;

namespace DS4WinWPF.DS4Forms.ViewModels
{
    public class SettingsViewModel
    {
        // Re-Enable Ex Mode
        public bool HideDS4Controller
        {
            get => DS4Windows.Global.UseExclusiveMode;
            set => DS4Windows.Global.UseExclusiveMode = value;
        }

        public bool SwipeTouchSwitchProfile { get => DS4Windows.Global.SwipeProfiles;
            set => DS4Windows.Global.SwipeProfiles = value; }

        private bool runAtStartup;
        public bool RunAtStartup
        {
            get => runAtStartup;
            set
            {
                runAtStartup = value;
                RunAtStartupChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler RunAtStartupChanged;

        private bool runStartProg;
        public bool RunStartProg
        {
            get => runStartProg;
            set
            {
                runStartProg = value;
                RunStartProgChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler RunStartProgChanged;

        private bool runStartTask;
        public bool RunStartTask
        {
            get => runStartTask;
            set
            {
                runStartTask = value;
                RunStartTaskChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler RunStartTaskChanged;

        private bool canWriteTask;
        public bool CanWriteTask { get => canWriteTask; }

        public ImageSource uacSource;
        public ImageSource UACSource { get => uacSource; }

        public ImageSource questionMarkSource;
        public ImageSource QuestionMarkSource { get => questionMarkSource; }

        private Visibility showRunStartPanel = Visibility.Collapsed;
        public Visibility ShowRunStartPanel {
            get => showRunStartPanel;
            set
            {
                if (showRunStartPanel == value) return;
                showRunStartPanel = value;
                ShowRunStartPanelChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler ShowRunStartPanelChanged;

        private Visibility _isProfileChangedCheckVisible;

        public Visibility IsProfileChangedCheckVisible
        {
            get => _isProfileChangedCheckVisible;
            private set
            {
                _isProfileChangedCheckVisible = value;
                IsProfileChangedCheckVisibleChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler IsProfileChangedCheckVisibleChanged;

        public bool ProfileChangedNotification
        {
            get => Global.ProfileChangedNotification;
            set => Global.ProfileChangedNotification = value;
        }

        public int ShowNotificationsIndex
        {
            get => DS4Windows.Global.Notifications;
            set
            {
                Global.Notifications = value;
                // display only when all notifications are on
                IsProfileChangedCheckVisible = value == 2 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public bool DisconnectBTStop { get => DS4Windows.Global.DCBTatStop; set => DS4Windows.Global.DCBTatStop = value; }
        public bool FlashHighLatency { get => DS4Windows.Global.FlashWhenLate; set => DS4Windows.Global.FlashWhenLate = value; }
        public int FlashHighLatencyAt 
        { 
            get => DS4Windows.Global.FlashWhenLateAt; 
            set 
            {
                if (value < 1 || value > 9999)
                {
                    DS4Windows.AppLogger.LogToGui($"Invalid flash latency threshold: {value}ms. Must be between 1-9999ms", true);
                    return;
                }
                DS4Windows.Global.FlashWhenLateAt = value; 
            } 
        }
        public bool StartMinimize { get => DS4Windows.Global.StartMinimized; set => DS4Windows.Global.StartMinimized = value; }
        public bool MinimizeToTaskbar { get => DS4Windows.Global.MinToTaskbar; set => DS4Windows.Global.MinToTaskbar = value; }
        public bool CloseMinimizes { get => DS4Windows.Global.CloseMini; set => DS4Windows.Global.CloseMini = value; }
        public bool QuickCharge { get => DS4Windows.Global.QuickCharge; set => DS4Windows.Global.QuickCharge = value; }

        public int IconChoiceIndex
        {
            get => (int)DS4Windows.Global.UseIconChoice;
            set
            {
                int temp = (int)DS4Windows.Global.UseIconChoice;
                if (temp == value) return;
                DS4Windows.Global.UseIconChoice = (DS4Windows.TrayIconChoice)value;
                IconChoiceIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler IconChoiceIndexChanged;

        public int AppChoiceIndex
        {
            get => (int)DS4Windows.Global.UseCurrentTheme;
            set
            {
                int temp = (int)DS4Windows.Global.UseCurrentTheme;
                if (temp == value) return;
                DS4Windows.Global.UseCurrentTheme = (DS4Windows.AppThemeChoice)value;
                AppChoiceIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler AppChoiceIndexChanged;

        public bool CheckForUpdates
        {
            get => DS4Windows.Global.CheckWhen > 0;
            set
            {
                DS4Windows.Global.CheckWhen = value ? 24 : 0;
                CheckForNoUpdatesWhen();
            }
        }
        public event EventHandler CheckForUpdatesChanged;

        public int CheckEvery
        {
            get
            {
                int temp = DS4Windows.Global.CheckWhen;
                if (temp > 23)
                {
                    temp = temp / 24;
                }
                return temp;
            }
            set
            {
                int temp;
                if (checkEveryUnitIdx == 0 && value < 24)
                {
                    temp = DS4Windows.Global.CheckWhen;
                    if (temp != value)
                    {
                        DS4Windows.Global.CheckWhen = value;
                        CheckForNoUpdatesWhen();
                    }
                }
                else if (checkEveryUnitIdx == 1)
                {
                    temp = DS4Windows.Global.CheckWhen / 24;
                    if (temp != value)
                    {
                        DS4Windows.Global.CheckWhen = value * 24;
                        CheckForNoUpdatesWhen();
                    }
                }
            }
        }
        public event EventHandler CheckEveryChanged;

        private int checkEveryUnitIdx = 1;
        public int CheckEveryUnit
        {
            get
            {
                return checkEveryUnitIdx;
            }
            set
            {
                if (checkEveryUnitIdx == value) return;
                checkEveryUnitIdx = value;
                CheckEveryUnitChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler CheckEveryUnitChanged;

        public bool UseOSCServer
        {
            get => DS4Windows.Global.isUsingOSCServer();
            set
            {
                if (DS4Windows.Global.isUsingOSCServer() == value) return;
                DS4Windows.Global.setUsingOSCServer(value);
                UseOSCServerChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler UseOSCServerChanged;
        public int OscPort 
        { 
            get => DS4Windows.Global.getOSCServerPortNum(); 
            set 
            {
                if (value < 1024 || value > 65535)
                {
                    DS4Windows.AppLogger.LogToGui($"Invalid OSC port number: {value}. Must be between 1024-65535", true);
                    return;
                }
                DS4Windows.Global.setOSCServerPort(value); 
            } 
        }
        
        public bool InterpretingOscMonitoring { get => DS4Windows.Global.isInterpretingOscMonitoring(); set => DS4Windows.Global.setInterpretingOscMonitoring(value); }

        public bool UseOSCSender
        {
            get => DS4Windows.Global.isUsingOSCSender();
            set
            {
                if (DS4Windows.Global.isUsingOSCSender() == value) return;
                DS4Windows.Global.setUsingOSCSender(value);
                UseOSCSenderChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler UseOSCSenderChanged;
        public int OscSendPort 
        { 
            get => DS4Windows.Global.getOSCSenderPortNum(); 
            set 
            {
                if (value < 1024 || value > 65535)
                {
                    DS4Windows.AppLogger.LogToGui($"Invalid OSC send port number: {value}. Must be between 1024-65535", true);
                    return;
                }
                DS4Windows.Global.setOSCSenderPort(value); 
            } 
        }

        public string OscSenderAddress
        {
            get => DS4Windows.Global.getOSCSenderAddress();
            set => DS4Windows.Global.setOSCSenderAddress(value);
        }


        public bool UseUDPServer
        {
            get => DS4Windows.Global.isUsingUDPServer();
            set
            {
                if (DS4Windows.Global.isUsingUDPServer() == value) return;
                DS4Windows.Global.setUsingUDPServer(value);
                UseUDPServerChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler UseUDPServerChanged;

        public string UdpIpAddress { get => DS4Windows.Global.getUDPServerListenAddress();
            set => DS4Windows.Global.setUDPServerListenAddress(value); }
        public int UdpPort 
        { 
            get => DS4Windows.Global.getUDPServerPortNum(); 
            set 
            {
                if (value < 1024 || value > 65535)
                {
                    DS4Windows.AppLogger.LogToGui($"Invalid UDP port number: {value}. Must be between 1024-65535", true);
                    return;
                }
                DS4Windows.Global.setUDPServerPort(value); 
            } 
        }

        public bool UseUdpSmoothing
        {
            get => DS4Windows.Global.UseUDPSeverSmoothing;
            set
            {
                bool temp = DS4Windows.Global.UseUDPSeverSmoothing;
                if (temp == value) return;
                DS4Windows.Global.UseUDPSeverSmoothing = value;
                UseUdpSmoothingChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler UseUdpSmoothingChanged;

        public Visibility UdpServerOneEuroPanelVisibility
        {
            get => DS4Windows.Global.isUsingUDPServer() && DS4Windows.Global.UseUDPSeverSmoothing ? Visibility.Visible : Visibility.Collapsed;
        }
        public event EventHandler UdpServerOneEuroPanelVisibilityChanged;

        public double UdpSmoothMinCutoff
        {
            get => DS4Windows.Global.UDPServerSmoothingMincutoff;
            set
            {
                double temp = DS4Windows.Global.UDPServerSmoothingMincutoff;
                if (temp == value) return;
                DS4Windows.Global.UDPServerSmoothingMincutoff = value;
                UdpSmoothMinCutoffChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler UdpSmoothMinCutoffChanged;

        public double UdpSmoothBeta
        {
            get => DS4Windows.Global.UDPServerSmoothingBeta;
            set
            {
                double temp = DS4Windows.Global.UDPServerSmoothingBeta;
                if (temp == value) return;
                DS4Windows.Global.UDPServerSmoothingBeta = value;
                UdpSmoothBetaChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler UdpSmoothBetaChanged;

        public bool UseCustomSteamFolder
        {
            get => DS4Windows.Global.UseCustomSteamFolder;
            set
            {
                DS4Windows.Global.UseCustomSteamFolder = value;
                UseCustomSteamFolderChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler UseCustomSteamFolderChanged;

        public string CustomSteamFolder
        {
            get => DS4Windows.Global.CustomSteamFolder;
            set
            {
                string temp = DS4Windows.Global.CustomSteamFolder;
                if (temp == value) return;
                
                if (string.IsNullOrEmpty(value))
                {
                    DS4Windows.Global.CustomSteamFolder = value;
                    return;
                }
                
                try
                {
                    if (Directory.Exists(value))
                    {
                        DS4Windows.Global.CustomSteamFolder = value;
                    }
                    else
                    {
                        DS4Windows.AppLogger.LogToGui($"Custom Steam folder does not exist: {value}", true);
                    }
                }
                catch (Exception ex)
                {
                    DS4Windows.AppLogger.LogToGui($"Error validating Steam folder path: {ex.Message}", true);
                }
            }
        }

        private bool viewEnabled = true;
        public bool ViewEnabled
        {
            get => viewEnabled;
            set
            {
                viewEnabled = value;
                ViewEnabledChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler ViewEnabledChanged;

        public string FakeExeName
        {
            get => DS4Windows.Global.FakeExeName;
            set
            {
                string temp = DS4Windows.Global.FakeExeName;
                if (temp == value) return;
                DS4Windows.Global.FakeExeName = value;
                FakeExeNameChanged?.Invoke(this, EventArgs.Empty);
                FakeExeNameChangeCompare?.Invoke(this, temp, value);
            }
        }
        public event EventHandler FakeExeNameChanged;
        public event FakeExeNameChangeHandler FakeExeNameChangeCompare;
        public delegate void FakeExeNameChangeHandler(SettingsViewModel sender,
            string oldvalue, string newvalue);


        public bool HidHideClientFound
        {
            get
            {
                bool result = DS4Windows.Global.hidHideInstalled &&
                    !string.IsNullOrEmpty(DS4Windows.Util.GetHidHideClientPath());

                return result;
            }
        }
        public event EventHandler HidHideClientFoundChanged;

        private List<MonitorChoiceListing> absMonitorChoices = new List<MonitorChoiceListing>();
        public List<MonitorChoiceListing> AbsMonitorChoices => absMonitorChoices;
        public event EventHandler AbsMonitorChoicesChanged;

        //private string absMonitorSettingEDID = string.Empty;
        public string AbsMonitorSettingEDID
        {
            get => Global.AbsoluteDisplayEDID;
            set => Global.AbsoluteDisplayEDID = value;
        }

        public int ProcessPriorityIndex
        {
            get => Global.ProcessPriority;
            set
            {
                Global.ProcessPriority = value;
                ProcessPriorityIndexChanged?.Invoke(this, EventArgs.Empty);
            }

        }

        public event EventHandler ProcessPriorityIndexChanged;

        public SettingsViewModel()
        {
            checkEveryUnitIdx = 1;
            IsProfileChangedCheckVisible = Global.Notifications == 2 ? Visibility.Visible : Visibility.Collapsed;

            int checklapse = DS4Windows.Global.CheckWhen;
            if (checklapse < 24 && checklapse > 0)
            {
                checkEveryUnitIdx = 0;
            }

            CheckStartupOptions();

            Icon img = SystemIcons.Shield;
            Bitmap bitmap = img.ToBitmap();
            IntPtr hBitmap = bitmap.GetHbitmap();

            ImageSource wpfBitmap =
                 Imaging.CreateBitmapSourceFromHBitmap(
                      hBitmap, IntPtr.Zero, Int32Rect.Empty,
                      BitmapSizeOptions.FromEmptyOptions());
            uacSource = wpfBitmap;

            img = SystemIcons.Question;
            wpfBitmap =
                 Imaging.CreateBitmapSourceFromHBitmap(
                      img.ToBitmap().GetHbitmap(), IntPtr.Zero, Int32Rect.Empty,
                      BitmapSizeOptions.FromEmptyOptions());
            questionMarkSource = wpfBitmap;

            runStartProg = StartupMethods.HasStartProgEntry();
            try
            {
                runStartTask = StartupMethods.HasTaskEntry();
            }
            catch (COMException ex)
            {
                DS4Windows.AppLogger.LogToGui(string.Format("Error in TaskService. Check WinOS TaskScheduler service functionality. {0}", ex.Message), true);
            }

            runAtStartup = runStartProg || runStartTask;
            canWriteTask = DS4Windows.Global.IsAdministrator();

            if (!runAtStartup)
            {
                runStartProg = true;
            }
            else if (runStartProg && runStartTask)
            {
                runStartProg = false;
                if (StartupMethods.CanWriteStartEntry())
                {
                    StartupMethods.DeleteStartProgEntry();
                }
            }

            if (runAtStartup && runStartProg)
            {
                bool locChange = StartupMethods.CheckStartupExeLocation();
                if (locChange)
                {
                    if (StartupMethods.CanWriteStartEntry())
                    {
                        StartupMethods.DeleteStartProgEntry();
                        StartupMethods.WriteStartProgEntry();
                    }
                    else
                    {
                        runAtStartup = false;
                        showRunStartPanel = Visibility.Collapsed;
                    }
                }
            }
            else if (runAtStartup && runStartTask)
            {
                if (canWriteTask)
                {
                    StartupMethods.DeleteOldTaskEntry();
                    StartupMethods.WriteTaskEntry();
                }
            }

            if (runAtStartup)
            {
                showRunStartPanel = Visibility.Visible;
            }

            RefreshMonitorChoices();

            RunAtStartupChanged += SettingsViewModel_RunAtStartupChanged;
            RunStartProgChanged += SettingsViewModel_RunStartProgChanged;
            RunStartTaskChanged += SettingsViewModel_RunStartTaskChanged;
            FakeExeNameChanged += SettingsViewModel_FakeExeNameChanged;
            FakeExeNameChangeCompare += SettingsViewModel_FakeExeNameChangeCompare;
            UseUdpSmoothingChanged += SettingsViewModel_UseUdpSmoothingChanged;
            UseUDPServerChanged += SettingsViewModel_UseUDPServerChanged;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

            //CheckForUpdatesChanged += SettingsViewModel_CheckForUpdatesChanged;
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            RefreshMonitorChoices();
        }

        private void SettingsViewModel_UseUDPServerChanged(object sender, EventArgs e)
        {
            UdpServerOneEuroPanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SettingsViewModel_UseUdpSmoothingChanged(object sender, EventArgs e)
        {
            UdpServerOneEuroPanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SettingsViewModel_FakeExeNameChangeCompare(SettingsViewModel sender,
            string oldvalue, string newvalue)
        {
            string old_exefile = Path.Combine(DS4Windows.Global.exedirpath, $"{oldvalue}.exe");
            string old_conf_file = Path.Combine(DS4Windows.Global.exedirpath, $"{oldvalue}.runtimeconfig.json");
            string old_deps_file = Path.Combine(DS4Windows.Global.exedirpath, $"{oldvalue}.deps.json");

            if (!string.IsNullOrEmpty(oldvalue))
            {
                if (File.Exists(old_exefile))
                {
                    File.Delete(old_exefile);
                }

                if (File.Exists(old_conf_file))
                {
                    File.Delete(old_conf_file);
                }

                if (File.Exists(old_deps_file))
                {
                    File.Delete(old_deps_file);
                }
            }
        }

        private void SettingsViewModel_FakeExeNameChanged(object sender, EventArgs e)
        {
            string temp = FakeExeName;
            if (!string.IsNullOrEmpty(temp))
            {
                CreateFakeExe(FakeExeName);
            }
        }

        private void SettingsViewModel_RunStartTaskChanged(object sender, EventArgs e)
        {
            if (runStartTask)
            {
                StartupMethods.WriteTaskEntry();
            }
            else
            {
                StartupMethods.DeleteTaskEntry();
            }
        }

        private void SettingsViewModel_RunStartProgChanged(object sender, EventArgs e)
        {
            if (runStartProg)
            {
                StartupMethods.WriteStartProgEntry();
            }
            else
            {
                StartupMethods.DeleteStartProgEntry();
            }
        }

        private void SettingsViewModel_RunAtStartupChanged(object sender, EventArgs e)
        {
            if (runAtStartup)
            {
                RunStartProg = true;
                RunStartTask = false;
            }
            else
            {
                StartupMethods.DeleteStartProgEntry();
                StartupMethods.DeleteTaskEntry();
            }
        }

        private void SettingsViewModel_CheckForUpdatesChanged(object sender, EventArgs e)
        {
            if (!CheckForUpdates)
            {
                CheckEveryChanged?.Invoke(this, EventArgs.Empty);
                CheckEveryUnitChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void CheckStartupOptions()
        {
            bool lnkExists = File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
            if (lnkExists)
            {
                runAtStartup = true;
            }
            else
            {
                runAtStartup = false;
            }
        }

        private void CheckForNoUpdatesWhen()
        {
            if (DS4Windows.Global.CheckWhen == 0)
            {
                checkEveryUnitIdx = 1;
            }

            CheckForUpdatesChanged?.Invoke(this, EventArgs.Empty);
            CheckEveryChanged?.Invoke(this, EventArgs.Empty);
            CheckEveryUnitChanged?.Invoke(this, EventArgs.Empty);
        }

        public void CreateFakeExe(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                DS4Windows.AppLogger.LogToGui("Cannot create fake exe: Invalid filename provided", true);
                return;
            }

            // Validate filename for invalid characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (filename.IndexOfAny(invalidChars) >= 0)
            {
                DS4Windows.AppLogger.LogToGui("Cannot create fake exe: Filename contains invalid characters", true);
                return;
            }

            try
            {
                string exefile = Path.Combine(DS4Windows.Global.exedirpath, $"{filename}.exe");
                string current_conf_file_path = $"{DS4Windows.Global.exelocation}.runtimeconfig.json";
                string current_deps_file_path = $"{DS4Windows.Global.exelocation}.deps.json";

                string fake_conf_file = Path.Combine(DS4Windows.Global.exedirpath, $"{filename}.runtimeconfig.json");
                string fake_deps_file = Path.Combine(DS4Windows.Global.exedirpath, $"{filename}.deps.json");

                // Verify source files exist
                if (!File.Exists(DS4Windows.Global.exelocation))
                {
                    DS4Windows.AppLogger.LogToGui("Cannot create fake exe: Source executable not found", true);
                    return;
                }

                if (!File.Exists(current_conf_file_path))
                {
                    DS4Windows.AppLogger.LogToGui("Cannot create fake exe: Source runtime config not found", true);
                    return;
                }

                if (!File.Exists(current_deps_file_path))
                {
                    DS4Windows.AppLogger.LogToGui("Cannot create fake exe: Source deps file not found", true);
                    return;
                }

                // Copy executable
                File.Copy(DS4Windows.Global.exelocation, exefile, true);

                // Copy needed app config and deps files
                File.Copy(current_conf_file_path, fake_conf_file, true);
                File.Copy(current_deps_file_path, fake_deps_file, true);

                DS4Windows.AppLogger.LogToGui($"Fake executable created successfully: {filename}.exe", false);
            }
            catch (UnauthorizedAccessException ex)
            {
                DS4Windows.AppLogger.LogToGui($"Access denied creating fake exe: {ex.Message}", true);
            }
            catch (IOException ex)
            {
                DS4Windows.AppLogger.LogToGui($"IO error creating fake exe: {ex.Message}", true);
            }
            catch (Exception ex)
            {
                DS4Windows.AppLogger.LogToGui($"Unexpected error creating fake exe: {ex.Message}", true);
            }
        }

        public void DriverCheckRefresh()
        {
            HidHideClientFoundChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RefreshMonitorChoices()
        {
            absMonitorChoices.Clear();
            absMonitorChoices.Add(new MonitorChoiceListing()
            {
                DisplayName = "All Monitors",
                EDID = string.Empty,
                Index = 0,
            });

            int idx = 1;
            foreach(DISPLAY_DEVICE tempDis in Global.GrabCurrentMonitors())
            {
                absMonitorChoices.Add(new MonitorChoiceListing()
                {
                    DisplayName = tempDis.DeviceString,
                    EDID = tempDis.DeviceID,
                    Index = idx,
                });

                idx++;
            }

            AbsMonitorChoicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public struct MonitorChoiceListing
    {
        private int idx;
        public int Index
        {
            get => idx;
            set => idx = value;
        }

        private string edid;
        public string EDID
        {
            get => edid;
            set => edid = value;
        }

        private string displayName;
        public string DisplayName
        {
            get => displayName;
            set => displayName = value;
        }

        public string DisplayItemString
        {
            get => $"{idx}: {displayName}";
        }
    }
}
