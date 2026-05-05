using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowInstantLex", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowAnyOrigin();
    });
});

var app = builder.Build();

app.UseCors("AllowInstantLex");

app.MapGet("/", () => "InstantLex API is running.");

app.MapGet("/api/test-db", async (AppDbContext db) =>
{
    try
    {
        bool canConnect = await db.Database.CanConnectAsync();
        int userCount = await db.Users.CountAsync();
        int reminderCount = await db.Reminders.CountAsync();

        return Results.Ok(new
        {
            success = true,
            canConnect,
            userCount,
            reminderCount
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString());
    }
});

app.MapPost("/api/auth/register", async (RegisterRequest request, AppDbContext db) =>
{
    string firstName = request.FirstName.Trim();
    string lastName = request.LastName.Trim();
    string email = request.Email.Trim().ToLower();
    string password = request.Password;
    string cls = request.Class.Trim();
    string profile = request.Profile.Trim();
    string country = request.CountryOfOrigin.Trim();
    string institute = request.EducationalInstitute.Trim();

    if (string.IsNullOrWhiteSpace(firstName) ||
        string.IsNullOrWhiteSpace(lastName) ||
        string.IsNullOrWhiteSpace(email) ||
        string.IsNullOrWhiteSpace(password) ||
        string.IsNullOrWhiteSpace(cls) ||
        string.IsNullOrWhiteSpace(profile) ||
        string.IsNullOrWhiteSpace(country) ||
        string.IsNullOrWhiteSpace(institute))
    {
        return Results.BadRequest(new ApiResponse(false, "Please fill in all mandatory fields."));
    }

    if (!email.Contains("@"))
    {
        return Results.BadRequest(new ApiResponse(false, "Please enter a valid email address."));
    }

    if (password.Length < 6)
    {
        return Results.BadRequest(new ApiResponse(false, "Password must be at least 6 characters."));
    }

    bool exists = await db.Users.AnyAsync(u => u.Email == email);

    if (exists)
    {
        return Results.Conflict(new ApiResponse(false, "An account with this email already exists."));
    }

    var user = new UserEntity
    {
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
        Class = cls,
        Profile = profile,
        CountryOfOrigin = country,
        EducationalInstitute = institute,
        CreatedAt = DateTime.UtcNow
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok(new ApiResponse(true, "Account created successfully."));
});

app.MapPost("/api/auth/login", async (LoginRequest request, AppDbContext db) =>
{
    string email = request.Email.Trim().ToLower();
    string password = request.Password;

    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

    if (user == null)
    {
        return Results.Unauthorized();
    }

    bool passwordOk = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);

    if (!passwordOk)
    {
        return Results.Unauthorized();
    }

    var result = new UserDto(
        user.FirstName,
        user.LastName,
        user.Email,
        "",
        user.Class,
        user.Profile,
        user.CountryOfOrigin,
        user.EducationalInstitute
    );

    return Results.Ok(result);
});

app.MapPut("/api/user/update", async (UpdateUserRequest request, AppDbContext db) =>
{
    string email = request.Email.Trim().ToLower();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

    if (user == null)
    {
        return Results.NotFound(new ApiResponse(false, "User not found."));
    }

    if (string.IsNullOrWhiteSpace(request.FirstName) ||
        string.IsNullOrWhiteSpace(request.LastName) ||
        string.IsNullOrWhiteSpace(request.Class) ||
        string.IsNullOrWhiteSpace(request.Profile) ||
        string.IsNullOrWhiteSpace(request.CountryOfOrigin) ||
        string.IsNullOrWhiteSpace(request.EducationalInstitute))
    {
        return Results.BadRequest(new ApiResponse(false, "Please fill in all mandatory fields."));
    }

    user.FirstName = request.FirstName.Trim();
    user.LastName = request.LastName.Trim();
    user.Class = request.Class.Trim();
    user.Profile = request.Profile.Trim();
    user.CountryOfOrigin = request.CountryOfOrigin.Trim();
    user.EducationalInstitute = request.EducationalInstitute.Trim();

    await db.SaveChangesAsync();

    return Results.Ok(new ApiResponse(true, "Profile updated successfully."));
});

app.MapPut("/api/user/change-password", async (ChangePasswordRequest request, AppDbContext db) =>
{
    string email = request.Email.Trim().ToLower();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

    if (user == null)
    {
        return Results.NotFound(new ApiResponse(false, "User not found."));
    }

    if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
        string.IsNullOrWhiteSpace(request.NewPassword))
    {
        return Results.BadRequest(new ApiResponse(false, "Please fill in all fields."));
    }

    bool currentOk = BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash);

    if (!currentOk)
    {
        return Results.BadRequest(new ApiResponse(false, "Current password is incorrect."));
    }

    if (request.NewPassword.Length < 6)
    {
        return Results.BadRequest(new ApiResponse(false, "New password must be at least 6 characters."));
    }

    if (request.CurrentPassword == request.NewPassword)
    {
        return Results.BadRequest(new ApiResponse(false, "New password cannot be the same as the current one."));
    }

    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

    await db.SaveChangesAsync();

    return Results.Ok(new ApiResponse(true, "Password changed successfully."));
});

app.MapGet("/api/reminders/{email}", async (string email, AppDbContext db) =>
{
    email = email.Trim().ToLower();
    DateTime today = DateTime.UtcNow.Date;

    var reminders = await db.Reminders
        .Where(r => r.UserEmail == email && r.ReminderDate >= today)
        .OrderBy(r => r.ReminderDate)
        .ThenBy(r => r.Id)
        .Select(r => new ReminderDto(
            r.Id,
            r.UserEmail,
            r.ReminderDate,
            r.Title,
            r.Description ?? ""
        ))
        .ToListAsync();

    return Results.Ok(reminders);
});

