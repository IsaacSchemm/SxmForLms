namespace RadioHomeEngine.TemporaryMountPoints
{
    public sealed class EstablishedMountPoint : IMountPoint
    {
        private static readonly List<TemporaryMountPoint> _temporaryMountPoints = [];

        public static async Task<EstablishedMountPoint> GetOrCreateAsync(string device)
        {
            foreach (var existing in _temporaryMountPoints)
                if (existing.Device == device)
                    return new(existing);

            var mountPoint = await TemporaryMountPoint.CreateAsync(device);
            _temporaryMountPoints.Add(mountPoint);
            return new(mountPoint);
        }

        public static void UnmountDevice(string device)
        {
            foreach (var existing in _temporaryMountPoints.ToList())
            {
                if (existing.Device == device)
                {
                    _temporaryMountPoints.Remove(existing);
                    existing.Dispose();
                }
            }
        }

        public static void UnmountAll()
        {
            foreach (var existing in _temporaryMountPoints.ToList())
            {
                _temporaryMountPoints.Remove(existing);
                existing.Dispose();
            }
        }

        private readonly TemporaryMountPoint _underlyingMountPoint;

        private EstablishedMountPoint(
            TemporaryMountPoint underlyingMountPoint)
        {
            _underlyingMountPoint = underlyingMountPoint;
        }

        public string Device => _underlyingMountPoint.Device;
        public string MountPath => _underlyingMountPoint.MountPath;

        public void Dispose() { }
    }
}
