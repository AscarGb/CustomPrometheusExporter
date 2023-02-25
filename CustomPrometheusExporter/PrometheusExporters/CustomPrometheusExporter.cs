using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;

namespace CustomPrometheusExporter.PrometheusExporters
{
    /// <summary>
    ///     Exporter of OpenTelemetry metrics to Prometheus.
    /// </summary>
    [ExportModes(ExportModes.Pull)]
    public class CustomPrometheusExporter : BaseExporter<Metric>, IPullMetricExporter
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="PrometheusExporter" /> class.
        /// </summary>
        /// <param name="options">Options for the exporter.</param>
        public CustomPrometheusExporter(PrometheusExporterOptions options)
        {
            Options = options;
            CollectionManager = new CustomPrometheusCollectionManager(this);
        }

        public readonly PrometheusExporterOptions Options;

        private bool disposed;

        public Func<Batch<Metric>, ExportResult> OnExport { get; set; }

        public CustomPrometheusCollectionManager CollectionManager { get; }

        public Func<int, bool> Collect { get; set; }

        /// <summary>
        ///     Строковое представление метрик
        /// </summary>
        /// <returns></returns>
        public async Task<(string metrics, DateTime generatedAtUtc)> GetMetricsAsString()
        {
            try
            {
                var collectionResponse = await CollectionManager.EnterCollect().ConfigureAwait(false);
                try
                {
                    if (collectionResponse.View.Count > 0)
                    {
                        if (collectionResponse.View.Array is null)
                            return (string.Empty, DateTime.Now);

                        var result = Encoding.UTF8.GetString(
                            collectionResponse.View.Array,
                            0,
                            collectionResponse.View.Count);
                        return (result, collectionResponse.GeneratedAtUtc);
                    }
                }
                finally
                {
                    CollectionManager.ExitCollect();
                }
            }
            catch (Exception)
            {
                //TODO:
                //PrometheusExporterEventSource.Log.FailedExport(ex);
            }

            return (string.Empty, DateTime.Now);
        }

        /// <summary>
        ///     Записать метрики в HttpListenerContext
        /// </summary>
        /// <param name="context"></param>
        public async Task WriteToHttpListenerContext(HttpListenerContext context)
        {
            try
            {
                var collectionResponse = await CollectionManager.EnterCollect().ConfigureAwait(false);
                try
                {
                    context.Response.Headers.Add("Server", string.Empty);
                    if (collectionResponse.View.Count > 0)
                    {
                        context.Response.StatusCode = 200;
                        context.Response.Headers.Add("Last-Modified", collectionResponse.GeneratedAtUtc.ToString("R"));
                        context.Response.ContentType = "text/plain; charset=utf-8; version=0.0.4";
                        context.Response.ContentLength64 = collectionResponse.View.Count;
                        await context.Response.OutputStream
                            .WriteAsync(collectionResponse.View.Array, 0, collectionResponse.View.Count)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        // It's not expected to have no metrics to collect, but it's not necessarily a failure, either.
                        context.Response.StatusCode = 204;
                    }
                }
                finally
                {
                    CollectionManager.ExitCollect();
                }
            }
            catch (Exception)
            {
                context.Response.StatusCode = 500;
            }

            try
            {
                context.Response.Close();
            }
            catch
            {
                // ignored
            }
        }

        public override ExportResult Export(in Batch<Metric> metrics) => OnExport(metrics);

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
                disposed = true;

            base.Dispose(disposing);
        }
    }
}
