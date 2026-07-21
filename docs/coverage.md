# Coverage policy

Every portable test project collects Cobertura coverage in CI. ReportGenerator merges results and produces Cobertura plus browsable HTML artifacts. The alpha quality gate is 80% line and 60% branch coverage, chosen from measured behavioral tests rather than exclusions. The roadmap raises these to 85% line and 80% branch before a stable release. Generated code and test assemblies are not counted as product coverage.

The 0.1.0-alpha.1 release candidate measured 92.33% line coverage and 71.70% branch coverage across 46 portable tests.
