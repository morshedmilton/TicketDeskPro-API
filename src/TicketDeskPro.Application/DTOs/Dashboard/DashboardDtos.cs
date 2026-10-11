using TicketDeskPro.Domain.Enums;

namespace TicketDeskPro.Application.DTOs.Dashboard;

public record OpenByPriorityDto(int Low, int Medium, int High, int Urgent)
{
    public int Total => Low + Medium + High + Urgent;
}

/// <summary>US-8.01 headline numbers (Redis-cached, 30s).</summary>
public record DashboardKpisDto(
    OpenByPriorityDto OpenByPriority,
    int ResolvedLast7Days,
    double AvgFirstResponseMinutes,
    double AvgResolutionMinutes,
    double SlaComplianceRate7Days,
    DateTime GeneratedAt);

public record TicketTrendPointDto(DateTime Day, int Created, int Resolved);

public record CategoryDistributionPointDto(string CategoryName, int Count);

public record AgentPerformanceDto(
    Guid UserId,
    string AgentName,
    int OpenAssigned,
    int ResolvedLast30Days,
    double AvgResolutionMinutes);

/// <summary>Agent's personal queue snapshot (US-8.02/BR-020).</summary>
public record AgentWorkloadDto(string AgentName, int OpenAssigned)
{
    // ponytail: agent-workload view (US-6.04) kept minimal; richer dashboards land with the frontend.
}
