using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Sondor.Observability.Benchmarks.SpanBenchmarks).Assembly).Run(args);
