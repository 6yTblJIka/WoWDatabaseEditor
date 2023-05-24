﻿using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WDE.Module.Attributes;

namespace WDE.Common.Managers
{
    [UniqueProvider]
    public interface IWindowManager
    {
        IAbstractWindowView ShowWindow(IWindowViewModel viewModel, out Task task);
        
        Task<bool> ShowDialog(IDialog viewModel);

        IAbstractWindowView ShowStandaloneDocument(IDocument document, out Task task);
        
        Task<string?> ShowFolderPickerDialog(string defaultDirectory);
        
        Task<string?> ShowOpenFileDialog(string filter, string? defaultDirectory = null);
        
        Task<string?> ShowSaveFileDialog(string filter, string? defaultDirectory = null, string? initialFileName = null);

        void OpenUrl(string url, string arguments = "");

        void Activate();
    }

    public interface IAbstractWindowView
    {
        void Activate();
    }

    public static class WindowManagerExtensions
    {
        /// <summary>
        /// opens Windows Explorer or mac Finder with selected file
        /// </summary>
        /// <param name="path">file to select</param>
        public static void RevealFile(this IWindowManager windowManager, string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                windowManager.OpenUrl("explorer.exe",  "/select, \"" + path.Replace("/","\\").Replace("\\\\","\\") +"\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                windowManager.OpenUrl("open", "-R \"" + path + "\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                windowManager.OpenUrl("xdg-open", "\"" + Path.GetDirectoryName(path) + "\"");
            }
            else
                throw new Exception("Unknown OS");
        }
    }
}