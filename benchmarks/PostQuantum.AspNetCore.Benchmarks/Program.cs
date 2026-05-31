using BenchmarkDotNet.Running;

// Entry point for the benchmark suite. Run a specific class with:
//   dotnet run -c Release --project benchmarks/PostQuantum.AspNetCore.Benchmarks -- --filter '*Validate*'
// Or run all:
//   dotnet run -c Release --project benchmarks/PostQuantum.AspNetCore.Benchmarks
BenchmarkSwitcher.FromAssembly(typeof(PostQuantum.AspNetCore.Benchmarks.TokenValidationBenchmarks).Assembly).Run(args);

