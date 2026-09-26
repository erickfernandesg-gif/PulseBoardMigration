-- Preserva o vínculo histórico entre fatura e contrato.
drop policy if exists contracts_delete on public.client_contracts;
create policy contracts_delete on public.client_contracts
for delete to authenticated
using (
  and not exists (
    select 1
    from public.billing_invoices invoice
    where invoice.contract_id = client_contracts.id
  )
  and (
    (board_id is not null and (select private.can_edit_board(client_contracts.board_id)))
    or (board_id is null and (select public.is_manager()))
  )
);
