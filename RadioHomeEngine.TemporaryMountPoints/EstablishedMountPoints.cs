
namespace RadioHomeEngine.TemporaryMountPoints
{
    public static class EstablishedMountPoints
    {
        private sealed class MountPoint(EphemeralMountPoint underlyingMountPoint) : IMountPoint
        {
            public string Device => underlyingMountPoint.Device;
            public string MountPath => underlyingMountPoint.MountPath;

            public ValueTask UnmountAsync() => underlyingMountPoint.DisposeAsync();
        }

        private static readonly List<MountPoint> _mountPoints = [];

        public static async Task<IMountPoint> GetOrCreateAsync(string device)
        {
            foreach (var existing in _mountPoints)
                if (existing.Device == device)
                    return existing;

            var mountPoint = new MountPoint(
                await EphemeralMountPoint.CreateAsync(
                    device));
            _mountPoints.Add(mountPoint);
            return mountPoint;
        }

        public static async Task UnmountDeviceAsync(string device)
        {
            foreach (var existing in _mountPoints.ToList())
            {
                if (existing.Device == device)
                {
                    _mountPoints.Remove(existing);
                    await existing.UnmountAsync();
                }
            }
        }

        public static async Task UnmountAllAsync()
        {
            foreach (var existing in _mountPoints.ToList())
            {
                _mountPoints.Remove(existing);
                await existing.UnmountAsync();
            }
        }
    }
}
