namespace DotnetApi.Infrastructure;

public static class AuthDefaults
{
    /// <summary>
    /// Prototype-only fallback password for seeded users and for users created
    /// without an explicit password. Replace before using real data.
    /// </summary>
    public const string DemoPassword = "Passw0rd!";
}
