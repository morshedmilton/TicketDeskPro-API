namespace TicketDeskPro.Shared.Constants;

public static class AppRoles
{
    public const string Customer = "Customer";
    public const string Agent = "Agent";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";

    public static readonly IReadOnlyList<string> All = [Customer, Agent, Admin, SuperAdmin];
}
