using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Sondor.Cqrs.Benchmarks.RequestBenchmarks).Assembly).Run(args);
