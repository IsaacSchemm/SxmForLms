using System.Diagnostics;

namespace RadioHomeEngine.TemporaryMountPoints
{
    public sealed class TemporaryMountPoint : IDisposable
    {
        public readonly string Device;
        public readonly string MountPath;

        private bool _disposed;

        private TemporaryMountPoint(string device, string mountPath)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
            MountPath = mountPath ?? throw new ArgumentNullException(nameof(mountPath));
        }

        public static async Task<TemporaryMountPoint> CreateAsync(string device)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid()}");

            Console.Write($"[{device}] mounting to {directory}");

            Directory.CreateDirectory(directory);

            using var mountProc = Process.Start("mount", $"-o ro {device} {directory}");
            await mountProc.WaitForExitAsync();

            return new(device, directory);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            Console.WriteLine($"[{Device}] unmounting from {MountPath}");

            using var umountProc = Process.Start("umount", $"{MountPath}");
            umountProc.WaitForExit();

            Directory.Delete(MountPath, recursive: false);
        }
    }
}
