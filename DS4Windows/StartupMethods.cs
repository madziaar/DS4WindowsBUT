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
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.TaskScheduler;
using Task = Microsoft.Win32.TaskScheduler.Task;

namespace DS4WinWPF
{
    [System.Security.SuppressUnmanagedCodeSecurity]
    public static class StartupMethods
    {
        public static string lnkpath = Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk";
        private static string taskBatPath = Path.Combine(DS4Windows.Global.exedirpath, "task.bat");

        public static bool HasStartProgEntry()
        {
            // Exception handling should not be needed here. Method handles most cases
            bool exists = File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\DS4Windows.lnk");
            return exists;
        }

        public static bool HasTaskEntry()
        {
            TaskService ts = new TaskService();
            Task tasker = ts.FindTask("RunDS4Windows");
            return tasker != null;
        }

        public static void WriteStartProgEntry()
        {
            if (string.IsNullOrEmpty(DS4Windows.Global.exelocation) || !File.Exists(DS4Windows.Global.exelocation))
            {
                DS4Windows.AppLogger.LogToGui("Cannot create startup entry: Invalid executable location", true);
                return;
            }

            try
            {
                Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); // Windows Script Host Shell Object
                dynamic shell = Activator.CreateInstance(t);
                try
                {
                    var lnk = shell.CreateShortcut(lnkpath);
                    try
                    {
                        string app = DS4Windows.Global.exelocation;
                        lnk.TargetPath = DS4Windows.Global.exelocation;
                        lnk.Arguments = "-m";
                        // Need to add the DS4Windows directory as cwd or
                        // language assemblies cannot be discovered
                        lnk.WorkingDirectory = DS4Windows.Global.exedirpath;

                        //lnk.TargetPath = Assembly.GetExecutingAssembly().Location;
                        //lnk.Arguments = "-m";
                        lnk.IconLocation = app.Replace('\\', '/');
                        lnk.Save();
                        
                        DS4Windows.AppLogger.LogToGui("Startup shortcut created successfully", false);
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject(lnk);
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                DS4Windows.AppLogger.LogToGui($"Access denied creating startup entry: {ex.Message}", true);
            }
            catch (COMException ex)
            {
                DS4Windows.AppLogger.LogToGui($"COM error creating startup entry: {ex.Message}", true);
            }
            catch (Exception ex)
            {
                DS4Windows.AppLogger.LogToGui($"Unexpected error creating startup entry: {ex.Message}", true);
            }
        }

        public static void DeleteStartProgEntry()
        {
            try
            {
                if (File.Exists(lnkpath))
                {
                    FileInfo fileInfo = new FileInfo(lnkpath);
                    if (fileInfo.IsReadOnly)
                    {
                        DS4Windows.AppLogger.LogToGui("Cannot delete startup entry: File is read-only", true);
                        return;
                    }
                    
                    File.Delete(lnkpath);
                    DS4Windows.AppLogger.LogToGui("Startup shortcut removed successfully", false);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                DS4Windows.AppLogger.LogToGui($"Access denied deleting startup entry: {ex.Message}", true);
            }
            catch (IOException ex)
            {
                DS4Windows.AppLogger.LogToGui($"IO error deleting startup entry: {ex.Message}", true);
            }
            catch (Exception ex)
            {
                DS4Windows.AppLogger.LogToGui($"Unexpected error deleting startup entry: {ex.Message}", true);
            }
        }

        public static void DeleteOldTaskEntry()
        {
            TaskService ts = new TaskService();
            Task tasker = ts.FindTask("RunDS4Windows");
            if (tasker != null)
            {
                foreach(Microsoft.Win32.TaskScheduler.Action act in tasker.Definition.Actions)
                {
                    if (act.ActionType == TaskActionType.Execute)
                    {
                        ExecAction temp = act as ExecAction;
                        if (temp.Path != taskBatPath)
                        {
                            ts.RootFolder.DeleteTask("RunDS4Windows");
                            break;
                        }
                    }
                }
            }
        }

        public static bool CanWriteStartEntry()
        {
            bool result = false;
            if (!new FileInfo(lnkpath).IsReadOnly)
            {
                result = true;
            }

            return result;
        }

        public static void WriteTaskEntry()
        {
            try
            {
                DeleteTaskEntry();

                // Create new version of task.bat file using current exe
                // filename. Allow dynamic file
                if (!RefreshTaskBat())
                {
                    DS4Windows.AppLogger.LogToGui("Failed to create task batch file", true);
                    return;
                }

                using (TaskService ts = new TaskService())
                {
                    TaskDefinition td = ts.NewTask();
                    td.Triggers.Add(new LogonTrigger());
                    string dir = DS4Windows.Global.exedirpath;
                    td.Actions.Add(new ExecAction($@"{dir}\task.bat",
                        "",
                        dir));

                    td.Principal.RunLevel = TaskRunLevel.Highest;
                    td.Settings.StopIfGoingOnBatteries = false;
                    td.Settings.DisallowStartIfOnBatteries = false;
                    ts.RootFolder.RegisterTaskDefinition("RunDS4Windows", td);
                    
                    DS4Windows.AppLogger.LogToGui("Startup task created successfully", false);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                DS4Windows.AppLogger.LogToGui($"Access denied creating startup task: {ex.Message}", true);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                DS4Windows.AppLogger.LogToGui($"Task Scheduler COM error: {ex.Message}", true);
            }
            catch (Exception ex)
            {
                DS4Windows.AppLogger.LogToGui($"Unexpected error creating startup task: {ex.Message}", true);
            }
        }

        public static void DeleteTaskEntry()
        {
            TaskService ts = new TaskService();
            Task tasker = ts.FindTask("RunDS4Windows");
            if (tasker != null)
            {
                ts.RootFolder.DeleteTask("RunDS4Windows");
            }
        }

        public static bool CheckStartupExeLocation()
        {
            string lnkprogpath = ResolveShortcut(lnkpath);
            return lnkprogpath != DS4Windows.Global.exelocation;
        }

        public static void LaunchOldTask()
        {
            TaskService ts = new TaskService();
            Task tasker = ts.FindTask("RunDS4Windows");
            if (tasker != null)
            {
                tasker.Run("");
            }
        }

        private static string ResolveShortcut(string filePath)
        {
            Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); // Windows Script Host Shell Object
            dynamic shell = Activator.CreateInstance(t);
            string result;

            try
            {
                var shortcut = shell.CreateShortcut(filePath);
                result = shortcut.TargetPath;
                Marshal.FinalReleaseComObject(shortcut);
            }
            catch (COMException)
            {
                // A COMException is thrown if the file is not a valid shortcut (.lnk) file 
                result = null;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }

            return result;
        }

        private static bool RefreshTaskBat()
        {
            try
            {
                string dir = DS4Windows.Global.exedirpath;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    DS4Windows.AppLogger.LogToGui("Cannot create task batch file: Invalid directory", true);
                    return false;
                }

                string path = $@"{dir}\task.bat";
                using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (StreamWriter w = new StreamWriter(fileStream))
                {
                    w.WriteLine("@echo off"); // Turn off echo
                    w.WriteLine("SET mypath=\"%~dp0\"");
                    string temp = $"cmd.exe /c start \"RunDS4Windows\" %mypath%\\{DS4Windows.Global.exeFileName} -m";
                    w.WriteLine(temp);
                    w.WriteLine("exit");
                }
                
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                DS4Windows.AppLogger.LogToGui($"Access denied creating task batch file: {ex.Message}", true);
                return false;
            }
            catch (IOException ex)
            {
                DS4Windows.AppLogger.LogToGui($"IO error creating task batch file: {ex.Message}", true);
                return false;
            }
            catch (Exception ex)
            {
                DS4Windows.AppLogger.LogToGui($"Unexpected error creating task batch file: {ex.Message}", true);
                return false;
            }
        }
    }
}
