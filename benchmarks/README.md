# Benchmarks

Run `dotnet run --project benchmarks/AnkiIO.Benchmarks -c Release -- 10000`. The dependency-free harness measures large native JSON export/import and APKG creation while reporting payload size and elapsed time. Use an external process profiler for allocation measurements.
