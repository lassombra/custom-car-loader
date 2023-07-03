﻿using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace CCL.Creator
{
    public class CCBootstrapper
    {
        private const string DLL_FOLDER = "DLL_Links";
        private const string CAR_FOLDER = "_CCL_CARS";

        private const string PACKAGE_FOLDER = "CarCreator";

        private const string EXPORTER_FOLDER_DISABLED = "Bin~";
        private const string EXPORTER_FOLDER = "Bin";

        private static readonly string[] DLLNames =
        {
            "DV.Simulation",
            "DV.ThingTypes",
            "DV.Utils",
        };

        private static string LastDLLPath
        {
            get => EditorPrefs.GetString("CCL_LastDVDLLPath");
            set => EditorPrefs.SetString("CCL_LastDVDLLPath", value);
        }

        private static string GetDisabledBinPath() => Path.Combine(Application.dataPath, PACKAGE_FOLDER, EXPORTER_FOLDER_DISABLED);
        private static string GetEnabledBinPath() => Path.Combine(Application.dataPath, PACKAGE_FOLDER, EXPORTER_FOLDER);

        [MenuItem("CCL/Initialize Creator Package")]
        public static void Initialize(MenuCommand _)
        {
            if (!SetupDLLs())
            {
                EditorUtility.DisplayDialog(
                    "DV DLLs not linked",
                    "Derail Valley libraries were not linked - you will not be able to build cars until they are. " +
                    "Select CCL -> Initialize Creator Package from the menu bar to try again.",
                    "OK");
                return;
            }

            if (AssemblyCanUpdate())
            {
                EnableMainAssembly();
            }
            else if (Directory.Exists(GetDisabledBinPath()))
            {
                Directory.Delete(GetDisabledBinPath(), true);
            }

            CreateCarFolders();

            AssetDatabase.Refresh();

            EditorUtility.ClearProgressBar();
        }

        [MenuItem("CCL/Disable Creator Package (dev only)")]
        public static void DeInitialize(MenuCommand _)
        {
            DisableMainAssembly();

            AssetDatabase.Refresh();
        }

        private static bool DLLsNeedUpdated()
        {
            string localDLLFolder = Path.Combine(Application.dataPath, DLL_FOLDER);
            if (!Directory.Exists(localDLLFolder))
            {
                return true;
            }

            foreach (string dllName in DLLNames)
            {
                string destination = Path.Combine(localDLLFolder, $"{dllName}.dll");

                if (!File.Exists(destination))
                {
                    return true;
                }
            }

            foreach (string file in Directory.EnumerateFiles(localDLLFolder))
            {
                if ((Path.GetExtension(file) != ".meta") && !DLLNames.Contains(Path.GetFileNameWithoutExtension(file)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SetupDLLs()
        {
            if (!DLLsNeedUpdated()) return true;

            bool result;

            result = EditorUtility.DisplayDialog("Setup DV References",
                "You will be asked to select the folder where the Derail Valley libraries (DLL files) are located. " +
                "The tool will attempt to find your Derail Valley installation. If the tool can't find your install path, navigate to it manually. " +
                "Once you are in the installation path, select the DerailValley_Data/Managed folder.",
                "Proceed");

            if (!result) return false;

            string startingPath, folderName;
            string lastPath = LastDLLPath;
            if (!string.IsNullOrEmpty(lastPath) && Directory.Exists(lastPath))
            {
                startingPath = Path.GetDirectoryName(lastPath);
                folderName = Path.GetFileName(lastPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            else
            {
                startingPath = GetDefaultDLLFolder();
                folderName = "";
            }

            string dllPath = EditorUtility.SaveFolderPanel("Managed DLL Folder", startingPath, folderName);

            if (!string.IsNullOrWhiteSpace(dllPath) && Directory.Exists(dllPath))
            {
                LastDLLPath = dllPath;
                LinkDLLs(dllPath);
                return true;
            }

            return false;
        }

        private static void LinkDLLs(string dllPath)
        {
            string localDLLFolder = Path.Combine(Application.dataPath, DLL_FOLDER);
            Directory.CreateDirectory(localDLLFolder);

            foreach (var file in Directory.EnumerateFiles(localDLLFolder))
            {
                if ((Path.GetExtension(file) != ".meta") && !DLLNames.Contains(Path.GetFileNameWithoutExtension(file)))
                {
                    File.Delete(file);
                }
            }

            for (int i = 0; i < DLLNames.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Linking DLLs...", DLLNames[i], (float)i / DLLNames.Length);

                string dllName = $"{DLLNames[i]}.dll";
                string source = Path.Combine(dllPath, dllName);
                string destination = Path.Combine(localDLLFolder, dllName);

                if (!File.Exists(destination))
                {
                    CreateSymbolicLink(destination, source, SYMBOLIC_LINK_FLAG.File);
                }
            }
        }

        private static string GetDefaultDLLFolder()
        {
            string dllPath = "Steam/steamapps/common/Derail Valley/DerailValley_Data/Managed";

            // search for the user's DV install
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    string driveRoot = drive.RootDirectory.FullName;
                    string potentialPath = Path.Combine(driveRoot, "Program Files", dllPath);
                    if (Directory.Exists(potentialPath))
                    {
                        return potentialPath;
                    }

                    potentialPath = Path.Combine(driveRoot, "Program Files (x86)", dllPath);
                    if (Directory.Exists(potentialPath))
                    {
                        return potentialPath;
                    }
                }
                catch (Exception) { }
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        private static DateTime GetFolderLastModified(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return DateTime.MinValue;

            DateTime lastModified = DateTime.MinValue;
            foreach (string file in Directory.EnumerateFiles(dirPath))
            {
                DateTime fileModified = File.GetLastWriteTime(file);
                if (fileModified > lastModified)
                {
                    lastModified = fileModified;
                }
            }
            return lastModified;
        }

        private static bool AssemblyCanUpdate()
        {
            string hiddenName = GetDisabledBinPath();
            string enabledName = GetEnabledBinPath();

            var hiddenTime = GetFolderLastModified(hiddenName);
            var enabledTime = GetFolderLastModified(enabledName);

            return hiddenTime > enabledTime;
        }

        private static void EnableMainAssembly()
        {
            string hiddenName = GetDisabledBinPath();
            string newName = GetEnabledBinPath();

            if (Directory.Exists(hiddenName))
            {
                if (Directory.Exists(newName))
                {
                    Directory.Delete(newName, true);
                }

                Directory.Move(hiddenName, newName);
            }
        }

        private static void DisableMainAssembly()
        {
            string hiddenName = Path.Combine(Application.dataPath, PACKAGE_FOLDER, EXPORTER_FOLDER_DISABLED);
            string newName = Path.Combine(Application.dataPath, PACKAGE_FOLDER, EXPORTER_FOLDER);

            if (Directory.Exists(newName))
            {
                if (!Directory.Exists(hiddenName))
                {
                    Directory.Move(newName, hiddenName);
                }
                else
                {
                    Directory.Delete(newName);
                }
            }
        }

        private static void CreateCarFolders()
        {
            string carFolder = Path.Combine(Application.dataPath, CAR_FOLDER);
            Directory.CreateDirectory(carFolder);
        }

        [Flags]
        enum SYMBOLIC_LINK_FLAG
        {
            File = 0,
            Directory = 1,
            AllowUnprivilegedCreate = 2
        }

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.I1)]
        static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SYMBOLIC_LINK_FLAG dwFlags);
    }
}
