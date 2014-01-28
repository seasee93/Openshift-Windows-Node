﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Uhuru.Openshift.Runtime.Config;
using Uhuru.Openshift.Utilities;

namespace Uhuru.Openshift.Runtime.Utils
{
    public static class LinuxFiles
    {
        const string DummySymlinkSuffix = ".LINUXSYMLINK";

        private static string BashBinary
        {
            get
            {
                return Path.Combine(NodeConfig.Values["SSHD_BASE_DIR"], @"bin\bash.exe");
            }
        }

        private static string CygpathBinary
        {
            get
            {
                return Path.Combine(NodeConfig.Values["SSHD_BASE_DIR"], @"bin\cygpath.exe");
            }
        }

        private static string Cygpath(string directory)
        {
            return RunCommandAndGetOutput(CygpathBinary, directory).Trim();
        }

        public static void FixSymlinks(string directory)
        {
            string linuxDir = Cygpath(directory);

            // We use a .LINUXSYMLINK dummy suffix for cygpath when getting symlink names, otherwise it will convert them to the target directory
            string symlinkArguments = string.Format(@"--norc --login -c ""find -L {0} -xtype l -print0 | sort -z | xargs -0 -I {{}} cygpath --windows {{}}{1}""", linuxDir, DummySymlinkSuffix);
            string targetArguments = string.Format(@"--norc --login -c ""find -L {0} -xtype l -print0 | sort -z | xargs -0 -I {{}} cygpath --windows {{}}""", linuxDir);

            string[] symlinks = RunCommandAndGetOutput(BashBinary, symlinkArguments).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string[] targets = RunCommandAndGetOutput(BashBinary, targetArguments).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (symlinks.Length != targets.Length)
            {
                Logger.Error("Symlink count doesn't match target count in directory '{0}'. Symlinks: {1}. Targets: {2}", directory, string.Join(";", symlinks), string.Join(";", targets));
                throw new Exception("Symlink count doesn't match target count in directory.");
            }

            for (int i = 0; i < symlinks.Length; i++)
            {
                string symlink = symlinks[i].Replace(DummySymlinkSuffix, "");
                string target = targets[i];

                // Only fix links that cygwin translates (it will not translate Windows junction points, but it will see them as symlinks)
                if (symlink != target)
                {
                    Logger.Debug("Fixing symlink {0} -> {1}", symlink, target);
                    File.Delete(symlink);
                    DirectoryUtil.CreateSymbolicLink(symlink, target, DirectoryUtil.SymbolicLink.Directory);
                }
            }
        }

        public static void TakeOwnership(string directory, string windowsUser)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(directory);
            DirectorySecurity dirSecurity = dirInfo.GetAccessControl();

            dirSecurity.SetOwner(new NTAccount(windowsUser));
            dirSecurity.SetAccessRule(
                new FileSystemAccessRule(
                    windowsUser,
                    FileSystemRights.Write | FileSystemRights.Read | FileSystemRights.Delete | FileSystemRights.Modify | FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None | PropagationFlags.InheritOnly,
                    AccessControlType.Allow));

            using (new ProcessPrivileges.PrivilegeEnabler(Process.GetCurrentProcess(), ProcessPrivileges.Privilege.Restore))
            {
                dirInfo.SetAccessControl(dirSecurity);
            }
        }

        private static string RunCommandAndGetOutput(string command, string arguments)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = command;
            start.Arguments = arguments;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (Process process = Process.Start(start))
            {
                string result = process.StandardOutput.ReadToEnd();
                return result;
            }
        }
    }
}