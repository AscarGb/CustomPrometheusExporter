# CustomPrometheusExporter

AddCustomPrometheusExporter allows you to get a string representation of metrics at any time for OpenTelemetry, which
does not allow you to do AddPrometheusExporter

```c#
    public static class MetricProviderBuilderClassicDotNet
    {
        private static readonly object Lock = new();
        private static MeterProvider _meterProvider;
        private static CustomPrometheusExporter _customPrometheusExporter;

        public static IDisposable GetMeterProvider()
        {
            lock (Lock)
            {
                if (_meterProvider != null)
                    return _meterProvider;

                _meterProvider = Sdk.CreateMeterProviderBuilder()
                    .AddMeter("*")
                    .AddAspNetInstrumentation()
                    .AddView(AuthMetrics.ActLogonSeconds, // <-- project metrics
                        new ExplicitBucketHistogramConfiguration
                        {
                            Boundaries = Boundaries.DefaultBoundaries
                        })
                    .AddView(AuthMetrics.ActAuthSeconds,
                        new ExplicitBucketHistogramConfiguration
                        {
                            Boundaries = Boundaries.DefaultBoundaries
                        })
                        
                        //
                        // some metrics
                        //
                    
                    .AddCustomPrometheusExporter(out _customPrometheusExporter) // <-- new extension
                    .Build();

                SetupEmptyActivity();

                return _meterProvider;
            }
        }

        public static async Task<(string metrics, DateTime generatedAtUtc)> GetMetricAsString() =>
            await _customPrometheusExporter.GetMetricsAsString().ConfigureAwait(false);
        
        public static async Task WriteToHttpListenerContext(HttpListenerContext context) =>
            await _customPrometheusExporter.WriteToHttpListenerContext(context).ConfigureAwait(false);

        private static void SetupEmptyActivity() =>
            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = _ => true,

                SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                    ActivitySamplingResult.AllData,

                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllData
            });
    }
    
    public static class Boundaries
    {
        public static readonly double[] DefaultBoundaries = { 0.2, 0.5, 1, 5, 10, 30 };
    }
    
    
```
