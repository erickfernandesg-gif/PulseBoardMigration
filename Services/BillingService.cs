using PulseBoardMigration.Models;

#pragma warning disable CS8603
namespace PulseBoardMigration.Services;

public class BillingService
{
    private readonly SupabaseClientFactory _clientFactory;

    public BillingService(SupabaseClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<BillingViewModel?> GetBillingAsync(Guid userId, string? month)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var current = await client.From<Profile>().Where(x => x.Id == userId).Get();
        if (current.Models.FirstOrDefault()?.Role is not ("manager" or "admin")) return null;

        var selectedMonth = DateTime.TryParse($"{month}-01", out var parsed)
            ? parsed.ToString("yyyy-MM")
            : DateTime.UtcNow.ToString("yyyy-MM");
        var logs = await client.From<TimeLog>().Get();
        var tasks = await client.From<PulseTask>().Get();
        var boards = await client.From<Board>().Get();
        var profiles = await client.From<Profile>().Get();
        var clients = await client.From<ClientAccount>().Get();
        var contracts = await client.From<ClientContract>().Get();
        var invoices = await client.From<BillingInvoice>().Get();

        return new BillingViewModel
        {
            Month = selectedMonth,
            Logs = logs.Models.Where(x => x.LogDate.ToString("yyyy-MM") == selectedMonth).OrderByDescending(x => x.LogDate).ToList(),
            Tasks = tasks.Models.ToList(),
            Boards = boards.Models.ToList(),
            Profiles = profiles.Models.ToList(),
            Clients = clients.Models.ToList(),
            Contracts = contracts.Models.OrderByDescending(x => x.CreatedAt).ToList(),
            Invoices = invoices.Models.OrderByDescending(x => x.CreatedAt).ToList()
        };
    }

    public async Task SaveContractAsync(ClientContract contract)
    {
        if (contract.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(contract.Name))
            throw new InvalidOperationException("Cliente e nome do contrato são obrigatórios.");
        if (contract.ContractType is not ("hourly" or "fixed" or "retainer" or "hour_bank" or "internal"))
            throw new InvalidOperationException("Tipo de contrato inválido.");

        var client = await _clientFactory.CreateForCurrentUserAsync();
        contract.Name = contract.Name.Trim();
        contract.BillingRate = Math.Max(0, contract.BillingRate);
        contract.CreatedAt = DateTime.UtcNow;
        if (contract.Id == Guid.Empty)
        {
            await client.From<ClientContract>().Insert(contract);
            return;
        }

        await client.From<ClientContract>()
            .Where(x => x.Id == contract.Id)
            .Set(x => x.Name, contract.Name)
            .Set(x => x.ContractType, contract.ContractType)
            .Set(x => x.BillingRate, contract.BillingRate)
            .Set(x => x.BudgetAmount, contract.BudgetAmount)
            .Set(x => x.IncludedMinutes, contract.IncludedMinutes)
            .Set(x => x.StartsOn, contract.StartsOn)
            .Set(x => x.EndsOn, contract.EndsOn)
            .Set(x => x.IsActive, contract.IsActive)
            .Update();
    }

    public async Task ReviewTimeLogAsync(Guid logId, Guid reviewerId, bool approve)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<TimeLog>()
            .Where(x => x.Id == logId)
            .Set(x => x.ApprovalStatus, approve ? "approved" : "rejected")
            .Set(x => x.ApprovedBy, reviewerId)
            .Set(x => x.ApprovedAt, DateTime.UtcNow)
            .Update();
    }

    public async Task<BillingInvoice> GenerateInvoiceAsync(
        Guid clientId,
        Guid creatorId,
        DateTime periodStart,
        DateTime periodEnd,
        DateTime? dueDate)
    {
        if (periodEnd.Date < periodStart.Date) throw new InvalidOperationException("Período de faturamento inválido.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var tasks = await client.From<PulseTask>().Get();
        var logs = await client.From<TimeLog>().Get();
        var contracts = await client.From<ClientContract>().Get();
        var taskIds = tasks.Models.Where(x => x.ClientId == clientId).Select(x => x.Id).ToHashSet();
        var selectedLogs = logs.Models.Where(x =>
            taskIds.Contains(x.TaskId) && x.IsBillable && x.ApprovalStatus == "approved" &&
            x.BillingStatus == "unbilled" && x.LogDate.Date >= periodStart.Date && x.LogDate.Date <= periodEnd.Date).ToList();
        if (selectedLogs.Count == 0) throw new InvalidOperationException("Não existem horas aprovadas e não faturadas nesse período.");

        var contract = contracts.Models.FirstOrDefault(x => x.ClientId == clientId && x.IsActive &&
            x.StartsOn.Date <= periodEnd.Date && (!x.EndsOn.HasValue || x.EndsOn.Value.Date >= periodStart.Date));
        var total = selectedLogs.Sum(x => x.BillableAmount);
        var invoice = new BillingInvoice
        {
            ClientId = clientId,
            ContractId = contract?.Id,
            Reference = $"PB-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Status = "draft",
            PeriodStart = periodStart.Date,
            PeriodEnd = periodEnd.Date,
            DueDate = dueDate?.Date,
            Subtotal = total,
            Total = total,
            CreatedBy = creatorId,
            CreatedAt = DateTime.UtcNow
        };
        var inserted = await client.From<BillingInvoice>().Insert(invoice);
        var created = inserted.Models.FirstOrDefault() ?? throw new InvalidOperationException("Não foi possível criar a fatura.");

        try
        {
            foreach (var log in selectedLogs)
            {
                var task = tasks.Models.First(x => x.Id == log.TaskId);
                await client.From<BillingInvoiceItem>().Insert(new BillingInvoiceItem
                {
                    InvoiceId = created.Id,
                    TimeLogId = log.Id,
                    Description = $"{task.Title} - {log.LogDate:dd/MM/yyyy}",
                    Minutes = log.Minutes,
                    UnitRate = log.BillingRateSnapshot,
                    Amount = log.BillableAmount,
                    CreatedAt = DateTime.UtcNow
                });
                await client.From<TimeLog>()
                    .Where(x => x.Id == log.Id)
                    .Set(x => x.BillingStatus, "invoiced")
                    .Set(x => x.InvoiceId, created.Id)
                    .Update();
            }
        }
        catch
        {
            foreach (var log in selectedLogs)
            {
                await client.From<TimeLog>()
                    .Where(x => x.Id == log.Id)
                    .Set(x => x.BillingStatus, "unbilled")
                    .Set(x => x.InvoiceId, null)
                    .Update();
            }
            await client.From<BillingInvoice>().Where(x => x.Id == created.Id).Delete();
            throw;
        }

        return created;
    }

    public async Task UpdateInvoiceStatusAsync(Guid invoiceId, string status)
    {
        if (status is not ("draft" or "issued" or "paid" or "cancelled"))
            throw new InvalidOperationException("Situação de fatura inválida.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<BillingInvoice>()
            .Where(x => x.Id == invoiceId)
            .Set(x => x.Status, status)
            .Update();
    }
}
#pragma warning restore CS8603
