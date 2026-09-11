namespace SmartInput.Core.Diagnostics;

internal sealed class RollingDurationAggregate
{
    internal const int MaxSamples = 256;

    private readonly object _sync = new();
    private readonly double[] _samples = new double[MaxSamples];
    private int _sampleCount;
    private int _nextIndex;

    public void Record(double milliseconds)
    {
        if (milliseconds < 0)
        {
            milliseconds = 0;
        }

        lock (_sync)
        {
            _samples[_nextIndex] = milliseconds;
            _nextIndex = (_nextIndex + 1) % MaxSamples;
            if (_sampleCount < MaxSamples)
            {
                _sampleCount++;
            }
        }
    }

    public DurationAggregateSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            if (_sampleCount == 0)
            {
                return new DurationAggregateSnapshot();
            }

            var copy = new double[_sampleCount];
            if (_sampleCount < MaxSamples)
            {
                Array.Copy(_samples, copy, _sampleCount);
            }
            else
            {
                var tailLength = MaxSamples - _nextIndex;
                Array.Copy(_samples, _nextIndex, copy, 0, tailLength);
                Array.Copy(_samples, 0, copy, tailLength, _nextIndex);
            }

            Array.Sort(copy);

            var sum = 0d;
            var max = 0d;
            foreach (var sample in copy)
            {
                sum += sample;
                if (sample > max)
                {
                    max = sample;
                }
            }

            var average = sum / copy.Length;
            var p95Index = Math.Clamp((int)Math.Ceiling(copy.Length * 0.95) - 1, 0, copy.Length - 1);

            return new DurationAggregateSnapshot
            {
                SampleCount = copy.Length,
                AverageMilliseconds = average,
                MaxMilliseconds = max,
                Percentile95Milliseconds = copy[p95Index],
            };
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _sampleCount = 0;
            _nextIndex = 0;
            Array.Clear(_samples, 0, _samples.Length);
        }
    }
}

internal sealed class DurationAggregateSnapshot
{
    public long SampleCount { get; init; }

    public double AverageMilliseconds { get; init; }

    public double MaxMilliseconds { get; init; }

    public double Percentile95Milliseconds { get; init; }
}
