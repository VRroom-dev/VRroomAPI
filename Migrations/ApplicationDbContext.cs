using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VRroomAPI.Models;

namespace VRroomAPI.Migrations;
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid> {
	public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

	protected override void OnModelCreating(ModelBuilder builder) {
		base.OnModelCreating(builder);
		
		builder.Entity<Content>()
			.HasOne(c => c.Owner)
			.WithMany(p => p.OwnedContent)
			.HasForeignKey("OwnerId")
			.OnDelete(DeleteBehavior.Restrict);
		
		builder.Entity<Content>()
			.HasMany(c => c.ShareGroups)
			.WithMany()
			.UsingEntity(j => j.ToTable("ContentShareGroups"));
		
		builder.Entity<Bundle>()
			.HasOne(b => b.Content)
			.WithMany(c => c.PreviousVersions)
			.HasForeignKey(b => b.ContentId)
			.OnDelete(DeleteBehavior.Cascade);
    
		builder.Entity<Content>()
			.HasOne(c => c.ActiveBundle)
			.WithMany()
			.HasForeignKey(c => c.ActiveBundleId)
			.OnDelete(DeleteBehavior.SetNull);
	}

	public DbSet<UserProfile> UserProfiles { get; set; } = null!;
	public DbSet<UserSession> UserSessions { get; set; } = null!;
	public DbSet<Content> Content { get; set; } = null!;
	public DbSet<Bundle> Bundles { get; set; } = null!;
	public DbSet<ShareGroup> ShareGroups { get; set; } = null!;
}