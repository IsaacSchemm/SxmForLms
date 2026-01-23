namespace RadioHomeEngine.TemporaryMountPoints
{
    public interface IMountPoint
    {
        string Device { get; }
        string MountPath { get; }
    }
}
