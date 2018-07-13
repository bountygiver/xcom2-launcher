﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using JR.Utils.GUI.Forms;
using XCOM2Launcher.Classes.Steam;
using XCOM2Launcher.Forms;
using XCOM2Launcher.Mod;
using XCOM2Launcher.XCOM;

namespace XCOM2Launcher
{
    internal static class Program
    {
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
        private static void Main()
        {
#if !DEBUG
            try
            {
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            try
            {
                if (!CheckDotNet4_6() && MessageBox.Show(@"This program requires .NET v4.6 or newer.\r\nDo you want to install it now?", @"Error", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    Process.Start(@"https://www.microsoft.com/en-us/download/details.aspx?id=56115");
            }
            catch (Exception e)
            {
                if (MessageBox.Show(@"This program requires .NET v4.6 or newer.\r\nDo you want to install it now?", @"Error", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    Process.Start(@"https://www.microsoft.com/en-us/download/details.aspx?id=56115");
            }
            
            if (!SteamAPIWrapper.Init())
            {
                MessageBox.Show("Please start steam first!");
                return;
            }
            // SteamWorkshop.StartCallbackService();

            // Load settings
            var settings = InitializeSettings();
            if (settings == null)
                return;

#if !DEBUG
    // Check for update
            if (settings.CheckForUpdates)
            {
                try
                {
                    using (var client = new System.Net.WebClient())
                    {
                        client.Headers.Add("User-Agent: Other");
                        var regex = new Regex("[^0-9.]");
                        var json = client.DownloadString("https://api.github.com/repos/X2CommunityCore/xcom2-launcher/releases/latest");
                        var release = Newtonsoft.Json.JsonConvert.DeserializeObject<GitHub.Release>(json);
                        var currentVersion = new Version(regex.Replace(GetCurrentVersion(), ""));
                        var newVersion = new Version(regex.Replace(release.tag_name, ""));

                        if (currentVersion.CompareTo(newVersion) < 0)
                            // New version available
                            new UpdateAvailableDialog(release, currentVersion.ToString()).ShowDialog();
                    }
                }
                catch (System.Net.WebException)
                {
                    // No internet?
                }
            }
#endif

            // clean up old files
            if (File.Exists(XCOM2.DefaultConfigDir + @"\DefaultModOptions.ini.bak"))
            {
                // Restore backup
                File.Copy(XCOM2.DefaultConfigDir + @"\DefaultModOptions.ini.bak", XCOM2.DefaultConfigDir + @"\DefaultModOptions.ini", true);
                File.Delete(XCOM2.DefaultConfigDir + @"\DefaultModOptions.ini.bak");
            }

            Application.Run(new MainForm(settings));

            SteamAPIWrapper.Shutdown();
#if !DEBUG
            }
            catch (Exception e)
            {
                MessageBox.Show("An exception occured. See error.log for additional details.");
                File.WriteAllText("error.log", e.Message + "\r\nStack:\r\n" + e.StackTrace);
            }
#endif
        }

        /// <summary>
        /// Check whether .net runtime v4.6 is installed
        /// </summary>
        /// <returns>bool</returns>
        private static bool CheckDotNet4_6()
        {
            try
            {
                DateTimeOffset.FromUnixTimeSeconds(101010);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static Settings InitializeSettings()
        {
            var firstRun = !File.Exists("settings.json");

	        var settings = firstRun ? new Settings() : Settings.Instance;

	        if (settings.ShowUpgradeWarning && !firstRun)
	        {
		        MessageBoxManager.Cancel = "Exit";
		        MessageBoxManager.OK = "Continue";
				MessageBoxManager.Register();
				var choice = MessageBox.Show(
					"WARNING!!\n\nThis launcher is NOT COMPATIBLE with the old 'settings.json' file.\nStop NOW and launch the old version to export a profile of your mods WITH GROUPS!\nOnce that is done, move the old 'settings.json' file to a SAFE PLACE and then proceed.\nAfter loading, import the profile you saved to recover groups.\n\nIf you are not ready to do this, click 'Exit' to leave with no changes.",
					"WARNING!", MessageBoxButtons.OKCancel, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2);
				if (choice == DialogResult.Cancel) Environment.Exit(0);
				MessageBoxManager.Unregister();
			}
			settings.ShowUpgradeWarning = false;


			// Verify Game Path
			if (!Directory.Exists(settings.GamePath))
                settings.GamePath = XCOM2.DetectGameDir();

            if (settings.GamePath == "")
                MessageBox.Show(@"Could not find XCOM 2 installation path. Please fill it manually in the settings.");

            // Verify Mod Paths
            var pathsToEdit = settings.ModPaths.Where(m => !m.EndsWith("\\")).ToList();
            foreach (var modPath in pathsToEdit)
            {
                settings.ModPaths.Add(modPath + "\\");
                settings.ModPaths.Remove(modPath);
            }

            var oldPaths = settings.ModPaths.Where(modPath => !Directory.Exists(modPath)).ToList();
            foreach (var modPath in oldPaths)
                settings.ModPaths.Remove(modPath);

            foreach (var modPath in XCOM2.DetectModDirs())
            {
                if (!settings.ModPaths.Contains(modPath))
                {
                    if (!settings.ModPaths.Contains(modPath + "\\"))
                    {
                        settings.ModPaths.Add(modPath);
                    }
                }

            }


            if (settings.ModPaths.Count == 0)
                MessageBox.Show(@"Could not find XCOM 2 mod directories. Please fill them in manually in the settings.");

            if (settings.Mods.Entries.Count > 0)
            {
                // Verify categories
                var index = settings.Mods.Entries.Values.Max(c => c.Index);
                foreach (var cat in settings.Mods.Entries.Values.Where(c => c.Index == -1))
                    cat.Index = ++index;

                // Verify Mods 
	            foreach (var mod in settings.Mods.All)
	            {
		            if (!settings.ModPaths.Any(mod.IsInModPath))
						mod.State |= ModState.NotLoaded;
					if (!Directory.Exists(mod.Path) || !File.Exists(mod.GetModInfoFile()))
						mod.State |= ModState.NotInstalled;
	                // tags clean up
	                mod.Tags = mod.Tags.Where(t => settings.Tags.ContainsKey(t)).ToList();
	            }

                var newlyBrokenMods = settings.Mods.All.Where(m => (m.State == ModState.NotLoaded || m.State == ModState.NotInstalled) && !m.isHidden).ToList();
                if (newlyBrokenMods.Count > 0)
                {
                    if (newlyBrokenMods.Count == 1)
                        FlexibleMessageBox.Show($"The mod '{newlyBrokenMods[0].Name}' no longer exists and has been hidden.");
                    else
                        FlexibleMessageBox.Show($"{newlyBrokenMods.Count} mods no longer exist and have been hidden:\r\n\r\n" + string.Join("\r\n", newlyBrokenMods.Select(m => m.Name)));

                    foreach (var m in newlyBrokenMods)
                        m.isHidden = true;
						//settings.Mods.RemoveMod(m);
				}
            }

            // import mods
            settings.ImportMods();

            return settings;
        }

        public static string GetCurrentVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fields = assembly.GetType("XCOM2Launcher.GitVersionInformation").GetFields();

            var major = fields.Single(f => f.Name == "Major").GetValue(null);
            var minor = fields.Single(f => f.Name == "Minor").GetValue(null);
            var patch = fields.Single(f => f.Name == "Patch").GetValue(null);


            return $"v{major}.{minor}.{patch}";
        }
    }
}