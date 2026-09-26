using PulseBoardMigration.Models;
using PulseBoardMigration.Domain;

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
        var invoiceItems = await client.From<BillingInvoiceItem>().Get();
        var monthStart = DateTime.ParseExact($"{selectedMonth}-01", "yyyy-MM-dd", null);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        return new BillingViewModel
        {
            Month = selectedMonth,
            CurrentUser = current.Models.FirstOrDefault(),
            Logs = logs.Models.Where(x => x.LogDate.ToString("yyyy-MM") == selectedMonth).OrderByDescending(x => x.LogDate).ToList(),
            Tasks = tasks.Models.ToList(),
            Boards = boards.Models.ToList(),
            Profiles = profiles.Models.ToList(),
            Clients = clients.Models.ToList(),
            Contracts = contracts.Models.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.CreatedAt).ToList(),
            ContractIdsWithInvoices = invoices.Models
                .Where(x => x.ContractId.HasValue)
                .Select(x => x.ContractId!.Value)
                .ToHashSet(),
            Invoices = invoices.Models
                .Where(x => x.PeriodStart.Date <= monthEnd && x.PeriodEnd.Date >= monthStart)
                .OrderByDescending(x => x.CreatedAt)
                .ToList(),
            InvoiceItems = invoiceItems.Models.ToList()
        };
    }

    public async Task SaveContractAsync(ClientContract contract, Guid userId)
    {
        if (contract.ClientId == Guid.Empty || contract.BoardId == null || string.IsNullOrWhiteSpace(contract.Name))
            throw new InvalidOperationException("Projeto, cliente e nome do contrato são obrigatórios.");
        if (!BillingRules.IsAutomaticBillingContract(contract.ContractType))
            throw new InvalidOperationException("No momento, o faturamento automático aceita somente contratos por hora.");
        if (contract.EndsOn.HasValue && contract.EndsOn.Value.Date < contract.StartsOn.Date)
            throw new InvalidOperationException("O término do contrato não pode ser anterior ao início.");

        var client = await _clientFactory.CreateForCurrentUserAsync();
        var current = await client.From<Profile>().Where(x => x.Id == userId).Single();
        if (current?.Role is not ("manager" or "admin"))
            throw new InvalidOperationException("Você não possui permissão para administrar contratos.");
        var board = await client.From<Board>().Where(x => x.Id == contract.BoardId.Value).Single();
        if (board == null)
            throw new InvalidOperationException("Projeto não encontrado ou sem permissão de gestão.");
        contract.Name = contract.Name.Trim();
        contract.BillingRate = Math.Max(0, contract.BillingRate);
        contract.BudgetAmount = contract.BudgetAmount.HasValue ? Math.Max(0, contract.BudgetAmount.Value) : null;
        contract.IncludedMinutes = contract.IncludedMinutes.HasValue ? Math.Max(0, contract.IncludedMinutes.Value) : null;
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
            .Set(x => x.BoardId, contract.BoardId)
            .Set(x => x.ClientId, contract.ClientId)
            .Set(x => x.BillingRate, contract.BillingRate)
            .Set(x => x.BudgetAmount, contract.BudgetAmount)
            .Set(x => x.IncludedMinutes, contract.IncludedMinutes)
            .Set(x => x.StartsOn, contract.StartsOn)
            .Set(x => x.EndsOn, contract.EndsOn)
            .Set(x => x.IsActive, contract.IsActive)
            .Update();
    }

    public async Task DeleteContractAsync(Guid contractId, Guid userId)
    {
        if (contractId == Guid.Empty) throw new InvalidOperationException("Contrato inválido.");

        var client = await _clientFactory.CreateForCurrentUserAsync();
        var current = await client.From<Profile>().Where(x => x.Id == userId).Single();
        if (current?.Role is not ("manager" or "admin"))
            throw new InvalidOperationException("Você não possui permissão para excluir contratos.");

        var contract = await client.From<ClientContract>().Where(x => x.Id == contractId).Single();
        if (contract == null)
            throw new InvalidOperationException("Contrato não encontrado ou sem permissão.");

        var invoices = await client.From<BillingInvoice>().Get();
        if (invoices.Models.Any(x => x.ContractId == contractId))
            throw new InvalidOperationException("Este contrato possui histórico de faturamento e não pode ser excluído. Desative-o para impedir novos apontamentos.");

        await client.From<ClientContract>().Where(x => x.Id == contractId).Delete();
    }

    public async Task ReviewTimeLogAsync(Guid logId, Guid reviewerId, bool approve)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("review_billing_time_log", new
        {
            p_log_id = logId,
            p_approve = approve,
            p_reviewer_id = reviewerId
        });
    }

    public async Task DeletePendingTimeLogAsync(Guid logId, Guid requesterId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("delete_pending_billing_time_log", new
        {
            p_log_id = logId,
            p_requester_id = requesterId
        });
    }

    public async Task ReopenTimeLogAsync(Guid logId, Guid requesterId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("reopen_billing_time_log", new
        {
            p_log_id = logId,
            p_requester_id = requesterId
        });
    }

    public async Task ReverseInvoiceAndDeleteTimeLogAsync(Guid logId, Guid requesterId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("reverse_invoice_and_delete_time_log", new
        {
            p_log_id = logId,
            p_requester_id = requesterId
        });
    }

    public async Task DeleteCancelledInvoiceAsync(Guid invoiceId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("delete_cancelled_billing_invoice", new { p_invoice_id = invoiceId });
    }

    public async Task CleanDemoScenarioAsync(Guid invoiceId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("clean_demo_billing_scenario", new { p_invoice_id = invoiceId });
    }

    public async Task<BillingInvoice> GenerateInvoiceAsync(
        Guid clientId,
        Guid boardId,
        Guid creatorId,
        DateTime periodStart,
        DateTime periodEnd,
        DateTime? dueDate)
    {
        if (clientId == Guid.Empty || boardId == Guid.Empty)
            throw new InvalidOperationException("Selecione o projeto e o cliente da cobrança.");
        if (periodEnd.Date < periodStart.Date) throw new InvalidOperationException("Período de faturamento inválido.");
        if (dueDate.HasValue && dueDate.Value.Date < periodEnd.Date)
            throw new InvalidOperationException("O vencimento deve ser igual ou posterior ao fim do período.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        try
        {
            var invoiceId = await client.Rpc<Guid>("generate_billing_invoice", new
            {
                p_client_id = clientId,
                p_board_id = boardId,
                p_creator_id = creatorId,
                p_period_start = periodStart.Date,
                p_period_end = periodEnd.Date,
                p_due_date = dueDate?.Date
            });
            return await client.From<BillingInvoice>().Where(x => x.Id == invoiceId).Single()
                ?? throw new InvalidOperationException("Não foi possível recuperar a fatura criada.");
        }
        catch (Postgrest.Exceptions.PostgrestException exception)
            when (exception.Content?.Contains("PGRST202", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException(
                "A transação de faturamento não está instalada. Execute as migrações do banco antes de emitir faturas.", exception);
        }
    }

    public async Task UpdateInvoiceStatusAsync(Guid invoiceId, string status)
    {
        if (status is not ("issued" or "paid" or "cancelled"))
            throw new InvalidOperationException("Situação de fatura inválida.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("update_billing_invoice_status", new
        {
            p_invoice_id = invoiceId,
            p_status = status
        });
    }
}
#pragma warning restore CS8603
