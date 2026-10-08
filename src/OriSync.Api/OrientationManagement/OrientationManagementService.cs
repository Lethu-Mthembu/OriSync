using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OriSync.Api.Data;
using OriSync.Api.Domain;

namespace OriSync.Api.OrientationManagement;

public sealed partial class OrientationManagementService(
    OriSyncDbContext dbContext,
    TimeProvider timeProvider)
{
    private const string JohannesburgTimeZone = "Africa/Johannesburg";
    private const string DefaultBadgeColor = "#64748B";

    private static readonly IReadOnlyDictionary<string, string> KnownBadgeColors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BLACK"] = "#212121",
            ["BLUE"] = "#1565C0",
            ["BROWN"] = "#6D4C41",
            ["GREEN"] = "#2E7D32",
            ["GREY"] = "#616161",
            ["GRAY"] = "#616161",
            ["ORANGE"] = "#EF6C00",
            ["PINK"] = "#AD1457",
            ["PURPLE"] = "#6A1B9A",
            ["RED"] = "#C62828",
            ["WHITE"] = "#FFFFFF",
            ["YELLOW"] = "#F9A825"
        };

    public async Task<IReadOnlyList<OrientationResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var orientations = await dbContext.Orientations
            .AsNoTracking()
            .Include(item => item.Groups)
            .OrderByDescending(item => item.Year)
            .ToListAsync(cancellationToken);

        return await MapAsync(orientations, cancellationToken);
    }

    public async Task<OrientationResponse> CreateAsync(
        SaveOrientationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateOrientation(request);
        if (await dbContext.Orientations.AnyAsync(
                item => item.Year == request.Year,
                cancellationToken))
        {
            throw Conflict("An orientation already exists for this year.");
        }

        var orientation = new Orientation
        {
            Year = request.Year,
            Name = ResolveOrientationName(request),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            AttendanceOpensAt = request.AttendanceOpensAt,
            AttendanceClosesAt = request.AttendanceClosesAt,
            TimeZoneId = JohannesburgTimeZone,
            IsActive = false,
            RetentionDueAt = CalculateRetentionDueAt(request.EndDate),
            CreatedAt = timeProvider.GetUtcNow()
        };

        dbContext.Orientations.Add(orientation);
        await SaveChangesAsync("The orientation could not be created.", cancellationToken);
        return await GetRequiredAsync(orientation.Id, cancellationToken);
    }

    public async Task<OrientationResponse> UpdateAsync(
        long orientationId,
        SaveOrientationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateOrientation(request);
        var orientation = await FindRequiredAsync(orientationId, cancellationToken);
        if (request.Year != orientation.Year &&
            await dbContext.Orientations.AnyAsync(
                item => item.Year == request.Year && item.Id != orientationId,
                cancellationToken))
        {
            throw Conflict("An orientation already exists for this year.");
        }

        var latestAttendanceDate = await dbContext.AttendanceRecords
            .Where(item => item.OrientationId == orientationId)
            .Select(item => (DateOnly?)item.AttendanceDate)
            .MaxAsync(cancellationToken);

        if (latestAttendanceDate is not null)
        {
            var today = GetJohannesburgToday();
            if (request.Year != orientation.Year || request.StartDate != orientation.StartDate)
            {
                throw Conflict(
                    "The year and start date cannot change after attendance has been created.");
            }

            if (request.AttendanceOpensAt != orientation.AttendanceOpensAt)
            {
                throw Conflict(
                    "The opening time cannot change after attendance has been created.");
            }

            if (request.EndDate != orientation.EndDate &&
                (orientation.EndDate < today ||
                 request.EndDate < today ||
                 request.EndDate < latestAttendanceDate.Value))
            {
                throw Conflict(
                    "A past end date cannot be changed or moved before existing attendance.");
            }

            if (request.AttendanceClosesAt != orientation.AttendanceClosesAt &&
                orientation.EndDate < today)
            {
                throw Conflict("The closing time cannot change after the orientation has ended.");
            }
        }

        orientation.Year = request.Year;
        orientation.Name = ResolveOrientationName(request);
        orientation.StartDate = request.StartDate;
        orientation.EndDate = request.EndDate;
        orientation.AttendanceOpensAt = request.AttendanceOpensAt;
        orientation.AttendanceClosesAt = request.AttendanceClosesAt;
        orientation.RetentionDueAt = CalculateRetentionDueAt(request.EndDate);

        await SaveChangesAsync("The orientation could not be updated.", cancellationToken);
        return await GetRequiredAsync(orientationId, cancellationToken);
    }

    public async Task<OrientationResponse> ActivateAsync(
        long orientationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var orientation = await FindRequiredAsync(orientationId, cancellationToken);
        if (!await dbContext.Groups.AnyAsync(
                item => item.OrientationId == orientationId && item.IsActive,
                cancellationToken))
        {
            throw Conflict("Create at least one active group before activating the orientation.");
        }

        await dbContext.Orientations
            .Where(item => item.IsActive && item.Id != orientationId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.IsActive, false),
                cancellationToken);
        orientation.IsActive = true;
        await SaveChangesAsync("The orientation could not be activated.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetRequiredAsync(orientationId, cancellationToken);
    }

    public async Task<OrientationResponse> DeactivateAsync(
        long orientationId,
        CancellationToken cancellationToken)
    {
        var orientation = await FindRequiredAsync(orientationId, cancellationToken);
        orientation.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetRequiredAsync(orientationId, cancellationToken);
    }

    public async Task<OrientationResponse> CreateGroupAsync(
        long orientationId,
        SaveOrientationGroupRequest request,
        CancellationToken cancellationToken)
    {
        _ = await FindRequiredAsync(orientationId, cancellationToken);
        var (name, normalizedName, badgeColor) = ResolveGroup(request);
        if (await dbContext.Groups.AnyAsync(
                item => item.OrientationId == orientationId &&
                    item.NormalizedName == normalizedName,
                cancellationToken))
        {
            throw Conflict("A group with this name already exists in the orientation.");
        }

        dbContext.Groups.Add(new OrientationGroup
        {
            OrientationId = orientationId,
            Name = name,
            NormalizedName = normalizedName,
            BadgeColor = badgeColor,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow()
        });
        await SaveChangesAsync("The group could not be created.", cancellationToken);
        return await GetRequiredAsync(orientationId, cancellationToken);
    }

    public async Task<OrientationResponse> UpdateGroupAsync(
        long orientationId,
        long groupId,
        SaveOrientationGroupRequest request,
        CancellationToken cancellationToken)
    {
        var group = await FindGroupRequiredAsync(orientationId, groupId, cancellationToken);
        var (name, normalizedName, badgeColor) = ResolveGroup(request);
        var nameChanged = !string.Equals(group.Name, name, StringComparison.Ordinal);
        if (nameChanged && await HasAnyGroupReferenceAsync(groupId, cancellationToken))
        {
            throw Conflict(
                "A group name cannot change after mentors, students or attendance reference it.");
        }

        if (nameChanged && await dbContext.Groups.AnyAsync(
                item => item.OrientationId == orientationId &&
                    item.NormalizedName == normalizedName &&
                    item.Id != groupId,
                cancellationToken))
        {
            throw Conflict("A group with this name already exists in the orientation.");
        }

        group.Name = name;
        group.NormalizedName = normalizedName;
        group.BadgeColor = badgeColor;
        await SaveChangesAsync("The group could not be updated.", cancellationToken);
        return await GetRequiredAsync(orientationId, cancellationToken);
    }

    public async Task<OrientationResponse> ActivateGroupAsync(
        long orientationId,
        long groupId,
        CancellationToken cancellationToken)
    {
        var group = await FindGroupRequiredAsync(orientationId, groupId, cancellationToken);
        group.IsActive = true;
        group.DeactivatedAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetRequiredAsync(orientationId, cancellationToken);
    }

    public async Task<OrientationResponse> DeactivateGroupAsync(
        long orientationId,
        long groupId,
        CancellationToken cancellationToken)
    {
        var group = await FindGroupRequiredAsync(orientationId, groupId, cancellationToken);
        var hasActiveMentors = await dbContext.Accounts.AnyAsync(
            item => item.IsActive && item.CurrentGroupId == groupId,
            cancellationToken);
        var hasCurrentStudents = await dbContext.StudentEnrollments.AnyAsync(
            item => item.RemovedAt == null &&
                (item.CurrentGroupId == groupId || item.PendingGroupId == groupId),
            cancellationToken);
        if (hasActiveMentors || hasCurrentStudents)
        {
            throw Conflict(
                "Move or remove current mentors and students before deactivating this group.");
        }

        group.IsActive = false;
        group.DeactivatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetRequiredAsync(orientationId, cancellationToken);
    }

    public async Task DeleteGroupAsync(
        long orientationId,
        long groupId,
        CancellationToken cancellationToken)
    {
        var group = await FindGroupRequiredAsync(orientationId, groupId, cancellationToken);
        if (await HasAnyGroupReferenceAsync(groupId, cancellationToken))
        {
            throw Conflict(
                "This group is already in use and must be deactivated instead of deleted.");
        }

        dbContext.Groups.Remove(group);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<OrientationResponse> GetRequiredAsync(
        long orientationId,
        CancellationToken cancellationToken)
    {
        var orientation = await dbContext.Orientations
            .AsNoTracking()
            .Include(item => item.Groups)
            .SingleOrDefaultAsync(item => item.Id == orientationId, cancellationToken) ??
            throw NotFound("Orientation not found.");
        return (await MapAsync([orientation], cancellationToken)).Single();
    }

    private async Task<IReadOnlyList<OrientationResponse>> MapAsync(
        IReadOnlyCollection<Orientation> orientations,
        CancellationToken cancellationToken)
    {
        var groupIds = orientations.SelectMany(item => item.Groups).Select(item => item.Id).ToArray();
        var referencedGroupIds = new HashSet<long>();
        if (groupIds.Length > 0)
        {
            referencedGroupIds.UnionWith(await dbContext.Accounts
                .Where(item => item.CurrentGroupId != null && groupIds.Contains(item.CurrentGroupId.Value))
                .Select(item => item.CurrentGroupId!.Value)
                .ToListAsync(cancellationToken));
            referencedGroupIds.UnionWith(await dbContext.StudentEnrollments
                .Where(item => item.CurrentGroupId != null && groupIds.Contains(item.CurrentGroupId.Value))
                .Select(item => item.CurrentGroupId!.Value)
                .ToListAsync(cancellationToken));
            referencedGroupIds.UnionWith(await dbContext.StudentEnrollments
                .Where(item => item.PendingGroupId != null && groupIds.Contains(item.PendingGroupId.Value))
                .Select(item => item.PendingGroupId!.Value)
                .ToListAsync(cancellationToken));
            referencedGroupIds.UnionWith(await dbContext.AttendanceRecords
                .Where(item => groupIds.Contains(item.GroupId))
                .Select(item => item.GroupId)
                .Distinct()
                .ToListAsync(cancellationToken));
        }

        return orientations.Select(orientation => new OrientationResponse(
            orientation.Id,
            orientation.Year,
            orientation.Name,
            orientation.StartDate,
            orientation.EndDate,
            orientation.AttendanceOpensAt,
            orientation.AttendanceClosesAt,
            orientation.TimeZoneId,
            orientation.IsActive,
            orientation.RetentionDueAt,
            GetOperatingDates(orientation.StartDate, orientation.EndDate),
            orientation.Groups
                .OrderBy(group => group.Name, StringComparer.Ordinal)
                .Select(group => new OrientationGroupResponse(
                    group.Id,
                    group.Name,
                    group.BadgeColor,
                    group.IsActive,
                    !referencedGroupIds.Contains(group.Id)))
                .ToArray()))
            .ToArray();
    }

    private async Task<Orientation> FindRequiredAsync(
        long orientationId,
        CancellationToken cancellationToken) =>
        await dbContext.Orientations.SingleOrDefaultAsync(
            item => item.Id == orientationId,
            cancellationToken) ?? throw NotFound("Orientation not found.");

    private async Task<OrientationGroup> FindGroupRequiredAsync(
        long orientationId,
        long groupId,
        CancellationToken cancellationToken) =>
        await dbContext.Groups.SingleOrDefaultAsync(
            item => item.Id == groupId && item.OrientationId == orientationId,
            cancellationToken) ?? throw NotFound("Group not found.");

    private async Task<bool> HasAnyGroupReferenceAsync(
        long groupId,
        CancellationToken cancellationToken) =>
        await dbContext.Accounts.AnyAsync(item => item.CurrentGroupId == groupId, cancellationToken) ||
        await dbContext.StudentEnrollments.AnyAsync(
            item => item.CurrentGroupId == groupId || item.PendingGroupId == groupId,
            cancellationToken) ||
        await dbContext.AttendanceRecords.AnyAsync(item => item.GroupId == groupId, cancellationToken);

    private async Task SaveChangesAsync(string conflictDetail, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw Conflict(conflictDetail);
        }
    }

    private static void ValidateOrientation(SaveOrientationRequest request)
    {
        if (request.Year is < 2020 or > 2200 ||
            request.StartDate.Year != request.Year ||
            request.EndDate.Year != request.Year)
        {
            throw Invalid("The dates and selected year must describe the same year.");
        }

        if (request.StartDate > request.EndDate)
        {
            throw Invalid("The start date must be on or before the end date.");
        }

        if (request.AttendanceOpensAt >= request.AttendanceClosesAt)
        {
            throw Invalid("The attendance opening time must be before the closing time.");
        }

        if (GetOperatingDates(request.StartDate, request.EndDate).Count == 0)
        {
            throw Invalid("The orientation range must contain at least one weekday.");
        }

        if (request.Name?.Trim().Length > 120)
        {
            throw Invalid("The orientation name cannot exceed 120 characters.");
        }
    }

    private static string ResolveOrientationName(SaveOrientationRequest request) =>
        string.IsNullOrWhiteSpace(request.Name)
            ? $"First Year Orientation {request.Year.ToString(CultureInfo.InvariantCulture)}"
            : request.Name.Trim();

    private static (string Name, string NormalizedName, string BadgeColor) ResolveGroup(
        SaveOrientationGroupRequest request)
    {
        var name = request.Name?.Trim().ToUpperInvariant() ?? string.Empty;
        if (name.Length is < 1 or > 40)
        {
            throw Invalid("The group name must contain between 1 and 40 characters.");
        }

        var suppliedColor = request.BadgeColor?.Trim().ToUpperInvariant();
        var badgeColor = !string.IsNullOrWhiteSpace(suppliedColor)
            ? suppliedColor
            : KnownBadgeColors.GetValueOrDefault(name);
        if (badgeColor is null)
        {
            throw Invalid("Choose a badge colour for a group without a recognised colour name.");
        }

        if (!BadgeColorPattern().IsMatch(badgeColor))
        {
            throw Invalid("The badge colour must be a six-digit hexadecimal colour.");
        }

        return (name, name.ToLowerInvariant(), badgeColor);
    }

    private static DateTimeOffset CalculateRetentionDueAt(DateOnly endDate)
    {
        var localDueAt = DateTime.SpecifyKind(
            endDate.AddMonths(6).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        var utcDueAt = TimeZoneInfo.ConvertTimeToUtc(
            localDueAt,
            TimeZoneInfo.FindSystemTimeZoneById(JohannesburgTimeZone));
        return new DateTimeOffset(utcDueAt);
    }

    private DateOnly GetJohannesburgToday()
    {
        var localNow = TimeZoneInfo.ConvertTime(
            timeProvider.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById(JohannesburgTimeZone));
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private static List<DateOnly> GetOperatingDates(DateOnly startDate, DateOnly endDate)
    {
        var dates = new List<DateOnly>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                dates.Add(date);
            }
        }

        return dates;
    }

    private static OrientationManagementException Invalid(string detail) => new(
        StatusCodes.Status400BadRequest,
        "Invalid orientation configuration.",
        detail);

    private static OrientationManagementException Conflict(string detail) => new(
        StatusCodes.Status409Conflict,
        "Orientation configuration conflict.",
        detail);

    private static OrientationManagementException NotFound(string detail) => new(
        StatusCodes.Status404NotFound,
        "Orientation configuration not found.",
        detail);

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex BadgeColorPattern();
}
