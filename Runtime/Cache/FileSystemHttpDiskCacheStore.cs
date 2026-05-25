using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace ApiClient.Runtime.Cache
{
    /// <summary>
    /// Disk-backed <see cref="IHttpDiskCacheStore"/>. Per-key serialisation via
    /// <see cref="SemaphoreSlim"/> coalesces concurrent reads/writes on the same
    /// entry. Atomic publish via .tmp + <see cref="File.Move(string,string)"/>:
    /// readers check meta last, so a half-written entry is invisible.
    ///
    /// Eviction: LRU under <c>maxBytes</c>. Triggers from inside
    /// <see cref="WriteAsync"/> on overrun, drains to 90% of the cap. Index
    /// is rebuilt from .meta files on construction.
    /// </summary>
    public sealed class FileSystemHttpDiskCacheStore : IHttpDiskCacheStore
    {
        private readonly string _root;
        private readonly long _maxBytes;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();
        private readonly ConcurrentDictionary<string, IndexEntry> _index = new();
        private readonly SemaphoreSlim _evictionGate = new(1, 1);
        private long _approxSizeBytes;
        private bool _disposed;

        private sealed class IndexEntry
        {
            public long Size;
            public long LastAccessUnix;
        }

        public long ApproxSizeBytes => Interlocked.Read(ref _approxSizeBytes);

        public FileSystemHttpDiskCacheStore(string rootDirectory, long maxBytes = 100L * 1024 * 1024)
        {
            if (string.IsNullOrEmpty(rootDirectory)) throw new ArgumentNullException(nameof(rootDirectory));
            _root = rootDirectory;
            _maxBytes = Math.Max(1L * 1024 * 1024, maxBytes);

            try
            {
                Directory.CreateDirectory(_root);
                SweepOrphans();
                RebuildIndex();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(FileSystemHttpDiskCacheStore)} init failed: {ex.Message}");
            }
        }

        public async Task<DiskCacheEntry> TryReadMetaAsync(string key, CancellationToken ct)
        {
            if (_disposed || string.IsNullOrEmpty(key)) return null;
            var sem = _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var metaPath = MetaPath(key);
                if (!File.Exists(metaPath)) return null;
                var bodyPath = BodyPath(key);
                if (!File.Exists(bodyPath)) return null;

                using var fs = new FileStream(metaPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                var entry = JsonConvert.DeserializeObject<DiskCacheEntry>(json);
                if (entry != null)
                {
                    if (_index.TryGetValue(key, out var idx))
                    {
                        Interlocked.Exchange(ref idx.LastAccessUnix, NowUnix());
                    }
                }
                return entry;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(FileSystemHttpDiskCacheStore)}.TryReadMetaAsync({key}) failed: {ex.Message}");
                return null;
            }
            finally
            {
                sem.Release();
            }
        }

        public async Task<Stream> OpenBodyAsync(string key, CancellationToken ct)
        {
            if (_disposed || string.IsNullOrEmpty(key)) return null;
            try
            {
                var bodyPath = BodyPath(key);
                if (!File.Exists(bodyPath)) return null;
                var fs = new FileStream(bodyPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                var ms = new MemoryStream((int)Math.Min(fs.Length, int.MaxValue));
                await fs.CopyToAsync(ms, 8192, ct).ConfigureAwait(false);
                fs.Dispose();
                ms.Position = 0;
                return ms;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(FileSystemHttpDiskCacheStore)}.OpenBodyAsync({key}) failed: {ex.Message}");
                return null;
            }
        }

        public async Task WriteAsync(string key, DiskCacheEntry entry, byte[] body, CancellationToken ct)
        {
            if (_disposed || string.IsNullOrEmpty(key) || entry == null || body == null) return;
            var sem = _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                entry.BodyLength = body.LongLength;
                entry.StoredAt = DateTimeOffset.UtcNow;

                var shardDir = Path.Combine(_root, DiskCacheKey.Shard(key));
                Directory.CreateDirectory(shardDir);

                var bodyPath = BodyPath(key);
                var metaPath = MetaPath(key);
                var bodyTmp = bodyPath + ".tmp";
                var metaTmp = metaPath + ".tmp";

                using (var fs = new FileStream(bodyTmp, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
                {
                    await fs.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
                    await fs.FlushAsync(ct).ConfigureAwait(false);
                }

                var metaJson = JsonConvert.SerializeObject(entry);
                using (var fs = new FileStream(metaTmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                using (var writer = new StreamWriter(fs, Encoding.UTF8))
                {
                    await writer.WriteAsync(metaJson).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                // Body first so a half-rename leaves orphan body (swept on next init) without
                // a meta pointing at it.
                if (File.Exists(bodyPath)) File.Delete(bodyPath);
                File.Move(bodyTmp, bodyPath);

                if (File.Exists(metaPath)) File.Delete(metaPath);
                File.Move(metaTmp, metaPath);

                // Track size for eviction
                if (_index.TryGetValue(key, out var existing))
                {
                    Interlocked.Add(ref _approxSizeBytes, body.LongLength - existing.Size);
                    existing.Size = body.LongLength;
                    Interlocked.Exchange(ref existing.LastAccessUnix, NowUnix());
                }
                else
                {
                    _index[key] = new IndexEntry { Size = body.LongLength, LastAccessUnix = NowUnix() };
                    Interlocked.Add(ref _approxSizeBytes, body.LongLength);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(FileSystemHttpDiskCacheStore)}.WriteAsync({key}) failed: {ex.Message}");
            }
            finally
            {
                sem.Release();
            }

            if (Interlocked.Read(ref _approxSizeBytes) > _maxBytes)
            {
                _ = EvictAsync();
            }
        }

        public async Task InvalidateAsync(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            var sem = _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync().ConfigureAwait(false);
            try
            {
                DeleteEntryFiles(key);
                if (_index.TryRemove(key, out var idx))
                {
                    Interlocked.Add(ref _approxSizeBytes, -idx.Size);
                }
            }
            finally
            {
                sem.Release();
            }
        }

        public Task ClearAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(_root))
                    {
                        Directory.Delete(_root, recursive: true);
                        Directory.CreateDirectory(_root);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{nameof(FileSystemHttpDiskCacheStore)}.ClearAsync failed: {ex.Message}");
                }
                _index.Clear();
                Interlocked.Exchange(ref _approxSizeBytes, 0);
            });
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var kv in _keyLocks)
            {
                kv.Value.Dispose();
            }
            _keyLocks.Clear();
            _evictionGate.Dispose();
        }

        private async Task EvictAsync()
        {
            if (!await _evictionGate.WaitAsync(0).ConfigureAwait(false)) return;
            try
            {
                var target = (long)(_maxBytes * 0.9);
                if (Interlocked.Read(ref _approxSizeBytes) <= target) return;

                var snapshot = new List<KeyValuePair<string, IndexEntry>>(_index.Count);
                foreach (var kv in _index) snapshot.Add(kv);
                snapshot.Sort((a, b) => a.Value.LastAccessUnix.CompareTo(b.Value.LastAccessUnix));

                for (int i = 0; i < snapshot.Count && Interlocked.Read(ref _approxSizeBytes) > target; i++)
                {
                    var key = snapshot[i].Key;
                    await InvalidateAsync(key).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(FileSystemHttpDiskCacheStore)}.EvictAsync failed: {ex.Message}");
            }
            finally
            {
                _evictionGate.Release();
            }
        }

        private void DeleteEntryFiles(string key)
        {
            try { File.Delete(BodyPath(key)); } catch { }
            try { File.Delete(MetaPath(key)); } catch { }
            try { File.Delete(BodyPath(key) + ".tmp"); } catch { }
            try { File.Delete(MetaPath(key) + ".tmp"); } catch { }
        }

        private void SweepOrphans()
        {
            if (!Directory.Exists(_root)) return;
            try
            {
                foreach (var tmp in Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories))
                {
                    try { File.Delete(tmp); } catch { }
                }
            }
            catch { }
        }

        private void RebuildIndex()
        {
            _index.Clear();
            Interlocked.Exchange(ref _approxSizeBytes, 0);
            if (!Directory.Exists(_root)) return;

            try
            {
                foreach (var bodyPath in Directory.EnumerateFiles(_root, "*.body", SearchOption.AllDirectories))
                {
                    var key = Path.GetFileNameWithoutExtension(bodyPath);
                    var metaPath = Path.Combine(Path.GetDirectoryName(bodyPath) ?? "", key + ".meta.json");
                    if (!File.Exists(metaPath))
                    {
                        try { File.Delete(bodyPath); } catch { }
                        continue;
                    }
                    var size = new FileInfo(bodyPath).Length;
                    _index[key] = new IndexEntry { Size = size, LastAccessUnix = NowUnix() };
                    Interlocked.Add(ref _approxSizeBytes, size);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(FileSystemHttpDiskCacheStore)}.RebuildIndex failed: {ex.Message}");
            }
        }

        private string BodyPath(string key) =>
            Path.Combine(_root, DiskCacheKey.Shard(key), key + ".body");

        private string MetaPath(string key) =>
            Path.Combine(_root, DiskCacheKey.Shard(key), key + ".meta.json");

        private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
