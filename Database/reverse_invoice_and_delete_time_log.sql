-- Estorna uma fatura emitida e exclui um apontamento dela de forma auditável.
-- Faturas pagas não podem ser alteradas por este fluxo.

create or replace function private.reverse_invoice_and_delete_time_log_impl(p_log_id uuid, p_requester_id uuid)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
  target public.time_logs%rowtype;
  invoice public.billing_invoices%rowtype;
  task_board uuid;
  actor uuid := (select auth.uid());
  released_count integer;
begin
  if actor is null or actor <> p_requester_id then
    raise exception 'Solicitante inválido.' using errcode = '42501';
  end if;

  select l.* into target
  from public.time_logs l
  where l.id = p_log_id
  for update;

  select t.board_id into task_board
  from public.tasks t
  where t.id = target.task_id;

  select * into invoice
  from public.billing_invoices
  where id = target.invoice_id
  for update;

  if target.id is null or task_board is null or not private.can_edit_board(task_board) then
    raise exception 'Sem permissão para estornar este apontamento.' using errcode = '42501';
  end if;

  if invoice.id is null or target.billing_status <> 'invoiced' or invoice.status <> 'issued' then
    raise exception 'Somente apontamentos de faturas emitidas podem ser estornados por este fluxo. Faturas pagas exigem reembolso financeiro.' using errcode = '42501';
  end if;

  perform set_config('app.billing_operation', 'cancel_draft', true);
  update public.time_logs l
  set billing_status = 'unbilled', invoice_id = null
  from public.billing_invoice_items i
  where i.invoice_id = invoice.id and i.time_log_id = l.id and l.invoice_id = invoice.id;
  get diagnostics released_count = row_count;

  update public.billing_invoices
  set status = 'cancelled', status_changed_by = actor, status_changed_at = now(), cancelled_at = now()
  where id = invoice.id;

  insert into public.billing_invoice_events(invoice_id, actor_id, event_type, details)
  values (invoice.id, actor, 'cancelled', jsonb_build_object('reason', 'time_log_deleted', 'time_log_id', target.id, 'released_count', released_count));

  insert into public.activity_log(task_id, board_id, user_id, action, details)
  values (target.task_id, task_board, actor, 'billing_invoice_reversed_time_log_deleted', jsonb_build_object('invoice_id', invoice.id, 'released_count', released_count));

  delete from public.time_logs where id = target.id;
end;
$$;

create or replace function public.reverse_invoice_and_delete_time_log(p_log_id uuid, p_requester_id uuid)
returns void
language sql
security definer
set search_path = ''
as $$
  select private.reverse_invoice_and_delete_time_log_impl(p_log_id, p_requester_id);
$$;

revoke execute on function private.reverse_invoice_and_delete_time_log_impl(uuid, uuid), public.reverse_invoice_and_delete_time_log(uuid, uuid) from public, anon;
grant execute on function public.reverse_invoice_and_delete_time_log(uuid, uuid) to authenticated, service_role;
