using MakeMkv;

namespace TheDiscDb.Tools.MakeMkv
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Fantastic.FileSystem;
    using Microsoft.Extensions.Options;

    public class MakeMkvHelper
    {
        private readonly IOptions<MakeMkvOptions> options;
        private readonly IFileSystem fileSystem;

        public MakeMkvHelper(IOptions<MakeMkvOptions> options, IFileSystem fileSystem)
        {
            this.options = options ?? throw new System.ArgumentNullException(nameof(options));
            this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

            if (options.Value.Path == null)
            {
                throw new Exception("The MakeMKV Path is not configured");
            }
        }

        public IList<Drive> Drives => this.options.Value.Drives;

        public async Task WriteLogs(int driveIndex, string path, bool cleanLogs = true)
        {
            var info = new ProcessStartInfo
            {
                FileName = this.options.Value.Path,
                Arguments = GetInfoArguments(driveIndex, path)
            };

            await RunProcessAsync(info);

            if (cleanLogs)
            {
                await Task.Delay(200); // wait for file handle to be released
                try
                {
                    await CleanLogs(driveIndex, path);
                }
                catch (IOException e)
                {
                    throw new CleanLogFileException(path, e);
                }
            }
        }

        // --minlength=0 keeps short titles (menus, stubs, extras) that MakeMKV's default minimum length would hide,
        // matching the contributor upload command so logs are complete, comparable references.
        public static string GetInfoArguments(int driveIndex, string path)
            => $"--minlength=0 --robot --messages=\"{path}\" info disc:{driveIndex}";

        public async Task CleanLogs(int driveIndex, string path, CancellationToken cancellationToken = default)
        {
            var lines = await this.fileSystem.File.ReadAllLines(
                path,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
                cancellationToken);

            var output = LogParser.CleanLogs(lines, out _);
            await this.fileSystem.File.WriteAllLines(path, output, Encoding.UTF8, cancellationToken);
        }

        private static Task<int> RunProcessAsync(ProcessStartInfo info)
        {
            var tcs = new TaskCompletionSource<int>();

            var process = new Process
            {
                StartInfo = info,
                EnableRaisingEvents = true
            };

            process.Exited += (sender, args) =>
            {
                tcs.SetResult(process.ExitCode);
                process.Dispose();
            };

            process.Start();

            return tcs.Task;
        }
    }

    public class CleanLogFileException : ApplicationException
    {
        public CleanLogFileException(string path, Exception? innerException)
            : base($"Unable to clean log for '{path}'", innerException)
        {
        }
    }
}
