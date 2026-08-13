namespace PulseBoardMigration.Security;

public static class PulsePolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string ManagerOrAdmin = "ManagerOrAdmin";
    public const string FinanceAccess = "FinanceAccess";
}
