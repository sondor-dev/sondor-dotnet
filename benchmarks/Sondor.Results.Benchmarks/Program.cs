using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Sondor.Results.Benchmarks.CreateAndReadBenchmarks).Assembly).Run(args);
