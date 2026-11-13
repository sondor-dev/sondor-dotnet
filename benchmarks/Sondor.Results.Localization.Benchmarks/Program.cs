using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Sondor.Results.Localization.Benchmarks.LocalizeBenchmarks).Assembly).Run(args);
