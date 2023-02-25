using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace CustomPrometheusExporter.PrometheusExporters
{
    public class CustomPrometheusCollectionManager
    {
        public CustomPrometheusCollectionManager(CustomPrometheusExporter exporter)
        {
            this.exporter = exporter;
            scrapeResponseCacheDurationInMilliseconds =
                this.exporter.Options.ScrapeResponseCacheDurationMilliseconds;
            onCollectRef = OnCollect;
        }

        private readonly CustomPrometheusExporter exporter;
        private readonly Func<Batch<Metric>, ExportResult> onCollectRef;
        private readonly int scrapeResponseCacheDurationInMilliseconds;
        private byte[] buffer = new byte[85000]; // encourage the object to live in LOH (large object heap)
        private bool collectionRunning;
        private TaskCompletionSource<CollectionResponse> collectionTcs;
        private int globalLockState;
        private ArraySegment<byte> previousDataView;
        private DateTime? previousDataViewGeneratedAtUtc;
        private int readerCount;

        public Task<CollectionResponse> EnterCollect()
        {
            EnterGlobalLock();

            // If we are within {ScrapeResponseCacheDurationMilliseconds} of the
            // last successful collect, return the previous view.
            if (previousDataViewGeneratedAtUtc.HasValue
                && scrapeResponseCacheDurationInMilliseconds > 0
                && previousDataViewGeneratedAtUtc.Value.AddMilliseconds(scrapeResponseCacheDurationInMilliseconds) >=
                DateTime.UtcNow)
            {
                Interlocked.Increment(ref readerCount);
                ExitGlobalLock();

                return Task.FromResult(new CollectionResponse(previousDataView,
                    previousDataViewGeneratedAtUtc.Value, true));
            }

            // If a collection is already running, return a task to wait on the result.
            if (collectionRunning)
            {
                if (collectionTcs == null)
                    collectionTcs =
                        new TaskCompletionSource<CollectionResponse>(TaskCreationOptions
                            .RunContinuationsAsynchronously);

                Interlocked.Increment(ref readerCount);
                ExitGlobalLock();

                return collectionTcs.Task;
            }

            WaitForReadersToComplete();

            // Start a collection on the current thread.
            collectionRunning = true;
            previousDataViewGeneratedAtUtc = null;
            Interlocked.Increment(ref readerCount);
            ExitGlobalLock();

            CollectionResponse response;
            var result = ExecuteCollect();
            if (result)
            {
                previousDataViewGeneratedAtUtc = DateTime.UtcNow;
                response = new CollectionResponse(previousDataView, previousDataViewGeneratedAtUtc.Value,
                    false);
            }
            else
            {
                response = default;
            }

            EnterGlobalLock();

            collectionRunning = false;

            if (collectionTcs != null)
            {
                collectionTcs.SetResult(response);
                collectionTcs = null;
            }

            ExitGlobalLock();

            return Task.FromResult(response);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ExitCollect() => Interlocked.Decrement(ref readerCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnterGlobalLock()
        {
            SpinWait lockWait = default;
            while (true)
            {
                if (Interlocked.CompareExchange(ref globalLockState, 1, globalLockState) != 0)
                {
                    lockWait.SpinOnce();
                    continue;
                }

                break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExitGlobalLock() => globalLockState = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WaitForReadersToComplete()
        {
            SpinWait readWait = default;
            while (true)
            {
                if (Interlocked.CompareExchange(ref readerCount, 0, readerCount) != 0)
                {
                    readWait.SpinOnce();
                    continue;
                }

                break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ExecuteCollect()
        {
            exporter.OnExport = onCollectRef;
            var result = exporter.Collect(Timeout.Infinite);
            exporter.OnExport = null;
            return result;
        }

        private ExportResult OnCollect(Batch<Metric> metrics)
        {
            var cursor = 0;

            try
            {
                foreach (var metric in metrics)
                    while (true)
                        try
                        {
                            cursor = CustomPrometheusSerializer.WriteMetric(buffer, cursor, metric);
                            break;
                        }
                        catch (IndexOutOfRangeException)
                        {
                            var bufferSize = buffer.Length * 2;

                            // there are two cases we might run into the following condition:
                            // 1. we have many metrics to be exported - in this case we probably want
                            //    to put some upper limit and allow the user to configure it.
                            // 2. we got an IndexOutOfRangeException which was triggered by some other
                            //    code instead of the buffer[cursor++] - in this case we should give up
                            //    at certain point rather than allocating like crazy.
                            if (bufferSize > 100 * 1024 * 1024)
                                throw;

                            var newBuffer = new byte[bufferSize];
                            buffer.CopyTo(newBuffer, 0);
                            buffer = newBuffer;
                        }

                previousDataView = new ArraySegment<byte>(buffer, 0, Math.Max(cursor - 1, 0));
                return ExportResult.Success;
            }
            catch (Exception)
            {
                previousDataView = new ArraySegment<byte>(Array.Empty<byte>(), 0, 0);
                return ExportResult.Failure;
            }
        }

        public readonly struct CollectionResponse
        {
            public CollectionResponse(ArraySegment<byte> view, DateTime generatedAtUtc, bool fromCache)
            {
                View = view;
                GeneratedAtUtc = generatedAtUtc;
                FromCache = fromCache;
            }

            public ArraySegment<byte> View { get; }

            public DateTime GeneratedAtUtc { get; }

            public bool FromCache { get; }
        }
    }
}
