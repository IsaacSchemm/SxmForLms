namespace RadioHomeEngine.TemporaryMountPoints
{
    public interface IMountPoint : IDisposable
    {
        string Device { get; }
        string MountPath { get; }
    }
}
