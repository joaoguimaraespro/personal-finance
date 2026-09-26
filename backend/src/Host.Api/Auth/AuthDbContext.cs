using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Host.Api.Auth;

public sealed class AppUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>ASP.NET Core Identity store in its own schema, separate from financial data.</summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "auth";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
    }
}
