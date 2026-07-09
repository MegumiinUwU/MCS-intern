using Microsoft.EntityFrameworkCore;
using MCS_app.Models;

namespace MCS_app.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Employee>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
                entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Email).HasMaxLength(200).IsRequired();
                entity.Property(e => e.JobTitle).HasMaxLength(150);
                entity.Property(e => e.Department).HasMaxLength(150);
                entity.Property(e => e.Salary).HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<EmployeeDocument>(entity =>
            {
                entity.HasKey(d => d.Id);
                entity.Property(d => d.FileName).HasMaxLength(255).IsRequired();
                entity.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
                entity.Property(d => d.Data).IsRequired();
                entity.HasOne<Employee>()
                    .WithMany()
                    .HasForeignKey(d => d.EmployeeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Seed data so the endpoints return results out of the box.
            modelBuilder.Entity<Employee>().HasData(
                new Employee
                {
                    Id = 1,
                    FirstName = "Sara",
                    LastName = "Ahmed",
                    Email = "sara.ahmed@example.com",
                    JobTitle = "Software Engineer",
                    Department = "Engineering",
                    Salary = 85000m,
                    HireDate = new DateOnly(2022, 3, 15)
                },
                new Employee
                {
                    Id = 2,
                    FirstName = "Omar",
                    LastName = "Khaled",
                    Email = "omar.khaled@example.com",
                    JobTitle = "Product Manager",
                    Department = "Product",
                    Salary = 95000m,
                    HireDate = new DateOnly(2021, 7, 1)
                },
                new Employee
                {
                    Id = 3,
                    FirstName = "Mona",
                    LastName = "Youssef",
                    Email = "mona.youssef@example.com",
                    JobTitle = "UX Designer",
                    Department = "Design",
                    Salary = 78000m,
                    HireDate = new DateOnly(2023, 1, 10)
                }
            );
        }
    }
}
