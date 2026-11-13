using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Sondor.Options.FluentValidation.Benchmarks.ValidationBenchmarks).Assembly).Run(args);
