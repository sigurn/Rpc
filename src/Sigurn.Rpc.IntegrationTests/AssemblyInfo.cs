// RpcLogging is process-wide state: the tracing tests swap the logger factory and assert on what the
// runtime writes, so no other test may be running while they do.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
