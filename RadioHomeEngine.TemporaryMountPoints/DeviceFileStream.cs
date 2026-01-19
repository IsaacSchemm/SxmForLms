namespace RadioHomeEngine.TemporaryMountPoints
{
    public sealed class DeviceFileStream : Stream, IAsyncDisposable
    {
        private readonly FileStream _fileStream;
        private readonly TemporaryMountPoint _temporaryMountPoint;

        private DeviceFileStream
            (FileStream fileStream,
            TemporaryMountPoint temporaryMountPoint)
        {
            _fileStream = fileStream
                ?? throw new ArgumentNullException(nameof(fileStream));
            _temporaryMountPoint = temporaryMountPoint
                ?? throw new ArgumentNullException(nameof(temporaryMountPoint));
        }

        public override bool CanRead => _fileStream.CanRead;

        public override bool CanSeek => _fileStream.CanSeek;

        public override bool CanWrite => _fileStream.CanWrite;

        public override long Length => _fileStream.Length;

        public override long Position { get => _fileStream.Position; set => _fileStream.Position = value; }

        public static async Task<DeviceFileStream> CreateAsync(string device, string filePath)
        {
            var temporaryMountPoint = await TemporaryMountPoint.CreateAsync(device);

            var fileStream = new FileStream(
                Path.Combine(
                    temporaryMountPoint.MountPath,
                    filePath),
                FileMode.Open,
                FileAccess.Read);

            return new(fileStream, temporaryMountPoint);
        }

        public override void Flush()
        {
            _fileStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _fileStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _fileStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _fileStream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _fileStream.Write(buffer, offset, count);
        }

        public override void Close()
        {
            base.Close();
            _temporaryMountPoint.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            _fileStream.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _fileStream.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
