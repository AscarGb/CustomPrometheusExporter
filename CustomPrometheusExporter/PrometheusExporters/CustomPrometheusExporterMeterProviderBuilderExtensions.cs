using System;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;

namespace CustomPrometheusExporter.PrometheusExporters
{
    public static class CustomPrometheusExporterMeterProviderBuilderExtensions
    {
        /// <summary>
        ///     Adds <see cref="PrometheusExporter" /> to the <see cref="MeterProviderBuilder" />.
        /// </summary>
        /// <param name="builder"><see cref="MeterProviderBuilder" /> builder to use.</param>
        /// <param name="configure">Exporter configuration options.</param>
        /// <param name="exporter"></param>
        /// <returns>The instance of <see cref="MeterProviderBuilder" /> to chain the calls.</returns>
        public static MeterProviderBuilder AddCustomPrometheusExporter(
            this MeterProviderBuilder builder,
            out CustomPrometheusExporter exporter)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder), "Must not be null");

            return AddPrometheusExporter(builder, out exporter);
        }

        private static MeterProviderBuilder AddPrometheusExporter(MeterProviderBuilder builder,
            out CustomPrometheusExporter exporter)
        {
            var options = new PrometheusExporterOptions();
            exporter = new CustomPrometheusExporter(options);
            var reader = new BaseExportingMetricReader(exporter);
            reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;

            return builder.AddReader(reader);
        }
    }
}