app.MapPost("/api/reminders", async (CreateReminderRequest? request, AppDbContext db) =>
{
    try
    {
        if (request == null)
        {
            return Results.BadRequest(new ApiResponse(false, "Invalid reminder data."));
        }

        string email = request.UserEmail?.Trim().ToLower() ?? "";
        string title = request.Title?.Trim() ?? "";
        string description = request.Description?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.BadRequest(new ApiResponse(false, "User email is required."));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Results.BadRequest(new ApiResponse(false, "Reminder title is required."));
        }

        if (request.ReminderDate == default)
        {
            return Results.BadRequest(new ApiResponse(false, "Reminder date is required."));
        }

        var reminder = new ReminderEntity
        {
            UserEmail = email,
            ReminderDate = request.ReminderDate.Date,
            Title = title,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        db.Reminders.Add(reminder);
        await db.SaveChangesAsync();

        return Results.Ok(new ReminderDto(
            reminder.Id,
            reminder.UserEmail,
            reminder.ReminderDate,
            reminder.Title,
            reminder.Description ?? ""
        ));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString());
    }
});
app.MapDelete("/api/reminders/{id:int}", async (int id, AppDbContext db) =>
{
    var reminder = await db.Reminders.FirstOrDefaultAsync(r => r.Id == id);

    if (reminder == null)
    {
        return Results.NotFound(new ApiResponse(false, "Reminder not found."));
    }

    db.Reminders.Remove(reminder);
    await db.SaveChangesAsync();

    return Results.Ok(new ApiResponse(true, "Reminder deleted."));
});

app.MapDelete("/api/reminders/clear/{email}", async (string email, AppDbContext db) =>
{
    email = email.Trim().ToLower();

    var reminders = await db.Reminders
        .Where(r => r.UserEmail == email)
        .ToListAsync();

    db.Reminders.RemoveRange(reminders);
    await db.SaveChangesAsync();

    return Results.Ok(new ApiResponse(true, "All reminders cleared."));
});

var port = Environment.GetEnvironmentVariable("PORT");

if (!string.IsNullOrWhiteSpace(port))
{
    app.Run($"http://0.0.0.0:{port}");
}
else
{
    app.Run();
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<ReminderEntity> Reminders => Set<ReminderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>().ToTable("users");

        modelBuilder.Entity<UserEntity>().HasKey(u => u.Id);

        modelBuilder.Entity<UserEntity>().Property(u => u.Id).HasColumnName("id");
        modelBuilder.Entity<UserEntity>().Property(u => u.FirstName).HasColumnName("first_name");
        modelBuilder.Entity<UserEntity>().Property(u => u.LastName).HasColumnName("last_name");
        modelBuilder.Entity<UserEntity>().Property(u => u.Email).HasColumnName("email");
        modelBuilder.Entity<UserEntity>().Property(u => u.PasswordHash).HasColumnName("password_hash");
        modelBuilder.Entity<UserEntity>().Property(u => u.Class).HasColumnName("class");
        modelBuilder.Entity<UserEntity>().Property(u => u.Profile).HasColumnName("profile");
        modelBuilder.Entity<UserEntity>().Property(u => u.CountryOfOrigin).HasColumnName("country_of_origin");
        modelBuilder.Entity<UserEntity>().Property(u => u.EducationalInstitute).HasColumnName("educational_institute");
        modelBuilder.Entity<UserEntity>().Property(u => u.CreatedAt).HasColumnName("created_at");

        modelBuilder.Entity<UserEntity>().HasIndex(u => u.Email).IsUnique();

        modelBuilder.Entity<ReminderEntity>().ToTable("reminders");

        modelBuilder.Entity<ReminderEntity>().HasKey(r => r.Id);

        modelBuilder.Entity<ReminderEntity>().Property(r => r.Id).HasColumnName("id");
        modelBuilder.Entity<ReminderEntity>().Property(r => r.UserEmail).HasColumnName("user_email");
        modelBuilder.Entity<ReminderEntity>().Property(r => r.ReminderDate).HasColumnName("reminder_date");
        modelBuilder.Entity<ReminderEntity>().Property(r => r.Title).HasColumnName("title");
        modelBuilder.Entity<ReminderEntity>().Property(r => r.Description).HasColumnName("description");
        modelBuilder.Entity<ReminderEntity>().Property(r => r.CreatedAt).HasColumnName("created_at");

        modelBuilder.Entity<ReminderEntity>().HasIndex(r => r.UserEmail);
        modelBuilder.Entity<ReminderEntity>().HasIndex(r => new { r.UserEmail, r.ReminderDate });
    }
}

public class UserEntity
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Class { get; set; } = "";
    public string Profile { get; set; } = "";
    public string CountryOfOrigin { get; set; } = "";
    public string EducationalInstitute { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class ReminderEntity
{
    public int Id { get; set; }
    public string UserEmail { get; set; } = "";
    public DateTime ReminderDate { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string Class,
    string Profile,
    string CountryOfOrigin,
    string EducationalInstitute
);

public record LoginRequest(
    string Email,
    string Password
);

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string Class,
    string Profile,
    string CountryOfOrigin,
    string EducationalInstitute
);

public record ChangePasswordRequest(
    string Email,
    string CurrentPassword,
    string NewPassword
);

public record UserDto(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string Class,
    string Profile,
    string CountryOfOrigin,
    string EducationalInstitute
);

public record CreateReminderRequest(
    string UserEmail,
    DateTime ReminderDate,
    string Title,
    string Description
);

public record ReminderDto(
    int Id,
    string UserEmail,
    DateTime ReminderDate,
    string Title,
    string Description
);

public record ApiResponse(
    bool Success,
    string Message
);