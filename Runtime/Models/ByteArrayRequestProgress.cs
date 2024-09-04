namespace ApiClient.Runtime
{
    public readonly struct ByteArrayRequestProgress
    {
        public long TotalBytesRead { get; }
        public long ContentSize { get; }

        public ByteArrayRequestProgress(long totalBytesRead, long contentSize)
        {
            TotalBytesRead = totalBytesRead;
            ContentSize = contentSize;
        }
    }
}