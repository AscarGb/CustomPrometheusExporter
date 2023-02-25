using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using OpenTelemetry.Metrics;

namespace CustomPrometheusExporter.PrometheusExporters
{
    /// <summary>
    ///     OpenTelemetry additions to the PrometheusSerializer.
    /// </summary>
    public static class CustomPrometheusSerializer
    {
        private static readonly string[] MetricTypes =
        {
            "untyped", "counter", "gauge", "summary", "histogram", "histogram", "histogram", "histogram", "untyped"
        };

        public static int WriteMetric(byte[] buffer, int cursor, Metric metric)
        {
            if (!string.IsNullOrWhiteSpace(metric.Description))
                cursor = WriteHelpText(buffer, cursor, metric.Name, metric.Unit, metric.Description);

            var metricType = (int)metric.MetricType >> 4;
            cursor = WriteTypeInfo(buffer, cursor, metric.Name, metric.Unit, MetricTypes[metricType]);

            if (!metric.MetricType.IsHistogram())
                foreach (ref readonly var metricPoint in metric.GetMetricPoints())
                {
                    var tags = metricPoint.Tags;
                    var timestamp = metricPoint.EndTime.ToUnixTimeMilliseconds();

                    // Counter and Gauge
                    cursor = WriteMetricName(buffer, cursor, metric.Name, metric.Unit);

                    if (tags.Count > 0)
                    {
                        buffer[cursor++] = unchecked((byte)'{');

                        foreach (var tag in tags)
                        {
                            cursor = WriteLabel(buffer, cursor, tag.Key, tag.Value);
                            buffer[cursor++] = unchecked((byte)',');
                        }

                        buffer[cursor - 1] =
                            unchecked((byte)'}'); // Note: We write the '}' over the last written comma, which is extra.
                    }

                    buffer[cursor++] = unchecked((byte)' ');

                    // TODO: MetricType is same for all MetricPoints
                    // within a given Metric, so this check can avoided
                    // for each MetricPoint
                    if (((int)metric.MetricType & 0b_0000_1111) == 0x0a /* I8 */)
                    {
                        if (metric.MetricType.IsSum())
                            cursor = WriteLong(buffer, cursor, metricPoint.GetSumLong());
                        else
                            cursor = WriteLong(buffer, cursor, metricPoint.GetGaugeLastValueLong());
                    }
                    else
                    {
                        if (metric.MetricType.IsSum())
                            cursor = WriteDouble(buffer, cursor, metricPoint.GetSumDouble());
                        else
                            cursor = WriteDouble(buffer, cursor, metricPoint.GetGaugeLastValueDouble());
                    }

                    buffer[cursor++] = unchecked((byte)' ');

                    cursor = WriteLong(buffer, cursor, timestamp);

                    buffer[cursor++] = ASCII_LINEFEED;
                }
            else
                foreach (ref readonly var metricPoint in metric.GetMetricPoints())
                {
                    var tags = metricPoint.Tags;
                    var timestamp = metricPoint.EndTime.ToUnixTimeMilliseconds();

                    long totalCount = 0;
                    foreach (var histogramMeasurement in metricPoint.GetHistogramBuckets())
                    {
                        totalCount += histogramMeasurement.BucketCount;

                        cursor = WriteMetricName(buffer, cursor, metric.Name, metric.Unit);
                        cursor = WriteAsciiStringNoEscape(buffer, cursor, "_bucket{");

                        foreach (var tag in tags)
                        {
                            cursor = WriteLabel(buffer, cursor, tag.Key, tag.Value);
                            buffer[cursor++] = unchecked((byte)',');
                        }

                        cursor = WriteAsciiStringNoEscape(buffer, cursor, "le=\"");

                        if (histogramMeasurement.ExplicitBound != double.PositiveInfinity)
                            cursor = WriteDouble(buffer, cursor, histogramMeasurement.ExplicitBound);
                        else
                            cursor = WriteAsciiStringNoEscape(buffer, cursor, "+Inf");

                        cursor = WriteAsciiStringNoEscape(buffer, cursor, "\"} ");

                        cursor = WriteLong(buffer, cursor, totalCount);
                        buffer[cursor++] = unchecked((byte)' ');

                        cursor = WriteLong(buffer, cursor, timestamp);

                        buffer[cursor++] = ASCII_LINEFEED;
                    }

                    // Histogram sum
                    cursor = WriteMetricName(buffer, cursor, metric.Name, metric.Unit);
                    cursor = WriteAsciiStringNoEscape(buffer, cursor, "_sum");

                    if (tags.Count > 0)
                    {
                        buffer[cursor++] = unchecked((byte)'{');

                        foreach (var tag in tags)
                        {
                            cursor = WriteLabel(buffer, cursor, tag.Key, tag.Value);
                            buffer[cursor++] = unchecked((byte)',');
                        }

                        buffer[cursor - 1] =
                            unchecked((byte)'}'); // Note: We write the '}' over the last written comma, which is extra.
                    }

                    buffer[cursor++] = unchecked((byte)' ');

                    cursor = WriteDouble(buffer, cursor, metricPoint.GetHistogramSum());
                    buffer[cursor++] = unchecked((byte)' ');

                    cursor = WriteLong(buffer, cursor, timestamp);

                    buffer[cursor++] = ASCII_LINEFEED;

                    // Histogram count
                    cursor = WriteMetricName(buffer, cursor, metric.Name, metric.Unit);
                    cursor = WriteAsciiStringNoEscape(buffer, cursor, "_count");

                    if (tags.Count > 0)
                    {
                        buffer[cursor++] = unchecked((byte)'{');

                        foreach (var tag in tags)
                        {
                            cursor = WriteLabel(buffer, cursor, tag.Key, tag.Value);
                            buffer[cursor++] = unchecked((byte)',');
                        }

                        buffer[cursor - 1] =
                            unchecked((byte)'}'); // Note: We write the '}' over the last written comma, which is extra.
                    }

                    buffer[cursor++] = unchecked((byte)' ');

                    cursor = WriteLong(buffer, cursor, metricPoint.GetHistogramCount());
                    buffer[cursor++] = unchecked((byte)' ');

                    cursor = WriteLong(buffer, cursor, timestamp);

                    buffer[cursor++] = ASCII_LINEFEED;
                }

            buffer[cursor++] = ASCII_LINEFEED;

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteDouble(byte[] buffer, int cursor, double value)
        {
            if (!double.IsInfinity(value) && !double.IsNaN(value))

            {
                cursor = WriteAsciiStringNoEscape(buffer, cursor, value.ToString(CultureInfo.InvariantCulture));
            }
            else if (double.IsPositiveInfinity(value))
            {
                cursor = WriteAsciiStringNoEscape(buffer, cursor, "+Inf");
            }
            else if (double.IsNegativeInfinity(value))
            {
                cursor = WriteAsciiStringNoEscape(buffer, cursor, "-Inf");
            }
            else
            {
                Debug.Assert(double.IsNaN(value), $"{nameof(value)} should be NaN.");
                cursor = WriteAsciiStringNoEscape(buffer, cursor, "Nan");
            }

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteLong(byte[] buffer, int cursor, long value)
        {
            cursor = WriteAsciiStringNoEscape(buffer, cursor, value.ToString(CultureInfo.InvariantCulture));

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteAsciiStringNoEscape(byte[] buffer, int cursor, string value)
        {
            for (var i = 0; i < value.Length; i++)
                buffer[cursor++] = unchecked((byte)value[i]);

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteUnicodeNoEscape(byte[] buffer, int cursor, ushort ordinal)
        {
            if (ordinal <= 0x7F)
            {
                buffer[cursor++] = unchecked((byte)ordinal);
            }
            else if (ordinal <= 0x07FF)
            {
                buffer[cursor++] = unchecked((byte)(0b_1100_0000 | (ordinal >> 6)));
                buffer[cursor++] = unchecked((byte)(0b_1000_0000 | (ordinal & 0b_0011_1111)));
            }
            else if (ordinal <= 0xFFFF)
            {
                buffer[cursor++] = unchecked((byte)(0b_1110_0000 | (ordinal >> 12)));
                buffer[cursor++] = unchecked((byte)(0b_1000_0000 | ((ordinal >> 6) & 0b_0011_1111)));
                buffer[cursor++] = unchecked((byte)(0b_1000_0000 | (ordinal & 0b_0011_1111)));
            }
            else
            {
                Debug.Assert(ordinal <= 0xFFFF, ".NET string should not go beyond Unicode BMP.");
            }

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteUnicodeString(byte[] buffer, int cursor, string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var ordinal = (ushort)value[i];
                switch (ordinal)
                {
                    case ASCII_REVERSE_SOLIDUS:
                        buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                        buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                        break;
                    case ASCII_LINEFEED:
                        buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                        buffer[cursor++] = unchecked((byte)'n');
                        break;
                    default:
                        cursor = WriteUnicodeNoEscape(buffer, cursor, ordinal);
                        break;
                }
            }

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteLabelKey(byte[] buffer, int cursor, string value)
        {
            Debug.Assert(!string.IsNullOrEmpty(value), $"{nameof(value)} should not be null or empty.");

            var ordinal = (ushort)value[0];

            if (ordinal >= '0' && ordinal <= '9')
                buffer[cursor++] = unchecked((byte)'_');

            for (var i = 0; i < value.Length; i++)
            {
                ordinal = value[i];

                if (ordinal >= 'A' && ordinal <= 'Z' ||
                    ordinal >= 'a' && ordinal <= 'z' ||
                    ordinal >= '0' && ordinal <= '9')
                    buffer[cursor++] = unchecked((byte)ordinal);
                else
                    buffer[cursor++] = unchecked((byte)'_');
            }

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteLabelValue(byte[] buffer, int cursor, string value)
        {
            Debug.Assert(value != null, $"{nameof(value)} should not be null.");

            for (var i = 0; i < value.Length; i++)
            {
                var ordinal = (ushort)value[i];
                switch (ordinal)
                {
                    case ASCII_QUOTATION_MARK:
                        buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                        buffer[cursor++] = ASCII_QUOTATION_MARK;
                        break;
                    case ASCII_REVERSE_SOLIDUS:
                        buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                        buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                        break;
                    case ASCII_LINEFEED:
                        buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                        buffer[cursor++] = unchecked((byte)'n');
                        break;
                    default:
                        cursor = WriteUnicodeNoEscape(buffer, cursor, ordinal);
                        break;
                }
            }

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteLabel(byte[] buffer, int cursor, string labelKey, object labelValue)
        {
            cursor = WriteLabelKey(buffer, cursor, labelKey);
            buffer[cursor++] = unchecked((byte)'=');
            buffer[cursor++] = unchecked((byte)'"');

            // In Prometheus, a label with an empty label value is considered equivalent to a label that does not exist.
            cursor = WriteLabelValue(buffer, cursor, labelValue?.ToString() ?? string.Empty);
            buffer[cursor++] = unchecked((byte)'"');

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteMetricName(byte[] buffer, int cursor, string metricName, string metricUnit = null)
        {
            Debug.Assert(!string.IsNullOrEmpty(metricName), $"{nameof(metricName)} should not be null or empty.");

            for (var i = 0; i < metricName.Length; i++)
            {
                var ordinal = (ushort)metricName[i];
                buffer[cursor++] = ordinal switch
                {
                    ASCII_FULL_STOP or ASCII_HYPHEN_MINUS => unchecked((byte)'_'),
                    _ => unchecked((byte)ordinal)
                };
            }

            if (!string.IsNullOrEmpty(metricUnit))
            {
                buffer[cursor++] = unchecked((byte)'_');

                for (var i = 0; i < metricUnit.Length; i++)
                {
                    var ordinal = (ushort)metricUnit[i];

                    if (ordinal >= 'A' && ordinal <= 'Z' ||
                        ordinal >= 'a' && ordinal <= 'z' ||
                        ordinal >= '0' && ordinal <= '9')
                        buffer[cursor++] = unchecked((byte)ordinal);
                    else
                        buffer[cursor++] = unchecked((byte)'_');
                }
            }

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteHelpText(byte[] buffer,
            int cursor,
            string metricName,
            string metricUnit = null,
            string metricDescription = null)
        {
            cursor = WriteAsciiStringNoEscape(buffer, cursor, "# HELP ");
            cursor = WriteMetricName(buffer, cursor, metricName, metricUnit);

            if (!string.IsNullOrEmpty(metricDescription))
            {
                buffer[cursor++] = unchecked((byte)' ');
                cursor = WriteUnicodeString(buffer, cursor, metricDescription);
            }

            buffer[cursor++] = ASCII_LINEFEED;

            return cursor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WriteTypeInfo(byte[] buffer,
            int cursor,
            string metricName,
            string metricUnit,
            string metricType)
        {
            Debug.Assert(!string.IsNullOrEmpty(metricType), $"{nameof(metricType)} should not be null or empty.");

            cursor = WriteAsciiStringNoEscape(buffer, cursor, "# TYPE ");
            cursor = WriteMetricName(buffer, cursor, metricName, metricUnit);
            buffer[cursor++] = unchecked((byte)' ');
            cursor = WriteAsciiStringNoEscape(buffer, cursor, metricType);

            buffer[cursor++] = ASCII_LINEFEED;

            return cursor;
        }

#pragma warning disable SA1310 // Field name should not contain an underscore
        private const byte ASCII_QUOTATION_MARK = 0x22; // '"'
        private const byte ASCII_FULL_STOP = 0x2E; // '.'
        private const byte ASCII_HYPHEN_MINUS = 0x2D; // '-'
        private const byte ASCII_REVERSE_SOLIDUS = 0x5C; // '\\'
        private const byte ASCII_LINEFEED = 0x0A; // `\n`
#pragma warning restore SA1310 // Field name should not contain an underscore
    }
}
