namespace InternetMeter.Models;

public sealed record ConnectionInfo(
    string Process,
    int ProcessId,
    string Protocol,
    string LocalEndpoint,
    string RemoteEndpoint,
    string State);

