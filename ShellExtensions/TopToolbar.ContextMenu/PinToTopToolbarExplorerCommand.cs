using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TopToolbar.ContextMenu
{
    [ComVisible(true)]
    [Guid(ClassId)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class PinToTopToolbarExplorerCommand : IExplorerCommand
    {
        internal const string ClassId = "3B8F00F1-1335-4D86-9EAF-C75857D53599";
        private static readonly Guid CanonicalCommandGuid = new("B4A1BEBA-E6E2-445A-B5F2-335A8F75A5D9");
        private const string CommandTitle = "Pin to TopToolbar";

        public int GetTitle(IShellItemArray psiItemArray, out IntPtr ppszName)
        {
            ppszName = Marshal.StringToCoTaskMemUni(CommandTitle);
            return HResult.S_OK;
        }

        public int GetIcon(IShellItemArray psiItemArray, out IntPtr ppszIcon)
        {
            ppszIcon = IntPtr.Zero;

            if (!TryGetTopToolbarExecutablePath(out var executablePath))
            {
                return HResult.E_NOTIMPL;
            }

            ppszIcon = Marshal.StringToCoTaskMemUni($"{executablePath},0");
            return HResult.S_OK;
        }

        public int GetToolTip(IShellItemArray psiItemArray, out IntPtr ppszInfotip)
        {
            ppszInfotip = IntPtr.Zero;
            return HResult.E_NOTIMPL;
        }

        public int GetCanonicalName(out Guid pguidCommandName)
        {
            pguidCommandName = CanonicalCommandGuid;
            return HResult.S_OK;
        }

        public int GetState(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out EXPCMDSTATE pCmdState)
        {
            pCmdState = EXPCMDSTATE.ECS_DISABLED;

            if (TryGetAnySupportedPath(psiItemArray))
            {
                pCmdState = EXPCMDSTATE.ECS_ENABLED;
            }

            return HResult.S_OK;
        }

        public int Invoke(IShellItemArray psiItemArray, IntPtr pbc)
        {
            var launchedAny = false;

            foreach (var path in EnumerateSupportedPaths(psiItemArray))
            {
                if (TryLaunchPinCommand(path))
                {
                    launchedAny = true;
                }
            }

            return launchedAny ? HResult.S_OK : HResult.E_FAIL;
        }

        public int GetFlags(out EXPCMDFLAGS pFlags)
        {
            pFlags = EXPCMDFLAGS.ECF_DEFAULT;
            return HResult.S_OK;
        }

        public int EnumSubCommands(out IEnumExplorerCommand ppEnum)
        {
            ppEnum = null;
            return HResult.E_NOTIMPL;
        }

        private static bool TryGetAnySupportedPath(IShellItemArray shellItemArray)
        {
            foreach (var _ in EnumerateSupportedPaths(shellItemArray))
            {
                return true;
            }

            return false;
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateSupportedPaths(IShellItemArray shellItemArray)
        {
            if (shellItemArray == null)
            {
                yield break;
            }

            if (shellItemArray.GetCount(out var count) != HResult.S_OK || count == 0)
            {
                yield break;
            }

            for (uint i = 0; i < count; i++)
            {
                if (shellItemArray.GetItemAt(i, out var shellItem) != HResult.S_OK || shellItem == null)
                {
                    continue;
                }

                try
                {
                    if (shellItem.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var pathPtr) != HResult.S_OK || pathPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    var path = Marshal.PtrToStringUni(pathPtr);
                    Marshal.FreeCoTaskMem(pathPtr);

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    var normalized = NormalizePath(path);
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        continue;
                    }

                    if (File.Exists(normalized) || Directory.Exists(normalized))
                    {
                        yield return normalized;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(shellItem);
                }
            }
        }

        private static bool TryLaunchPinCommand(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!TryGetTopToolbarExecutablePath(out var executablePath))
            {
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = $"--pin \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                };

                _ = Process.Start(startInfo);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetTopToolbarExecutablePath(out string executablePath)
        {
            executablePath = string.Empty;

            try
            {
                var assemblyDirectory = Path.GetDirectoryName(typeof(PinToTopToolbarExplorerCommand).Assembly.Location);
                if (string.IsNullOrWhiteSpace(assemblyDirectory))
                {
                    return false;
                }

                var candidate = Path.Combine(assemblyDirectory, "TopToolbar.exe");
                if (!File.Exists(candidate))
                {
                    return false;
                }

                executablePath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizePath(string rawPath)
        {
            try
            {
                return Path.GetFullPath(rawPath.Trim().Trim('"'));
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    internal static class HResult
    {
        internal const int S_OK = 0;
        internal const int E_FAIL = unchecked((int)0x80004005);
        internal const int E_NOTIMPL = unchecked((int)0x80004001);
    }

    [Flags]
    public enum EXPCMDFLAGS : uint
    {
        ECF_DEFAULT = 0x000,
    }

    [Flags]
    public enum EXPCMDSTATE : uint
    {
        ECS_ENABLED = 0x0,
        ECS_DISABLED = 0x1,
    }

    public enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000,
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9")]
    public interface IExplorerCommand
    {
        [PreserveSig]
        int GetTitle([In] IShellItemArray psiItemArray, out IntPtr ppszName);

        [PreserveSig]
        int GetIcon([In] IShellItemArray psiItemArray, out IntPtr ppszIcon);

        [PreserveSig]
        int GetToolTip([In] IShellItemArray psiItemArray, out IntPtr ppszInfotip);

        [PreserveSig]
        int GetCanonicalName(out Guid pguidCommandName);

        [PreserveSig]
        int GetState([In] IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out EXPCMDSTATE pCmdState);

        [PreserveSig]
        int Invoke([In] IShellItemArray psiItemArray, IntPtr pbc);

        [PreserveSig]
        int GetFlags(out EXPCMDFLAGS pFlags);

        [PreserveSig]
        int EnumSubCommands(out IEnumExplorerCommand ppEnum);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("D9745868-CA5F-4A76-91CD-F5A129FBB076")]
    public interface IEnumExplorerCommand
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
    public interface IShellItemArray
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppvOut);

        [PreserveSig]
        int GetPropertyStore(int flags, [In] ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetPropertyDescriptionList(ref PROPERTYKEY keyType, [In] ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetAttributes(SIATTRIBFLAGS dwAttribFlags, uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int GetCount(out uint pdwNumItems);

        [PreserveSig]
        int GetItemAt(uint dwIndex, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    public interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare([In] IShellItem psi, uint hint, out int piOrder);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [Flags]
    public enum SIATTRIBFLAGS
    {
        AND = 0x1,
        OR = 0x2,
        APPCOMPAT = 0x3,
        MASK = 0x3,
    }
}
