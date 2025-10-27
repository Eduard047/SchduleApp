using Microsoft.EntityFrameworkCore;
using BlazorWasmDotNet8AspNetCoreHosted.Server.Domain.Entities;

namespace BlazorWasmDotNet8AspNetCoreHosted.Server.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherModule> TeacherModules => Set<TeacherModule>();

    public DbSet<LessonTypeRef> LessonTypes => Set<LessonTypeRef>();

    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<BuildingTravel> BuildingTravels => Set<BuildingTravel>();

    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<ModuleRoom> ModuleRooms => Set<ModuleRoom>();
    public DbSet<ModuleBuilding> ModuleBuildings => Set<ModuleBuilding>();

    public DbSet<ModulePlan> ModulePlans => Set<ModulePlan>();
    public DbSet<ScheduleItem> ScheduleItems => Set<ScheduleItem>();

    public DbSet<TeacherCourseLoad> TeacherCourseLoads => Set<TeacherCourseLoad>();
    public DbSet<TeacherWorkingHour> TeacherWorkingHours => Set<TeacherWorkingHour>();
    
    public DbSet<ModuleTopic> ModuleTopics => Set<ModuleTopic>();

    public DbSet<LunchConfig> LunchConfigs => Set<LunchConfig>();
    public DbSet<CalendarException> CalendarExceptions => Set<CalendarException>();
    public DbSet<ModuleSequenceItem> ModuleSequenceItems => Set<ModuleSequenceItem>();
    public DbSet<ModuleFiller> ModuleFillers => Set<ModuleFiller>();
    public DbSet<TeacherDraftItem> TeacherDraftItems => Set<TeacherDraftItem>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();


    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LessonTypeRef>().ToTable("LessonTypes");

        b.Entity<TeacherModule>().HasKey(x => new { x.TeacherId, x.ModuleId });
        b.Entity<ModuleRoom>().HasKey(x => new { x.ModuleId, x.RoomId });
        b.Entity<ModuleBuilding>().HasKey(x => new { x.ModuleId, x.BuildingId });

        b.Entity<Group>()
            .HasOne(g => g.Course)
            .WithMany(c => c.Groups)
            .HasForeignKey(g => g.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Module>(e =>
        {
            e.HasOne(m => m.Course)
                .WithMany(c => c.Modules)
                .HasForeignKey(m => m.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
            
            e.Property(m => m.Credits)
                .HasColumnType("decimal(6,2)")
                .HasDefaultValue(0m);
        });

        b.Entity<ModulePlan>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Course)
                .WithMany()
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Module)
                .WithMany()
                .HasForeignKey(x => x.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.CourseId, x.ModuleId }).IsUnique();

            e.Property(x => x.TargetHours).HasDefaultValue(0);
            e.Property(x => x.ScheduledHours).HasDefaultValue(0);
        });

        
        b.Entity<Room>()
            .HasOne(r => r.Building).WithMany().HasForeignKey(r => r.BuildingId);

        b.Entity<BuildingTravel>(e =>
        {
            e.HasIndex(x => new { x.FromBuildingId, x.ToBuildingId }).IsUnique();
            e.HasOne(x => x.From).WithMany().HasForeignKey(x => x.FromBuildingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.To).WithMany().HasForeignKey(x => x.ToBuildingId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TeacherWorkingHour>(e =>
        {
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId);
        });

        b.Entity<TeacherCourseLoad>(e =>
        {
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId);
            e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId);
        });

        b.Entity<ScheduleItem>(e =>
        {
            e.HasOne(si => si.Teacher).WithMany()
                .HasForeignKey(si => si.TeacherId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(si => si.Room).WithMany()
                .HasForeignKey(si => si.RoomId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(si => si.Group).WithMany()
                .HasForeignKey(si => si.GroupId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(si => si.Module).WithMany()
                .HasForeignKey(si => si.ModuleId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(si => si.LessonType).WithMany()
                .HasForeignKey(si => si.LessonTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            
            e.HasOne(si => si.ModuleTopic).WithMany()
                .HasForeignKey(si => si.ModuleTopicId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => new { x.Date, x.GroupId });
            e.HasIndex(x => new { x.Date, x.TeacherId });
            e.HasIndex(x => new { x.Date, x.RoomId });
        });

        b.Entity<CalendarException>(e =>
        {
            e.HasIndex(x => x.Date).IsUnique();
        });


        b.Entity<ModuleSequenceItem>(e =>
        {
            e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CourseId, x.ModuleId }).IsUnique();
            e.HasIndex(x => new { x.CourseId, x.Order }).IsUnique();
            e.Property(x => x.Order).HasDefaultValue(0);
        });

        b.Entity<ModuleFiller>(e =>
        {
            e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CourseId, x.ModuleId }).IsUnique();
        });

        b.Entity<TimeSlot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Course)
                .WithMany()
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(x => x.SortOrder).HasDefaultValue(0);
            e.Property(x => x.IsActive).HasDefaultValue(true);

            
            e.HasIndex(x => new { x.CourseId, x.SortOrder }).IsUnique();
        });
        b.Entity<TeacherDraftItem>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LessonType).WithMany().HasForeignKey(x => x.LessonTypeId).OnDelete(DeleteBehavior.Restrict);
            
            e.HasOne(x => x.ModuleTopic).WithMany().HasForeignKey(x => x.ModuleTopicId).OnDelete(DeleteBehavior.SetNull);

            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.BatchKey).HasMaxLength(64);

            e.HasIndex(x => new { x.Date, x.GroupId });
            e.HasIndex(x => new { x.Date, x.TeacherId });
            e.HasIndex(x => new { x.Date, x.RoomId });
        });

        
        b.Entity<ModuleTopic>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Module)
                .WithMany(m => m.Topics)
                .HasForeignKey(x => x.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.LessonType)
                .WithMany()
                .HasForeignKey(x => x.LessonTypeId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Order).HasDefaultValue(0);
            e.Property(x => x.BlockNumber).HasDefaultValue(0);
            e.Property(x => x.BlockTitle).HasMaxLength(512).HasDefaultValue(string.Empty);
            e.Property(x => x.LessonNumber).HasDefaultValue(0);
            e.Property(x => x.QuestionNumber).HasDefaultValue(0);
            e.HasIndex(x => new { x.ModuleId, x.Order }).IsUnique();
            e.HasIndex(x => new { x.ModuleId, x.BlockNumber, x.LessonNumber, x.QuestionNumber }).IsUnique();
        });

    }
}








