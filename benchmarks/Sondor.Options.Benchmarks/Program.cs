using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Sondor.Options.Benchmarks.BindAndValidateBenchmarks).Assembly).Run(args);
