// Public surface re-exposes the engine library's IPqJwtReplayCache and
// StackExchange.Redis's IDatabase / IConnectionMultiplexer, neither of
// which are CLS-compliant. Advertise that honestly.
[assembly: System.CLSCompliant(false)]
