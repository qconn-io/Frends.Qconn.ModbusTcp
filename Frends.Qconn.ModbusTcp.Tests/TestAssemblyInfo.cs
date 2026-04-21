// All test classes share a static ConnectionPool — parallel execution causes races.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
