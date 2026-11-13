using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Sondor.AspNetCore.Benchmarks.ClassifierBenchmarks).Assembly).Run(args);
