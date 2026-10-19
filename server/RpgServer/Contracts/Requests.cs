namespace RpgServer;

/// <summary>DTO request yang diterima endpoint (bentuk JSON tetap sama seperti v1).</summary>
public sealed record ProfileRequest(string? Name);

public sealed record EventRequest(string? Kind, string? Detail);
