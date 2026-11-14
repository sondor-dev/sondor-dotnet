using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Sondor.Http.Benchmarks.CallBenchmarks).Assembly).Run(args);
