namespace PostQuantum.AspNetCore;

/// <summary>
/// Well-known constants for the post-quantum JWT bearer authentication scheme.
/// </summary>
public static class PostQuantumJwtBearerDefaults
{
    /// <summary>
    /// The default authentication scheme name. Use this as the
    /// <see cref="Microsoft.AspNetCore.Authentication.AuthenticationOptions.DefaultScheme"/>
    /// or with <c>[Authorize(AuthenticationSchemes = PostQuantumJwtBearerDefaults.AuthenticationScheme)]</c>.
    /// </summary>
    public const string AuthenticationScheme = "PostQuantumJwtBearer";

    /// <summary>The <c>Authorization</c> header scheme this handler expects (<c>Bearer</c>).</summary>
    public const string BearerPrefix = "Bearer ";
}
