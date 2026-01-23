using System.Diagnostics;

namespace RadioHomeEngine.TemporaryMountPoints
{
    public sealed class EphemeralMountPoint : IMountPoint, IAsyncDisposable
    {
        private bool _disposed;

        public string Device { get; }
        public string MountPath { get; }

        private EphemeralMountPoint(string device, string mountPath)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
            MountPath = mountPath ?? throw new ArgumentNullException(nameof(mountPath));
        }

        public static async Task<EphemeralMountPoint> CreateAsync(string device)
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

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            Console.WriteLine($"[{Device}] unmounting from {MountPath}");

            using var umountProc = Process.Start("umount", $"{MountPath}");
            await umountProc.WaitForExitAsync();

            Directory.Delete(MountPath, recursive: false);
        }
    }
}
